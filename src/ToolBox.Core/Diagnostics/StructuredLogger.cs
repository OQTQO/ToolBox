using DiagnosticsDebug = System.Diagnostics.Debug;
using System.Threading.Channels;

namespace ToolBox.Core.Diagnostics;

public sealed class StructuredLogger : IStructuredLogger
{
    private readonly Channel<LogEvent> _events = Channel.CreateUnbounded<LogEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });

    private readonly RollingJsonLogWriter _fileWriter;
    private readonly Task _writerTask;
    private readonly LogLevel _minimumLevel;
    private int _disposeSignaled;

    public StructuredLogger(LoggerOptions options, string sessionId, string hostVersion)
    {
        ArgumentNullException.ThrowIfNull(options);
        SessionId = string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("Session id is required.", nameof(sessionId))
            : sessionId;
        HostVersion = string.IsNullOrWhiteSpace(hostVersion)
            ? throw new ArgumentException("Host version is required.", nameof(hostVersion))
            : hostVersion;

        _minimumLevel = options.MinimumLevel;
        _fileWriter = new RollingJsonLogWriter(options);
        _writerTask = Task.Run(DrainAsync);
    }

    public string SessionId { get; }

    public string HostVersion { get; }

    public event Action<LogEvent>? EventWritten;

    public void Log(
        LogLevel level,
        string component,
        string message,
        string? operationId = null,
        string? errorCode = null,
        Exception? exception = null,
        string? pluginId = null,
        string? pluginVersion = null)
    {
        if (Volatile.Read(ref _disposeSignaled) != 0 || level < _minimumLevel)
        {
            return;
        }

        var entry = new LogEvent(
            DateTimeOffset.UtcNow,
            level,
            SessionId,
            string.IsNullOrWhiteSpace(operationId) ? Guid.NewGuid().ToString("N") : operationId,
            string.IsNullOrWhiteSpace(component) ? "Unknown" : component,
            message ?? string.Empty,
            HostVersion,
            pluginId,
            pluginVersion,
            errorCode,
            exception is null ? null : ExceptionDetails.From(exception));

        try
        {
            EventWritten?.Invoke(entry);
        }
        catch (Exception callbackException)
        {
            DiagnosticsDebug.WriteLine($"ToolBox logger event subscriber failed: {callbackException}");
        }

        if (!_events.Writer.TryWrite(entry))
        {
            DiagnosticsDebug.WriteLine("ToolBox logger queue rejected an event.");
        }
    }

    public void Trace(string component, string message, string? operationId = null)
    {
        Log(LogLevel.Trace, component, message, operationId);
    }

    public void Debug(string component, string message, string? operationId = null)
    {
        Log(LogLevel.Debug, component, message, operationId);
    }

    public void Info(string component, string message, string? operationId = null)
    {
        Log(LogLevel.Information, component, message, operationId);
    }

    public void Warning(string component, string message, string? operationId = null, string? errorCode = null)
    {
        Log(LogLevel.Warning, component, message, operationId, errorCode);
    }

    public void Error(
        string component,
        string message,
        string? operationId = null,
        string? errorCode = null,
        Exception? exception = null)
    {
        Log(LogLevel.Error, component, message, operationId, errorCode, exception);
    }

    public void Critical(
        string component,
        string message,
        string? operationId = null,
        string? errorCode = null,
        Exception? exception = null)
    {
        Log(LogLevel.Critical, component, message, operationId, errorCode, exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0)
        {
            return;
        }

        _events.Writer.TryComplete();

        try
        {
            await _writerTask.ConfigureAwait(false);
        }
        finally
        {
            _fileWriter.Dispose();
        }
    }

    private async Task DrainAsync()
    {
        await foreach (var entry in _events.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                _fileWriter.Write(entry);
            }
            catch (Exception writerException)
            {
                DiagnosticsDebug.WriteLine($"ToolBox structured log write failed: {writerException}");
            }
        }
    }
}
