using System.Globalization;
using System.IO.Pipes;
using System.Text;
using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;

namespace ToolBox.PluginWorker;

public static partial class WorkerEntryPoint
{
private static async Task<WorkerRequestResult> HandleRequestAsync(
        WorkerMessage message,
        WorkerArguments arguments,
        InProcessPluginRuntime pluginRuntime,
        LoadedInProcessPlugin? loadedPlugin,
        CancellationToken cancellationToken)
    {
        var requestId = message.RequestId ?? string.Empty;
        var operation = message.Operation?.Trim().ToLowerInvariant();
        var shutdownOptions = ParseShutdownOptions(message.Payload);

        switch (operation)
        {
            case "start":
                if (loadedPlugin is not null)
                {
                    return await ErrorResultAsync(
                        arguments,
                        requestId,
                        "PLUGIN_ALREADY_STARTED",
                        "The Worker already has a loaded plugin.")
                        .ConfigureAwait(false);
                }

                try
                {
                    var discovered = pluginRuntime.DiscoverSingle(arguments.PluginDirectory);
                    loadedPlugin = pluginRuntime.Load(discovered, ToolBox.PluginSdk.PluginExecutionMode.OutOfProcess);
                    await loadedPlugin.StartAsync(cancellationToken).ConfigureAwait(false);

                    return new WorkerRequestResult(
                        loadedPlugin,
                        ShouldShutdown: false,
                        ExitCode: 0,
                        Response: WorkerProtocol.CreateResponse(
                            arguments.LaunchId,
                            requestId,
                            "start",
                            "started"),
                        Event: WorkerProtocol.CreateEvent(
                            arguments.LaunchId,
                            "plugin.started",
                            "started"));
                }
                catch (Exception exception)
                {
                    return await ErrorResultAsync(
                        arguments,
                        requestId,
                        GetRequestErrorCode(exception, "PLUGIN_START_FAILED", cancellationToken),
                        exception.Message)
                        .ConfigureAwait(false);
                }

            case "ui.snapshot":
                return CreateUiSnapshotResult(
                    arguments,
                    requestId,
                    loadedPlugin,
                    provider => provider.GetSnapshot());

            case "ui.action":
                return await ExecuteUiRequestAsync(
                        message,
                        arguments,
                        requestId,
                        loadedPlugin,
                        static (provider, request, cancellationToken) =>
                            provider.ExecuteAsync(request.ActionId, request.Argument, cancellationToken),
                        cancellationToken)
                    .ConfigureAwait(false);

            case "ui.input":
                return await ExecuteInputRequestAsync(
                        message,
                        arguments,
                        requestId,
                        loadedPlugin,
                        cancellationToken)
                    .ConfigureAwait(false);

            case "stop":
                if (loadedPlugin is null)
                {
                    return new WorkerRequestResult(
                        null,
                        ShouldShutdown: false,
                        ExitCode: 0,
                        Response: WorkerProtocol.CreateResponse(
                            arguments.LaunchId,
                            requestId,
                            "stop",
                            "already-stopped"));
                }

                try
                {
                    await loadedPlugin.StopAndUnloadAsync(shutdownOptions, cancellationToken).ConfigureAwait(false);
                    return new WorkerRequestResult(
                        null,
                        ShouldShutdown: false,
                        ExitCode: 0,
                        Response: WorkerProtocol.CreateResponse(
                            arguments.LaunchId,
                            requestId,
                            "stop",
                            "stopped"),
                        Event: WorkerProtocol.CreateEvent(
                            arguments.LaunchId,
                            "plugin.stopped",
                            "stopped"));
                }
                catch (Exception exception)
                {
                    return await ErrorResultAsync(
                        arguments,
                        requestId,
                        GetRequestErrorCode(exception, "PLUGIN_STOP_FAILED", cancellationToken),
                        exception.Message)
                        .ConfigureAwait(false);
                }

            case "shutdown":
                if (loadedPlugin is not null)
                {
                    try
                    {
                        await loadedPlugin.StopAndUnloadAsync(shutdownOptions, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        return await ErrorResultAsync(
                                arguments,
                                requestId,
                                GetRequestErrorCode(exception, "PLUGIN_STOP_FAILED", cancellationToken),
                                exception.Message,
                                shouldShutdown: true,
                                exitCode: 12)
                            .ConfigureAwait(false);
                    }
                }

                return new WorkerRequestResult(
                    null,
                    ShouldShutdown: true,
                    ExitCode: 0,
                    Response: WorkerProtocol.CreateResponse(
                        arguments.LaunchId,
                        requestId,
                        "shutdown",
                        "shutdown"),
                    Event: WorkerProtocol.CreateEvent(
                        arguments.LaunchId,
                        "worker.shutdown",
                        "shutdown"));

            default:
                return await ErrorResultAsync(
                        arguments,
                        requestId,
                        "WORKER_OPERATION_UNSUPPORTED",
                        $"Worker operation '{message.Operation}' is not supported.")
                    .ConfigureAwait(false);
        }
    }

    private static Task<WorkerRequestResult> ErrorResultAsync(
        WorkerArguments arguments,
        string requestId,
        string errorCode,
        string errorMessage,
        LoadedInProcessPlugin? loadedPlugin = null,
        bool shouldShutdown = false,
        int exitCode = 0)
    {
        // The main loop writes the response after this method returns. Keeping the
        // error as a result avoids concurrent writes on the single control channel.
        return Task.FromResult(new WorkerRequestResult(
            loadedPlugin,
            shouldShutdown,
            exitCode,
            WorkerProtocol.CreateError(arguments.LaunchId, requestId, errorCode, errorMessage)));
    }

    private static WorkerRequestResult CreateUiSnapshotResult(
        WorkerArguments arguments,
        string requestId,
        LoadedInProcessPlugin? loadedPlugin,
        Func<IPluginUiProvider, PluginUiSnapshot> getSnapshot)
    {
        if (loadedPlugin is null)
        {
            return new WorkerRequestResult(
                null,
                ShouldShutdown: false,
                ExitCode: 0,
                Response: WorkerProtocol.CreateError(
                    arguments.LaunchId,
                    requestId,
                    "PLUGIN_NOT_RUNNING",
                    "The plugin must be running before its controls can be used."));
        }

        try
        {
            var provider = loadedPlugin.GetCapability<IPluginUiProvider>();
            if (provider is null)
            {
                return new WorkerRequestResult(
                    loadedPlugin,
                    ShouldShutdown: false,
                    ExitCode: 0,
                    Response: WorkerProtocol.CreateError(
                        arguments.LaunchId,
                        requestId,
                        "PLUGIN_UI_UNSUPPORTED",
                        "The plugin does not provide a ToolBox UI surface."));
            }

            var snapshot = getSnapshot(provider)
                ?? throw new PluginLoadException(
                    "PLUGIN_UI_SNAPSHOT_MISSING",
                    "The plugin returned an empty UI snapshot.");
            return new WorkerRequestResult(
                loadedPlugin,
                ShouldShutdown: false,
                ExitCode: 0,
                Response: WorkerProtocol.CreateResponse(
                    arguments.LaunchId,
                    requestId,
                    "ui.snapshot",
                    WorkerProtocol.SerializePayload(snapshot)));
        }
        catch (Exception exception)
        {
            return new WorkerRequestResult(
                loadedPlugin,
                ShouldShutdown: false,
                ExitCode: 0,
                Response: WorkerProtocol.CreateError(
                    arguments.LaunchId,
                    requestId,
                    GetErrorCode(exception, "PLUGIN_UI_FAILED"),
                    exception.Message));
        }
    }

    private static async Task<WorkerRequestResult> ExecuteUiRequestAsync(
        WorkerMessage message,
        WorkerArguments arguments,
        string requestId,
        LoadedInProcessPlugin? loadedPlugin,
        Func<IPluginUiProvider, UiActionRequest, CancellationToken, ValueTask<PluginUiSnapshot>> execute,
        CancellationToken cancellationToken)
    {
        if (loadedPlugin is null)
        {
            return await ErrorResultAsync(
                    arguments,
                    requestId,
                    "PLUGIN_NOT_RUNNING",
                    "The plugin must be running before its controls can be used.")
                .ConfigureAwait(false);
        }

        try
        {
            var provider = loadedPlugin.GetCapability<IPluginUiProvider>();
            if (provider is null)
            {
                return await ErrorResultAsync(
                        arguments,
                        requestId,
                        "PLUGIN_UI_UNSUPPORTED",
                        "The plugin does not provide a ToolBox UI surface.",
                        loadedPlugin: loadedPlugin)
                    .ConfigureAwait(false);
            }

            var request = WorkerProtocol.DeserializePayload<UiActionRequest>(message.Payload);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActionId);
            var snapshot = await execute(provider, request, cancellationToken).ConfigureAwait(false);
            return new WorkerRequestResult(
                loadedPlugin,
                ShouldShutdown: false,
                ExitCode: 0,
                Response: WorkerProtocol.CreateResponse(
                    arguments.LaunchId,
                    requestId,
                    "ui.action",
                    WorkerProtocol.SerializePayload(snapshot)));
        }
        catch (Exception exception)
        {
            return await ErrorResultAsync(
                    arguments,
                    requestId,
                    GetRequestErrorCode(exception, "PLUGIN_UI_ACTION_FAILED", cancellationToken),
                    exception.Message,
                    loadedPlugin: loadedPlugin)
                .ConfigureAwait(false);
        }
    }

    private static async Task<WorkerRequestResult> ExecuteInputRequestAsync(
        WorkerMessage message,
        WorkerArguments arguments,
        string requestId,
        LoadedInProcessPlugin? loadedPlugin,
        CancellationToken cancellationToken)
    {
        if (loadedPlugin is null)
        {
            return await ErrorResultAsync(
                    arguments,
                    requestId,
                    "PLUGIN_NOT_RUNNING",
                    "The plugin must be running before its controls can be used.")
                .ConfigureAwait(false);
        }

        try
        {
            var provider = loadedPlugin.GetCapability<IPluginUiProvider>();
            if (provider is null)
            {
                return await ErrorResultAsync(
                        arguments,
                        requestId,
                        "PLUGIN_UI_UNSUPPORTED",
                        "The plugin does not provide a ToolBox UI surface.",
                        loadedPlugin: loadedPlugin)
                    .ConfigureAwait(false);
            }

            var input = WorkerProtocol.DeserializePayload<PluginInputEvent>(message.Payload);
            var snapshot = await provider.HandleInputAsync(input, cancellationToken).ConfigureAwait(false);
            return new WorkerRequestResult(
                loadedPlugin,
                ShouldShutdown: false,
                ExitCode: 0,
                Response: WorkerProtocol.CreateResponse(
                    arguments.LaunchId,
                    requestId,
                    "ui.input",
                    WorkerProtocol.SerializePayload(snapshot)));
        }
        catch (Exception exception)
        {
            return await ErrorResultAsync(
                    arguments,
                    requestId,
                    GetRequestErrorCode(exception, "PLUGIN_UI_INPUT_FAILED", cancellationToken),
                    exception.Message,
                    loadedPlugin: loadedPlugin)
                .ConfigureAwait(false);
        }
    }

    private static async Task TryWriteErrorAsync(
        StreamWriter writer,
        string launchId,
        string? requestId,
        WorkerProtocolException exception)
    {
        try
        {
            await WriteErrorAsync(writer, launchId, requestId, exception.ErrorCode, exception.Message)
                .ConfigureAwait(false);
        }
        catch
        {
            // The channel may already be closed; the process exit is the fallback signal.
        }
    }

    private static async Task TryWriteErrorAsync(
        WorkerMessageWriter writer,
        string launchId,
        string? requestId,
        WorkerProtocolException exception)
    {
        try
        {
            await WriteErrorAsync(writer, launchId, requestId, exception.ErrorCode, exception.Message)
                .ConfigureAwait(false);
        }
        catch
        {
            // The channel may already be closed; the process exit is the fallback signal.
        }
    }

    private static Task WriteErrorAsync(
        StreamWriter writer,
        string launchId,
        string? requestId,
        string errorCode,
        string errorMessage)
    {
        return WorkerProtocol.WriteAsync(
                writer,
                WorkerProtocol.CreateError(launchId, requestId, errorCode, errorMessage))
            .AsTask();
    }

    private static Task WriteErrorAsync(
        WorkerMessageWriter writer,
        string launchId,
        string? requestId,
        string errorCode,
        string errorMessage)
    {
        return writer.EnqueueAsync(
                WorkerProtocol.CreateError(launchId, requestId, errorCode, errorMessage))
            .AsTask();
    }

    private static void ValidateEnvelope(WorkerMessage message, string expectedLaunchId)
    {
        if (message.ProtocolMajor != WorkerProtocol.ProtocolMajor)
        {
            throw new WorkerProtocolException(
                "WORKER_PROTOCOL_MISMATCH",
                $"Worker protocol major '{message.ProtocolMajor}' is not supported.");
        }

        if (!string.Equals(message.LaunchId, expectedLaunchId, StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                "WORKER_LAUNCH_ID_MISMATCH",
                "The control message launch id does not match this Worker.");
        }
    }

    private static string GetErrorCode(Exception exception, string fallback)
    {
        return exception switch
        {
            PluginLoadException loadException => loadException.ErrorCode,
            WorkerProtocolException protocolException => protocolException.ErrorCode,
            OperationCanceledException => "PLUGIN_SHUTDOWN_TIMEOUT",
            _ => fallback
        };
    }

    private static string GetRequestErrorCode(
        Exception exception,
        string fallback,
        CancellationToken cancellationToken)
    {
        return exception is OperationCanceledException && cancellationToken.IsCancellationRequested
            ? "WORKER_REQUEST_CANCELLED"
            : GetErrorCode(exception, fallback);
    }

    private static PluginShutdownOptions ParseShutdownOptions(string? payload)
    {
        if (!double.TryParse(
                payload,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var milliseconds)
            || !double.IsFinite(milliseconds)
            || milliseconds <= 0)
        {
            return PluginShutdownOptions.Default;
        }

        return new PluginShutdownOptions(
            TimeSpan.FromMilliseconds(Math.Min(milliseconds, int.MaxValue)));
    }

    private sealed record WorkerRequestResult(
        LoadedInProcessPlugin? LoadedPlugin,
        bool ShouldShutdown,
        int ExitCode,
        WorkerMessage Response,
        WorkerMessage? Event = null);

    private sealed record ActiveWorkerRequest(
        string RequestId,
        CancellationTokenSource Cancellation,
        Task<WorkerRequestResult> Task);

    private sealed record WorkerRequestCompletion(
        LoadedInProcessPlugin? LoadedPlugin,
        bool ShouldShutdown,
        int ExitCode);

    private sealed record UiActionRequest(string ActionId, string? Argument);
}
