namespace ToolBox.Core.Diagnostics;

public sealed record LogEvent(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string SessionId,
    string OperationId,
    string Module,
    string Message,
    string HostVersion,
    string? PluginId = null,
    string? PluginVersion = null,
    string? ErrorCode = null,
    ExceptionDetails? Exception = null);

public sealed record ExceptionDetails(
    string Type,
    string Message,
    string? StackTrace)
{
    public static ExceptionDetails From(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new ExceptionDetails(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception.StackTrace);
    }
}
