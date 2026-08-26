using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBox.PluginSdk;

public sealed class PluginManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly PluginManifestParserOptions _options;

    public PluginManifestParser(PluginManifestParserOptions? options = null)
    {
        _options = options ?? new PluginManifestParserOptions();

        if (_options.SupportedPluginApiMajor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SupportedPluginApiMajor must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SupportedOs);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.SupportedArchitecture);
    }

    public PluginManifest Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw CreateValidationException(
                "MANIFEST_EMPTY",
                "$",
                "Manifest JSON must not be empty.");
        }

        PluginManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw CreateValidationException(
                "MANIFEST_JSON_INVALID",
                "$",
                "Manifest JSON is malformed.",
                exception);
        }

        if (manifest is null)
        {
            throw CreateValidationException(
                "MANIFEST_EMPTY",
                "$",
                "Manifest JSON did not contain an object.");
        }

        var errors = Validate(manifest);

        if (errors.Count > 0)
        {
            throw new PluginManifestValidationException(errors);
        }

        return manifest;
    }

    private List<PluginManifestValidationError> Validate(PluginManifest manifest)
    {
        var errors = new List<PluginManifestValidationError>();

        if (manifest.FormatVersion != PluginContract.ManifestFormatVersion)
        {
            errors.Add(new PluginManifestValidationError(
                "MANIFEST_FORMAT_UNSUPPORTED",
                "formatVersion",
                $"Expected manifest format {PluginContract.ManifestFormatVersion}."));
        }

        RequireText(errors, "id", manifest.Id, "Plugin id is required.");
        RequireText(errors, "name", manifest.Name, "Plugin name is required.");
        RequireText(errors, "version", manifest.Version, "Plugin version is required.");
        RequireText(errors, "publisher", manifest.Publisher, "Plugin publisher is required.");
        RequireText(errors, "entryPoint", manifest.EntryPoint, "Plugin entry point is required.");

        if (!string.IsNullOrWhiteSpace(manifest.Id)
            && manifest.Id.Any(character => char.IsWhiteSpace(character) || character is '/' or '\\' or ':'))
        {
            errors.Add(new PluginManifestValidationError(
                "MANIFEST_ID_INVALID",
                "id",
                "Plugin id cannot contain whitespace, path separators, or a drive separator."));
        }

        if (!string.IsNullOrWhiteSpace(manifest.Name) && manifest.Name.Length > 128)
        {
            errors.Add(new PluginManifestValidationError(
                "MANIFEST_NAME_TOO_LONG",
                "name",
                "Plugin name must be 128 characters or fewer."));
        }

        if (manifest.PluginApiMajor != _options.SupportedPluginApiMajor)
        {
            errors.Add(new PluginManifestValidationError(
                "PLUGIN_API_MAJOR_UNSUPPORTED",
                "pluginApiMajor",
                $"Plugin API major {manifest.PluginApiMajor} is not supported; expected {_options.SupportedPluginApiMajor}."));
        }

        if (manifest.Platform is null)
        {
            errors.Add(new PluginManifestValidationError(
                "MANIFEST_PLATFORM_REQUIRED",
                "platform",
                "Platform information is required."));
        }
        else
        {
            if (!string.Equals(manifest.Platform.Os, _options.SupportedOs, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new PluginManifestValidationError(
                    "MANIFEST_OS_UNSUPPORTED",
                    "platform.os",
                    $"Operating system '{manifest.Platform.Os}' is not supported."));
            }

            if (!string.Equals(manifest.Platform.Arch, _options.SupportedArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new PluginManifestValidationError(
                    "MANIFEST_ARCH_UNSUPPORTED",
                    "platform.arch",
                    $"Architecture '{manifest.Platform.Arch}' is not supported."));
            }
        }

        if (manifest.Runtime is null)
        {
            errors.Add(new PluginManifestValidationError(
                "MANIFEST_RUNTIME_REQUIRED",
                "runtime",
                "Runtime information is required."));
        }
        else if (manifest.Runtime.SupportedModes is null || manifest.Runtime.SupportedModes.Length == 0)
        {
            errors.Add(new PluginManifestValidationError(
                "MANIFEST_RUNTIME_MODES_REQUIRED",
                "runtime.supportedModes",
                "At least one supported execution mode is required."));
        }
        else
        {
            if (manifest.Runtime.SupportedModes.Distinct().Count() != manifest.Runtime.SupportedModes.Length)
            {
                errors.Add(new PluginManifestValidationError(
                    "MANIFEST_RUNTIME_MODES_DUPLICATE",
                    "runtime.supportedModes",
                    "Supported execution modes must be unique."));
            }

            if (!manifest.Runtime.SupportedModes.Contains(manifest.Runtime.PreferredMode))
            {
                errors.Add(new PluginManifestValidationError(
                    "MANIFEST_PREFERRED_MODE_UNSUPPORTED",
                    "runtime.preferredMode",
                    "Preferred execution mode must be listed in supportedModes."));
            }
        }

        return errors;
    }

    private static void RequireText(
        List<PluginManifestValidationError> errors,
        string field,
        string value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new PluginManifestValidationError("MANIFEST_FIELD_REQUIRED", field, message));
        }
    }

    private static PluginManifestValidationException CreateValidationException(
        string code,
        string field,
        string message,
        Exception? innerException = null)
    {
        return new PluginManifestValidationException(
            new[] { new PluginManifestValidationError(code, field, message) },
            innerException);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
