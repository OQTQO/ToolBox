using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;
using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Plugins;

[SupportedOSPlatform("windows")]
public sealed class OutOfProcessPluginSession : IAsyncDisposable
{
    private readonly WorkerProcessHandle _worker;
    private readonly NamedPipeServerStream _pipe;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private PluginState _state;
    private bool _channelClosed;
    private bool _workerDisposed;
    private bool _disposed;

    internal OutOfProcessPluginSession(
        DiscoveredPlugin discoveredPlugin,
        string launchId,
        WorkerProcessHandle worker,
        NamedPipeServerStream pipe,
        StreamReader reader,
        StreamWriter writer)
    {
        Discovered = discoveredPlugin ?? throw new ArgumentNullException(nameof(discoveredPlugin));
        LaunchId = string.IsNullOrWhiteSpace(launchId)
            ? throw new ArgumentException("A Worker launch id is required.", nameof(launchId))
            : launchId;
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
        _pipe = pipe ?? throw new ArgumentNullException(nameof(pipe));
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _state = PluginState.CreateInstalled(Manifest).TransitionTo(PluginLifecycleState.Disabled);
    }

    public DiscoveredPlugin Discovered { get; }

    public PluginManifest Manifest => Discovered.Manifest;

    public string LaunchId { get; }

    public int WorkerProcessId => _worker.ProcessId;

    public PluginState State => _state;

    public bool WorkerHasExited => _workerDisposed || _worker.HasExited;

    public async Task StartPluginAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state.LifecycleState != PluginLifecycleState.Disabled)
        {
            throw new PluginLoadException(
                "PLUGIN_START_STATE_INVALID",
                $"The OutOfProcess plugin cannot be started from state '{_state.LifecycleState}'.");
        }

        _state = _state.TransitionTo(PluginLifecycleState.Starting);

        try
        {
            await RequestAsync("start", cancellationToken: cancellationToken).ConfigureAwait(false);
            _state = _state.TransitionTo(PluginLifecycleState.Running);
        }
        catch (Exception exception)
        {
            _state = _state.TransitionTo(
                PluginLifecycleState.Faulted,
                errorCode: GetErrorCode(exception, "WORKER_START_FAILED"),
                errorMessage: exception.Message);
            throw;
        }
    }

    public async Task<PluginUiSnapshot?> GetUiSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var response = await RequestAsync("ui.snapshot", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return DeserializeUiSnapshot(response.Payload);
        }
        catch (WorkerProtocolException exception)
            when (exception.ErrorCode == "PLUGIN_UI_UNSUPPORTED")
        {
            return null;
        }
    }

    public async Task<PluginUiSnapshot> ExecuteUiActionAsync(
        string actionId,
        string? argument = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var payload = WorkerProtocol.SerializePayload(new
        {
            actionId,
            argument
        });
        var response = await RequestAsync("ui.action", payload, cancellationToken)
            .ConfigureAwait(false);
        return DeserializeUiSnapshot(response.Payload)
            ?? throw new WorkerProtocolException(
                "PLUGIN_UI_SNAPSHOT_MISSING",
                "The plugin action completed without returning a UI snapshot.");
    }

    public async Task<PluginUiSnapshot> SendUiInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);

        var response = await RequestAsync(
                "ui.input",
                WorkerProtocol.SerializePayload(input),
                cancellationToken)
            .ConfigureAwait(false);
        return DeserializeUiSnapshot(response.Payload)
            ?? throw new WorkerProtocolException(
                "PLUGIN_UI_SNAPSHOT_MISSING",
                "The plugin input was accepted without returning a UI snapshot.");
    }

    public async Task SendHeartbeatAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureChannelOpen();

        var requestId = Guid.NewGuid().ToString("N");
        await WriteAsync(
                new WorkerMessage(
                    WorkerMessageType.Heartbeat,
                    WorkerProtocol.ProtocolMajor,
                    LaunchId,
                    RequestId: requestId,
                    Payload: "ping"),
                cancellationToken)
            .ConfigureAwait(false);

        while (true)
        {
            var message = await WorkerProtocol.ReadAsync(_reader, cancellationToken).ConfigureAwait(false);
            ValidateEnvelope(message);

            if (message.Type == WorkerMessageType.Error)
            {
                throw CreateProtocolError(message);
            }

            if (message.Type == WorkerMessageType.Heartbeat
                && string.Equals(message.RequestId, requestId, StringComparison.Ordinal))
            {
                return;
            }
        }
    }

    public async Task CancelAsync(
        string requestId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        EnsureChannelOpen();

        await WriteAsync(
                new WorkerMessage(
                    WorkerMessageType.Cancel,
                    WorkerProtocol.ProtocolMajor,
                    LaunchId,
                    RequestId: requestId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return StopAsync(PluginShutdownOptions.Default, cancellationToken);
    }

    public async Task StopAsync(
        PluginShutdownOptions options,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);

        using var deadline = ShutdownDeadline.Start(options, cancellationToken);
        await StopCoreAsync(deadline).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(ShutdownDeadline deadline)
    {

        if (_channelClosed)
        {
            return;
        }

        var wasRunning = _state.LifecycleState == PluginLifecycleState.Running;

        if (_state.LifecycleState is not PluginLifecycleState.Running
            and not PluginLifecycleState.Disabled)
        {
            throw new PluginLoadException(
                "PLUGIN_STOP_STATE_INVALID",
                $"The OutOfProcess plugin cannot be stopped from state '{_state.LifecycleState}'.");
        }

        if (wasRunning)
        {
            _state = _state.TransitionTo(PluginLifecycleState.Stopping);
        }

        try
        {
            if (wasRunning)
            {
                await RequestAsync(
                        "stop",
                        FormatRemainingMilliseconds(deadline),
                        deadline.Token)
                    .ConfigureAwait(false);
            }

            await RequestAsync(
                    "shutdown",
                    FormatRemainingMilliseconds(deadline),
                    deadline.Token)
                .ConfigureAwait(false);
            await WaitForWorkerExitAsync(deadline).ConfigureAwait(false);
            DisposeWorker(deadline);

            if (wasRunning)
            {
                _state = _state.TransitionTo(PluginLifecycleState.Disabled);
            }

            CloseChannel();
        }
        catch (Exception exception)
        {
            var surfacedException = exception is OperationCanceledException
                && deadline.IsTimedOut
                ? new WorkerProtocolException(
                    "PLUGIN_SHUTDOWN_TIMEOUT",
                    "The OutOfProcess plugin did not stop before the shutdown deadline.")
                : exception;

            if (wasRunning && _state.LifecycleState == PluginLifecycleState.Stopping)
            {
                _state = _state.TransitionTo(
                    PluginLifecycleState.DisableFailed,
                    errorCode: GetFailureCode(surfacedException, deadline),
                    errorMessage: surfacedException.Message);
                _state = _state.TransitionTo(
                    PluginLifecycleState.RestartRequired,
                    errorCode: GetFailureCode(surfacedException, deadline),
                    errorMessage: surfacedException.Message);
            }

            TerminateWorker(deadline);
            CloseChannel();
            throw surfacedException;
        }
    }

    public void TerminateForTest()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_state.LifecycleState == PluginLifecycleState.Running)
        {
            _state = _state.TransitionTo(
                PluginLifecycleState.Faulted,
                errorCode: "WORKER_TERMINATED",
                errorMessage: "The Worker was terminated by the test harness.");
        }
        else if (_state.LifecycleState == PluginLifecycleState.Starting)
        {
            _state = _state.TransitionTo(
                PluginLifecycleState.Faulted,
                errorCode: "WORKER_TERMINATED",
                errorMessage: "The Worker was terminated by the test harness.");
        }

        TerminateWorker();
        CloseChannel();
    }

    public void RequireRestart(
        string reason,
        string errorCode = "PLUGIN_RESTART_REQUIRED")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        if (_state.LifecycleState == PluginLifecycleState.RestartRequired)
        {
            return;
        }

        _state = _state.TransitionTo(
            PluginLifecycleState.RestartRequired,
            errorCode: errorCode,
            errorMessage: reason);
    }

    public void Quarantine(
        string reason,
        string errorCode = "PLUGIN_QUARANTINED")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        _state = _state.TransitionTo(
            PluginLifecycleState.Quarantined,
            errorCode: errorCode,
            errorMessage: reason);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        using var deadline = ShutdownDeadline.Start(PluginShutdownOptions.Default);

        try
        {
            if (!_channelClosed)
            {
                try
                {
                    await StopCoreAsync(deadline).ConfigureAwait(false);
                }
                catch
                {
                    TerminateWorker(deadline);
                    CloseChannel();
                }
            }
        }
        finally
        {
            CloseChannel();
            DisposeWorker(deadline);
            _writeGate.Dispose();
            _requestGate.Dispose();
            _disposed = true;
        }
    }

    private async Task<WorkerMessage> RequestAsync(
        string operation,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        EnsureChannelOpen();

        await _requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            await WriteAsync(
                    WorkerProtocol.CreateRequest(LaunchId, requestId, operation, payload),
                    cancellationToken)
                .ConfigureAwait(false);

            while (true)
            {
                var message = await WorkerProtocol.ReadAsync(_reader, cancellationToken).ConfigureAwait(false);
                ValidateEnvelope(message);

                if (message.Type == WorkerMessageType.Error)
                {
                    throw CreateProtocolError(message);
                }

                if (message.Type == WorkerMessageType.Response
                    && string.Equals(message.RequestId, requestId, StringComparison.Ordinal))
                {
                    return message;
                }
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task WriteAsync(WorkerMessage message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WorkerProtocol.WriteAsync(_writer, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task WaitForWorkerExitAsync(ShutdownDeadline deadline)
    {
        while (!_worker.HasExited)
        {
            deadline.ThrowIfExpired();
            await Task.Delay(25, deadline.Token).ConfigureAwait(false);
        }
    }

    private void ValidateEnvelope(WorkerMessage message)
    {
        if (message.ProtocolMajor != WorkerProtocol.ProtocolMajor)
        {
            throw new WorkerProtocolException(
                "WORKER_PROTOCOL_MISMATCH",
                $"Worker protocol major '{message.ProtocolMajor}' is not supported.");
        }

        if (!string.Equals(message.LaunchId, LaunchId, StringComparison.Ordinal))
        {
            throw new WorkerProtocolException(
                "WORKER_LAUNCH_ID_MISMATCH",
                "The Worker returned a message for a different launch.");
        }
    }

    private static WorkerProtocolException CreateProtocolError(WorkerMessage message)
    {
        return new WorkerProtocolException(
            message.ErrorCode ?? "WORKER_REQUEST_FAILED",
            message.ErrorMessage ?? "The PluginWorker rejected the request.");
    }

    private static PluginUiSnapshot? DeserializeUiSnapshot(string? payload)
    {
        return string.IsNullOrWhiteSpace(payload)
            ? null
            : WorkerProtocol.DeserializePayload<PluginUiSnapshot>(payload);
    }

    private static string GetErrorCode(Exception exception, string fallback)
    {
        return exception switch
        {
            WorkerProtocolException protocolException => protocolException.ErrorCode,
            PluginLoadException loadException => loadException.ErrorCode,
            _ => fallback
        };
    }

    private void EnsureChannelOpen()
    {
        if (_channelClosed || !_pipe.IsConnected)
        {
            throw new WorkerProtocolException(
                "WORKER_PIPE_CLOSED",
                "The PluginWorker control channel is closed.");
        }
    }

    private void TerminateWorker(ShutdownDeadline? deadline = null)
    {
        try
        {
            _worker.Terminate();

            if (deadline is not null && deadline.Remaining > TimeSpan.Zero)
            {
                _worker.WaitForExit(deadline);
            }
        }
        catch (InvalidOperationException)
        {
            // The process may have exited between the state check and termination.
        }
        catch (OperationCanceledException)
        {
            // The deadline owns the remaining shutdown budget. The Job Object
            // still guarantees process-tree termination when its handle closes.
        }
    }

    private static string GetFailureCode(Exception exception, ShutdownDeadline deadline)
    {
        if (deadline.IsTimedOut)
        {
            return "PLUGIN_SHUTDOWN_TIMEOUT";
        }

        return exception switch
        {
            WorkerProtocolException protocolException => protocolException.ErrorCode,
            PluginLoadException loadException => loadException.ErrorCode,
            _ => "WORKER_STOP_FAILED"
        };
    }

    private static string FormatRemainingMilliseconds(ShutdownDeadline deadline)
    {
        var milliseconds = Math.Max(1, Math.Ceiling(deadline.Remaining.TotalMilliseconds));
        return milliseconds.ToString(CultureInfo.InvariantCulture);
    }

    private void DisposeWorker(ShutdownDeadline? deadline = null)
    {
        if (_workerDisposed)
        {
            return;
        }

        _worker.Dispose(deadline);
        _workerDisposed = true;
    }

    private void CloseChannel()
    {
        if (_channelClosed)
        {
            return;
        }

        _channelClosed = true;

        try
        {
            _writer.Dispose();
        }
        finally
        {
            try
            {
                _reader.Dispose();
            }
            finally
            {
                _pipe.Dispose();
            }
        }
    }
}
