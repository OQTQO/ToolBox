using System.IO.Compression;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Packaging;

/// <summary>
/// Reads the manifest needed by the Host to route an incoming package. Full
/// archive safety, hashes, and transaction checks remain in the installer.
/// </summary>
public sealed class PluginPackageInspector
{
    private readonly PluginManifestParser _manifestParser;

    public PluginPackageInspector(PluginManifestParser? manifestParser = null)
    {
        _manifestParser = manifestParser ?? new PluginManifestParser();
    }

    public PluginManifest ReadManifest(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        if (!File.Exists(packagePath))
        {
            throw new PluginPackageException(
                "PACKAGE_NOT_FOUND",
                $"The plugin package '{packagePath}' does not exist.");
        }

        try
        {
            using var archive = ZipFile.OpenRead(Path.GetFullPath(packagePath));
            var manifestEntries = archive.Entries
                .Where(entry => string.Equals(
                    entry.FullName.Replace('\\', '/'),
                    "manifest.json",
                    StringComparison.Ordinal))
                .ToArray();

            if (manifestEntries.Length != 1 || manifestEntries[0].Length > 1024 * 1024)
            {
                throw new PluginPackageException(
                    "BAD_MANIFEST_PACKAGE",
                    "The selected package must contain exactly one root manifest.json no larger than 1 MiB.");
            }

            using var stream = manifestEntries[0].Open();
            using var reader = new StreamReader(stream);
            var manifestJson = reader.ReadToEnd();
            return _manifestParser.Parse(manifestJson);
        }
        catch (PluginPackageException)
        {
            throw;
        }
        catch (PluginManifestValidationException exception)
        {
            var errorCode = exception.Errors.Any(error =>
                    error.Code == "PLUGIN_API_MAJOR_UNSUPPORTED")
                ? "INCOMPATIBLE_API_PLUGIN"
                : "BAD_MANIFEST_PACKAGE";
            throw new PluginPackageException(
                errorCode,
                "The selected package manifest failed PluginSdk validation.",
                exception);
        }
        catch (Exception exception) when (exception is InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The selected package manifest could not be read.",
                exception);
        }
    }
}
