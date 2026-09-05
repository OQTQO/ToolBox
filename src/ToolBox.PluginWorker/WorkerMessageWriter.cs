using System.Collections.Generic;
using ToolBox.Core.Plugins.Worker;

namespace ToolBox.PluginWorker;

internal sealed class WorkerMessageWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    private readonly Queue<WorkerMessage> _messages = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Task _pumpTask;
    private WorkerMessage? _latestUiUpdate;
    private bool _uiUpdateSignalPending;
    private bool _completed;

    public WorkerMessageWriter(StreamWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _pumpTask = RunAsync();
    }

    public ValueTask EnqueueAsync(WorkerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            if (_completed)
            {
                return ValueTask.CompletedTask;
            }

            _messages.Enqueue(message);
            _signal.Release();
        }

        return ValueTask.CompletedTask;
    }

    public bool TryEnqueueLatestUiUpdate(WorkerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_gate)
        {
            if (_completed)
            {
                return false;
            }

            _latestUiUpdate = message;
            if (!_uiUpdateSignalPending)
            {
                _uiUpdateSignalPending = true;
                _signal.Release();
            }
            return true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_completed)
            {
                _completed = true;
                _signal.Release();
            }
        }

        try
        {
            await _pumpTask.ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The Host owns the process boundary and treats a closed pipe as a
            // Worker failure. There is nothing else the writer can report.
        }
        catch (ObjectDisposedException)
        {
            // The pipe may already have been closed during process shutdown.
        }
        finally
        {
            _signal.Dispose();
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await _signal.WaitAsync().ConfigureAwait(false);

            while (true)
            {
                WorkerMessage? message;

                lock (_gate)
                {
                    if (_messages.Count > 0)
                    {
                        message = _messages.Dequeue();
                    }
                    else if (_latestUiUpdate is not null)
                    {
                        message = _latestUiUpdate;
                        _latestUiUpdate = null;
                        _uiUpdateSignalPending = false;
                    }
                    else
                    {
                        if (_completed)
                        {
                            return;
                        }

                        break;
                    }
                }

                try
                {
                    await WorkerProtocol.WriteAsync(_writer, message).ConfigureAwait(false);
                }
                catch (WorkerProtocolException) when (
                    message.Type == WorkerMessageType.Event
                    && string.Equals(message.Operation, "ui.updated", StringComparison.Ordinal))
                {
                    // A malformed or oversized unsolicited UI update is
                    // disposable; never let it terminate the Worker.
                }
            }
        }
    }
}
