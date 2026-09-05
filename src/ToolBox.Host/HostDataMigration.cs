using System.IO;
using System.Text;

namespace ToolBox.Host;

internal sealed record HostDataMigrationResult(
    int CopiedFileCount,
    IReadOnlyList<string> Warnings);

internal static class HostDataMigration
{
    internal const string MarkerFileName = ".legacy-data-migration-v1.complete";

    public static HostDataMigrationResult Migrate(
        HostStoragePaths storage,
        string? legacyInstallRoot = null,
        string? legacyDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(storage);

        if (storage.IsAcceptance)
        {
            return new HostDataMigrationResult(0, Array.Empty<string>());
        }

        var warnings = new List<string>();
        var copiedFileCount = 0;
        try
        {
            Directory.CreateDirectory(storage.DataRoot);
            var markerPath = Path.Combine(storage.DataRoot, MarkerFileName);
            if (File.Exists(markerPath))
            {
                return new HostDataMigrationResult(0, warnings);
            }

            var effectiveLegacyInstallRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(legacyInstallRoot)
                    ? AppContext.BaseDirectory
                    : legacyInstallRoot);
            var effectiveLegacyDataRoot = Path.GetFullPath(
                string.IsNullOrWhiteSpace(legacyDataRoot)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ToolBox")
                    : legacyDataRoot);

            copiedFileCount += CopyDirectoryMissing(
                Path.Combine(effectiveLegacyInstallRoot, "Plugins"),
                storage.PluginsRoot,
                warnings);
            copiedFileCount += CopyDirectoryMissing(
                Path.Combine(effectiveLegacyDataRoot, "Plugins"),
                storage.PluginDataRoot,
                warnings);
            copiedFileCount += CopyDirectoryMissing(
                Path.Combine(effectiveLegacyDataRoot, "Logs"),
                storage.LogsRoot,
                warnings);
            copiedFileCount += CopyFileMissing(
                Path.Combine(effectiveLegacyDataRoot, "ui-settings.json"),
                storage.SettingsPath,
                warnings);

            File.WriteAllText(
                markerPath,
                $"ToolBox legacy data migration completed at {DateTimeOffset.UtcNow:O}{Environment.NewLine}",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Legacy data migration could not complete: {exception.Message}");
        }

        return new HostDataMigrationResult(copiedFileCount, warnings);
    }

    private static int CopyDirectoryMissing(
        string sourceDirectory,
        string destinationDirectory,
        List<string> warnings)
    {
        if (!Directory.Exists(sourceDirectory)
            || string.Equals(
                Path.GetFullPath(sourceDirectory),
                Path.GetFullPath(destinationDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var copiedFileCount = 0;
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var filePath in EnumerateFilesWithoutReparsePoints(sourceDirectory))
            {
                var destinationPath = MapPath(sourceDirectory, destinationDirectory, filePath);
                if (File.Exists(destinationPath))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(filePath, destinationPath, overwrite: false);
                    copiedFileCount++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Could not copy legacy data file '{filePath}': {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not read legacy data directory '{sourceDirectory}': {exception.Message}");
        }

        return copiedFileCount;
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string sourceDirectory)
    {
        var pendingDirectories = new Stack<string>();
        var sourceInfo = new DirectoryInfo(sourceDirectory);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            yield break;
        }

        pendingDirectories.Push(sourceDirectory);
        while (pendingDirectories.Count > 0)
        {
            var currentDirectory = pendingDirectories.Pop();
            foreach (var filePath in Directory.EnumerateFiles(currentDirectory))
            {
                if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) == 0)
                {
                    yield return filePath;
                }
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(currentDirectory))
            {
                var directoryInfo = new DirectoryInfo(directoryPath);
                if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pendingDirectories.Push(directoryPath);
                }
            }
        }
    }

    private static int CopyFileMissing(
        string sourcePath,
        string destinationPath,
        List<string> warnings)
    {
        if (!File.Exists(sourcePath) || File.Exists(destinationPath))
        {
            return 0;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not copy legacy data file '{sourcePath}': {exception.Message}");
            return 0;
        }
    }

    private static string MapPath(string sourceRoot, string destinationRoot, string path)
    {
        var relativePath = Path.GetRelativePath(sourceRoot, path);
        return Path.Combine(destinationRoot, relativePath);
    }
}
