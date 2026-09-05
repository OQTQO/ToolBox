using System.IO;

namespace ToolBox.Host;

internal sealed record HostLaunchOptions(
    string? UiAcceptanceRoot,
    string? UiAcceptancePackage)
{
    private const string RootOption = "--ui-acceptance-root";
    private const string PackageOption = "--ui-acceptance-package";

    public static HostLaunchOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? acceptanceRoot = null;
        string? acceptancePackage = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (!string.Equals(option, RootOption, StringComparison.Ordinal)
                && !string.Equals(option, PackageOption, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown Host option '{option}'.");
            }

            if (index + 1 >= arguments.Count)
            {
                throw new ArgumentException($"Host option '{option}' is missing its value.");
            }

            var value = arguments[++index];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Host option '{option}' cannot have an empty value.");
            }

            var fullPath = Path.GetFullPath(value);
            if (string.Equals(option, RootOption, StringComparison.Ordinal))
            {
                acceptanceRoot = fullPath;
            }
            else
            {
                acceptancePackage = fullPath;
            }
        }

        if (acceptancePackage is not null && acceptanceRoot is null)
        {
            throw new ArgumentException(
                $"Host option '{PackageOption}' requires '{RootOption}'.");
        }

        if (acceptancePackage is not null && !File.Exists(acceptancePackage))
        {
            throw new FileNotFoundException(
                "The UI acceptance plugin package was not found.",
                acceptancePackage);
        }

        return new HostLaunchOptions(acceptanceRoot, acceptancePackage);
    }
}

internal sealed record HostStoragePaths(
    string DataRoot,
    string PluginsRoot,
    string PluginDataRoot,
    string LogsRoot,
    string SettingsPath,
    bool IsAcceptance)
{
    public static HostStoragePaths Create(string? acceptanceRoot)
    {
        if (string.IsNullOrWhiteSpace(acceptanceRoot))
        {
            var dataRoot = Path.Combine(AppContext.BaseDirectory, "Data");
            return new HostStoragePaths(
                dataRoot,
                Path.Combine(dataRoot, "Plugins"),
                Path.Combine(dataRoot, "PluginData"),
                Path.Combine(dataRoot, "Logs"),
                Path.Combine(dataRoot, "ui-settings.json"),
                false);
        }

        var root = Path.GetFullPath(acceptanceRoot);
        if (File.Exists(root))
        {
            throw new IOException($"The UI acceptance root is a file: '{root}'.");
        }

        return new HostStoragePaths(
            root,
            Path.Combine(root, "Plugins"),
            Path.Combine(root, "PluginData"),
            Path.Combine(root, "Logs"),
            Path.Combine(root, "ui-settings.json"),
            true);
    }
}
