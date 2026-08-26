namespace ToolBox.Core.Plugins;

public sealed class PluginLoadException : InvalidOperationException
{
    public PluginLoadException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
