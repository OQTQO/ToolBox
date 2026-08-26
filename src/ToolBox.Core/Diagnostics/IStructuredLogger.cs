using System.Diagnostics.CodeAnalysis;

namespace ToolBox.Core.Diagnostics;

public interface IStructuredLogger : IAsyncDisposable
{
    string SessionId { get; }

    event Action<LogEvent>? EventWritten;

    void Log(
        LogLevel level,
        string component,
        string message,
        string? operationId = null,
        string? errorCode = null,
        Exception? exception = null,
        string? pluginId = null,
        string? pluginVersion = null);

    void Trace(string component, string message, string? operationId = null);

    void Debug(string component, string message, string? operationId = null);

    void Info(string component, string message, string? operationId = null);

    void Warning(string component, string message, string? operationId = null, string? errorCode = null);

    [SuppressMessage("Naming", "CA1716:IdentifiersShouldNotMatchKeywords", Justification = "Error is an intentional conventional logger method name.")]
    void Error(
        string component,
        string message,
        string? operationId = null,
        string? errorCode = null,
        Exception? exception = null);

    void Critical(
        string component,
        string message,
        string? operationId = null,
        string? errorCode = null,
        Exception? exception = null);
}
