namespace ToolBox.Core.Packaging;

public sealed class PluginPackageException : InvalidOperationException
{
    public PluginPackageException(
        string errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
