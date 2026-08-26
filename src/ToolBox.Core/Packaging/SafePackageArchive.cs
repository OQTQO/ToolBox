using System.IO.Compression;

namespace ToolBox.Core.Packaging;

internal static class SafePackageArchive
{
    public static async Task<IReadOnlyList<string>> ExtractAsync(
        string packagePath,
        string stagingRoot,
        PluginPackageOptions options,
        CancellationToken cancellationToken)
    {
        EnsureDirectoryIsSafe(stagingRoot);

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            if (archive.Entries.Count == 0)
            {
                throw Failure("BAD_ZIP_PACKAGE", "The package archive is empty.");
            }

            if (archive.Entries.Count > options.MaxEntryCount)
            {
                throw Failure(
                    "BAD_ZIP_PACKAGE",
                    $"The package contains {archive.Entries.Count} entries; the limit is {options.MaxEntryCount}.");
            }

            var files = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = NormalizeRelativePath(entry.FullName);
                if (!seenPaths.Add(relativePath))
                {
                    throw Failure(
                        "BAD_ZIP_PACKAGE",
                        $"The package contains a duplicate or case-colliding path '{relativePath}'.");
                }

                if (IsSymlinkOrReparsePoint(entry))
                {
                    throw Failure(
                        "BAD_ZIP_PACKAGE",
                        $"The package contains a symbolic link or reparse-point entry '{relativePath}'.");
                }

                var isDirectory = entry.FullName.EndsWith('/')
                    || entry.FullName.EndsWith('\\');

                if (entry.Length < 0 || entry.CompressedLength < 0)
                {
                    throw Failure(
                        "BAD_ZIP_PACKAGE",
                        $"The package contains an entry with an invalid size '{relativePath}'.");
                }

                if (!isDirectory)
                {
                    if (entry.Length > options.MaxEntryBytes)
                    {
                        throw Failure(
                            "BAD_ZIP_PACKAGE",
                            $"Package entry '{relativePath}' exceeds the per-file size limit.");
                    }

                    if (entry.Length > 0
                        && (entry.CompressedLength == 0
                            || entry.Length / (double)entry.CompressedLength > options.MaxCompressionRatio))
                    {
                        throw Failure(
                            "BAD_ZIP_PACKAGE",
                            $"Package entry '{relativePath}' exceeds the compression-ratio limit.");
                    }

                    totalBytes = checked(totalBytes + entry.Length);
                    if (totalBytes > options.MaxTotalBytes)
                    {
                        throw Failure(
                            "BAD_ZIP_PACKAGE",
                            "The package exceeds the total uncompressed size limit.");
                    }
                }

                var targetPath = GetSafeTargetPath(stagingRoot, relativePath);

                if (isDirectory)
                {
                    EnsureParentDirectoriesAreSafe(stagingRoot, targetPath);
                    Directory.CreateDirectory(targetPath);
                    EnsureDirectoryIsSafe(targetPath);
                    continue;
                }

                var parentDirectory = Path.GetDirectoryName(targetPath)
                    ?? throw Failure("BAD_ZIP_PACKAGE", "A package entry has no parent directory.");
                EnsureParentDirectoriesAreSafe(stagingRoot, parentDirectory);
                Directory.CreateDirectory(parentDirectory);
                EnsureParentDirectoriesAreSafe(stagingRoot, parentDirectory);

                try
                {
                    await using var source = entry.Open();
                    await using var destination = new FileStream(
                        targetPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: 64 * 1024,
                        options: FileOptions.SequentialScan | FileOptions.WriteThrough);

                    var buffer = new byte[64 * 1024];
                    long writtenBytes = 0;

                    while (true)
                    {
                        var read = await source
                            .ReadAsync(buffer.AsMemory(), cancellationToken)
                            .ConfigureAwait(false);

                        if (read == 0)
                        {
                            break;
                        }

                        writtenBytes = checked(writtenBytes + read);
                        if (writtenBytes > options.MaxEntryBytes
                            || writtenBytes > entry.Length)
                        {
                            throw Failure(
                                "BAD_ZIP_PACKAGE",
                                $"Package entry '{relativePath}' expanded beyond its declared limit.");
                        }

                        await destination
                            .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (writtenBytes != entry.Length)
                    {
                        throw Failure(
                            "BAD_ZIP_PACKAGE",
                            $"Package entry '{relativePath}' did not expand to its declared size.");
                    }

                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (PluginPackageException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw Failure(
                        "BAD_ZIP_PACKAGE",
                        $"Package entry '{relativePath}' could not be safely extracted.",
                        exception);
                }

                EnsureFileIsSafe(targetPath);
                files.Add(relativePath);
            }

            return files;
        }
        catch (PluginPackageException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                "The package is not a valid ZIP archive.",
                exception);
        }
        catch (OverflowException exception)
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                "The package declares an unsafe size.",
                exception);
        }
    }

    public static string NormalizeRelativePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Contains('\0'))
        {
            throw Failure("BAD_ZIP_PACKAGE", "The package contains an empty or invalid path.");
        }

        var normalized = rawPath.Replace('\\', '/');
        if (normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                $"The package contains an absolute path '{rawPath}'.");
        }

        normalized = normalized.TrimEnd('/');
        if (normalized.Length == 0)
        {
            throw Failure("BAD_ZIP_PACKAGE", "The package contains a root directory entry.");
        }

        var segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Any(segment => segment is "." or ".." or ""))
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                $"The package contains a traversal or ambiguous path '{rawPath}'.");
        }

        return string.Join('/', segments);
    }

    public static string GetSafeTargetPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var targetPath = Path.GetFullPath(
            Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!targetPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                $"Package entry '{relativePath}' escapes the staging directory.");
        }

        return targetPath;
    }

    public static void EnsureDirectoryIsSafe(string path)
    {
        var fullPath = Path.GetFullPath(path);

        if (Directory.Exists(fullPath)
            && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                $"Directory '{fullPath}' is a reparse point.");
        }
    }

    private static void EnsureFileIsSafe(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure(
                "BAD_ZIP_PACKAGE",
                $"File '{path}' is a reparse point.");
        }
    }

    private static void EnsureParentDirectoriesAreSafe(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(path);

        while (current.Length >= fullRoot.Length)
        {
            EnsureDirectoryIsSafe(current);

            if (string.Equals(current, fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || parent.Length >= current.Length)
            {
                break;
            }

            current = parent;
        }

        throw Failure("BAD_ZIP_PACKAGE", "A package path escaped the staging root.");
    }

    private static bool IsSymlinkOrReparsePoint(ZipArchiveEntry entry)
    {
        var attributes = unchecked((uint)entry.ExternalAttributes);
        var unixFileType = (attributes >> 16) & 0xF000u;

        return unixFileType == 0xA000u
            || (attributes & (uint)FileAttributes.ReparsePoint) != 0;
    }

    private static PluginPackageException Failure(
        string errorCode,
        string message,
        Exception? innerException = null)
    {
        return new PluginPackageException(errorCode, message, innerException);
    }
}
