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
    public static async Task<int> RunAsync(WorkerArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        using var pipe = new NamedPipeClientStream(
            ".",
            arguments.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipe.ConnectAsync(5_000)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            Console.Error.WriteLine($"Worker control channel connection failed: {exception.Message}");
            return 3;
        }

        using var reader = new StreamReader(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: true);
        using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true)
        {
            AutoFlush = false
        };

        WorkerMessage hello;

        try
        {
            hello = await WorkerProtocol.ReadAsync(reader).ConfigureAwait(false);
            WorkerHandshake.ValidateHello(hello, arguments.LaunchId);
        }
        catch (WorkerProtocolException exception)
        {
            await TryWriteErrorAsync(writer, arguments.LaunchId, null, exception).ConfigureAwait(false);
            return 10;
        }

        await WorkerProtocol.WriteAsync(
                writer,
                WorkerProtocol.CreateHelloAck(arguments.LaunchId))
            .ConfigureAwait(false);

        var pluginRuntime = new InProcessPluginRuntime();
        LoadedInProcessPlugin? loadedPlugin = null;

        try
        {
            var readTask = ReadValidatedMessageAsync(reader, arguments.LaunchId);
            ActiveWorkerRequest? activeRequest = null;

            while (true)
            {
                if (activeRequest is not null && activeRequest.Task.IsCompleted)
                {
                    var completion = await CompleteRequestAsync(
                            writer,
                            activeRequest,
                            loadedPlugin)
                        .ConfigureAwait(false);
                    loadedPlugin = completion.LoadedPlugin;
                    activeRequest = null;

                    if (completion.ShouldShutdown)
                    {
                        return completion.ExitCode;
                    }

                    continue;
                }

                if (activeRequest is not null)
                {
                    var completedTask = await Task.WhenAny(readTask, activeRequest.Task)
                        .ConfigureAwait(false);

                    if (completedTask == activeRequest.Task)
                    {
                        continue;
                    }
                }

                WorkerMessage message;

                try
                {
                    message = await readTask.ConfigureAwait(false);
                    readTask = ReadValidatedMessageAsync(reader, arguments.LaunchId);
                }
                catch (WorkerProtocolException exception)
                {
                    activeRequest?.Cancellation.Cancel();
                    await TryWriteErrorAsync(writer, arguments.LaunchId, null, exception).ConfigureAwait(false);
                    return 11;
                }

                switch (message.Type)
                {
                    case WorkerMessageType.Request:
                        if (activeRequest is not null)
                        {
                            await WriteErrorAsync(
                                    writer,
                                    arguments.LaunchId,
                                    message.RequestId,
                                    "WORKER_REQUEST_BUSY",
                                    "The Worker is already executing another plugin request.")
                                .ConfigureAwait(false);
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(message.RequestId))
                        {
                            await WriteErrorAsync(
                                    writer,
                                    arguments.LaunchId,
                                    message.RequestId,
                                    "WORKER_REQUEST_ID_REQUIRED",
                                    "Worker requests require a non-empty requestId.")
                                .ConfigureAwait(false);
                            break;
                        }

                        activeRequest = StartRequest(
                            message,
                            arguments,
                            pluginRuntime,
                            loadedPlugin);
                        break;

                    case WorkerMessageType.Heartbeat:
                        await WorkerProtocol.WriteAsync(
                                writer,
                                WorkerProtocol.CreateHeartbeat(arguments.LaunchId, message.RequestId))
                            .ConfigureAwait(false);
                        break;

                    case WorkerMessageType.Cancel:
                        if (activeRequest is not null
                            && string.Equals(
                                activeRequest.RequestId,
                                message.RequestId,
                                StringComparison.Ordinal))
                        {
                            activeRequest.Cancellation.Cancel();
                        }
                        else
                        {
                            await WriteErrorAsync(
                                    writer,
                                    arguments.LaunchId,
                                    message.RequestId,
                                    "WORKER_REQUEST_NOT_FOUND",
                                    "No active Worker request matches the supplied requestId.")
                                .ConfigureAwait(false);
                        }

                        break;

                    default:
                        await WriteErrorAsync(
                                writer,
                                arguments.LaunchId,
                                message.RequestId,
                                "WORKER_MESSAGE_UNEXPECTED",
                                $"Worker received unsupported message type '{message.Type}'.")
                            .ConfigureAwait(false);
                        break;
                }
            }
        }
        finally
        {
            if (loadedPlugin is not null)
            {
                try
                {
                    await loadedPlugin.StopAndUnloadAsync().ConfigureAwait(false);
                }
                catch
                {
                    // The host owns the process boundary and will terminate the Job on failure.
                }
            }
        }
    }

    private static async Task<WorkerMessage> ReadValidatedMessageAsync(
        StreamReader reader,
        string launchId)
    {
        var message = await WorkerProtocol.ReadAsync(reader).ConfigureAwait(false);
        ValidateEnvelope(message, launchId);
        return message;
    }

    private static ActiveWorkerRequest StartRequest(
        WorkerMessage message,
        WorkerArguments arguments,
        InProcessPluginRuntime pluginRuntime,
        LoadedInProcessPlugin? loadedPlugin)
    {
        var cancellation = new CancellationTokenSource();
        var task = Task.Run(
            () => HandleRequestAsync(
                message,
                arguments,
                pluginRuntime,
                loadedPlugin,
                cancellation.Token));
        return new ActiveWorkerRequest(message.RequestId!, cancellation, task);
    }

    private static async Task<WorkerRequestCompletion> CompleteRequestAsync(
        StreamWriter writer,
        ActiveWorkerRequest activeRequest,
        LoadedInProcessPlugin? loadedPlugin)
    {
        try
        {
            var result = await activeRequest.Task.ConfigureAwait(false);
            loadedPlugin = result.LoadedPlugin;

            if (result.Event is not null)
            {
                await WorkerProtocol.WriteAsync(writer, result.Event).ConfigureAwait(false);
            }

            await WorkerProtocol.WriteAsync(writer, result.Response).ConfigureAwait(false);
            return new WorkerRequestCompletion(
                loadedPlugin,
                result.ShouldShutdown,
                result.ExitCode);
        }
        finally
        {
            activeRequest.Cancellation.Dispose();
        }
    }
}
