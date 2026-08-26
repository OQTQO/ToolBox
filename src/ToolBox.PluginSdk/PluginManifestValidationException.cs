namespace ToolBox.PluginSdk;

public sealed record PluginManifestValidationError(
    string Code,
    string Field,
    string Message);

public sealed class PluginManifestValidationException : FormatException
{
    public PluginManifestValidationException(
        IReadOnlyList<PluginManifestValidationError> errors,
        Exception? innerException = null)
        : base(BuildMessage(errors), innerException)
    {
        ArgumentNullException.ThrowIfNull(errors);

        if (errors.Count == 0)
        {
            throw new ArgumentException("At least one validation error is required.", nameof(errors));
        }

        Errors = errors;
    }

    public IReadOnlyList<PluginManifestValidationError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<PluginManifestValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return errors.Count == 0
            ? "Plugin manifest validation failed."
            : string.Join("; ", errors.Select(error => $"{error.Field}: {error.Message}"));
    }
}
