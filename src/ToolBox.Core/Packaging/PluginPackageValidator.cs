using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Packaging;

internal sealed class PluginPackageValidator
{
    private const int PackageFormatVersion = 2;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly PluginManifestParser _manifestParser;

    internal PluginPackageValidator(PluginManifestParser manifestParser)
    {
        _manifestParser = manifestParser ?? throw new ArgumentNullException(nameof(manifestParser));
    }

    internal async Task<(
        PluginManifest Manifest,
        PluginPackageMetadata Metadata,
        VerifiedPluginPublisher Publisher)> ValidateAsync(
        string stagingRoot,
        IReadOnlyList<string> extractedFiles,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        ArgumentNullException.ThrowIfNull(extractedFiles);

        var manifest = await ReadManifestAsync(stagingRoot, cancellationToken).ConfigureAwait(false);
        var metadataResult = await ReadPackageMetadataAsync(stagingRoot, cancellationToken).ConfigureAwait(false);
        var metadata = metadataResult.Metadata;
        ValidatePackageMetadata(metadata, manifest);
        var publisher = await VerifyPublisherSignatureAsync(
                stagingRoot,
                manifest,
                metadataResult.RawBytes,
                cancellationToken)
            .ConfigureAwait(false);
        await ValidateHashesAsync(stagingRoot, extractedFiles, metadata, cancellationToken)
            .ConfigureAwait(false);
        ValidateStructuralSmoke(stagingRoot, manifest);
        return (manifest, metadata, publisher);
    }

    private async Task<PluginManifest> ReadManifestAsync(
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(stagingRoot, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The package does not contain a root manifest.json file.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            return _manifestParser.Parse(json);
        }
        catch (PluginManifestValidationException exception)
        {
            var errorCode = exception.Errors.Any(error =>
                    error.Code == "PLUGIN_API_MAJOR_UNSUPPORTED")
                ? "INCOMPATIBLE_API_PLUGIN"
                : "BAD_MANIFEST_PACKAGE";
            throw new PluginPackageException(
                errorCode,
                "The package manifest failed PluginSdk validation.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The package manifest could not be read.",
                exception);
        }
    }

    private static async Task<(PluginPackageMetadata Metadata, byte[] RawBytes)> ReadPackageMetadataAsync(
        string stagingRoot,
        CancellationToken cancellationToken)
    {
        var packageManifestPath = Path.Combine(stagingRoot, "package.json");

        if (!File.Exists(packageManifestPath))
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The package does not contain a root package.json file.");
        }

        try
        {
            var rawBytes = await File.ReadAllBytesAsync(packageManifestPath, cancellationToken).ConfigureAwait(false);
            var metadata = JsonSerializer.Deserialize<PluginPackageMetadata>(rawBytes, JsonOptions);

            if (metadata is null || metadata.Files is null)
            {
                throw new PluginPackageException(
                    "BAD_MANIFEST_PACKAGE",
                    "The package metadata is empty or incomplete.");
            }

            return (metadata, rawBytes);
        }
        catch (PluginPackageException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The package metadata JSON is malformed.",
                exception);
        }
        catch (IOException exception)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "The package metadata could not be read.",
                exception);
        }
    }

    private static async Task<VerifiedPluginPublisher> VerifyPublisherSignatureAsync(
        string stagingRoot,
        PluginManifest manifest,
        byte[] packageMetadataBytes,
        CancellationToken cancellationToken)
    {
        var signaturePath = Path.Combine(stagingRoot, "signature.json");
        if (!File.Exists(signaturePath))
        {
            throw new PluginPackageException(
                "PACKAGE_SIGNATURE_REQUIRED",
                "Package format 2 requires a root signature.json file.");
        }
        if (new FileInfo(signaturePath).Length > 1024 * 1024)
        {
            throw SignatureInvalid("Package signature metadata cannot exceed 1 MiB.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(signaturePath, cancellationToken).ConfigureAwait(false);
            var signature = JsonSerializer.Deserialize<PluginPackageSignature>(json, JsonOptions)
                ?? throw SignatureInvalid("Package signature metadata is empty.");

            if (signature.SchemaVersion != 1
                || !string.Equals(signature.Algorithm, "rsa-sha256", StringComparison.Ordinal)
                || !string.Equals(signature.Payload, "package.json", StringComparison.Ordinal))
            {
                throw SignatureInvalid("Package signature schema, algorithm, or payload is unsupported.");
            }
            if (!string.Equals(signature.PublisherId, manifest.Publisher, StringComparison.Ordinal))
            {
                throw SignatureInvalid("Package signature publisherId does not match manifest publisher.");
            }

            var certificateBytes = Convert.FromBase64String(signature.Certificate);
            var signatureBytes = Convert.FromBase64String(signature.Signature);
            using var certificate = X509CertificateLoader.LoadCertificate(certificateBytes);
            var now = DateTime.UtcNow;
            if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
            {
                throw SignatureInvalid("Package signing certificate is outside its validity period.");
            }

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is null || !rsa.VerifyData(
                    packageMetadataBytes,
                    signatureBytes,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
            {
                throw SignatureInvalid("Package signature verification failed.");
            }

            var certificateSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData))
                .ToLowerInvariant();
            return new VerifiedPluginPublisher(signature.PublisherId, certificateSha256);
        }
        catch (PluginPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or FormatException
            or CryptographicException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new PluginPackageException(
                "PACKAGE_SIGNATURE_INVALID",
                "Package signature could not be validated.",
                exception);
        }
    }

    private static void ValidatePackageMetadata(
        PluginPackageMetadata metadata,
        PluginManifest manifest)
    {
        if (metadata.PackageFormatVersion != PackageFormatVersion)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                $"Package format version '{metadata.PackageFormatVersion}' is not supported.");
        }

        if (!string.Equals(metadata.PluginId, manifest.Id, StringComparison.Ordinal))
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "Package metadata pluginId does not match manifest.json.");
        }

        if (!string.Equals(metadata.PluginVersion, manifest.Version, StringComparison.Ordinal))
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "Package metadata pluginVersion does not match manifest.json.");
        }

        if (metadata.Files.Length == 0)
        {
            throw new PluginPackageException(
                "BAD_MANIFEST_PACKAGE",
                "Package metadata must contain at least one payload hash.");
        }
    }

    private static async Task ValidateHashesAsync(
        string stagingRoot,
        IReadOnlyList<string> extractedFiles,
        PluginPackageMetadata metadata,
        CancellationToken cancellationToken)
    {
        var payloadFiles = extractedFiles
            .Where(path => !string.Equals(path, "package.json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, "signature.json", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in metadata.Files)
        {
            string normalizedPath;

            try
            {
                normalizedPath = SafePackageArchive.NormalizeRelativePath(file.Path);
            }
            catch (PluginPackageException exception)
            {
                throw new PluginPackageException(
                    "BAD_MANIFEST_PACKAGE",
                    "Package metadata contains an invalid file path.",
                    exception);
            }

            if (string.Equals(normalizedPath, "package.json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedPath, "signature.json", StringComparison.OrdinalIgnoreCase)
                || !expectedFiles.TryAdd(normalizedPath, file.Sha256 ?? string.Empty))
            {
                throw new PluginPackageException(
                    "BAD_MANIFEST_PACKAGE",
                    $"Package metadata contains a duplicate or self-referential hash path '{normalizedPath}'.");
            }

            if (!IsSha256(file.Sha256))
            {
                throw new PluginPackageException(
                    "BAD_MANIFEST_PACKAGE",
                    $"Package metadata contains an invalid SHA-256 for '{normalizedPath}'.");
            }
        }

        if (!payloadFiles.SetEquals(expectedFiles.Keys))
        {
            var missing = payloadFiles.Except(expectedFiles.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
            var extra = expectedFiles.Keys.Except(payloadFiles, StringComparer.OrdinalIgnoreCase).ToArray();
            throw new PluginPackageException(
                "PACKAGE_HASH_MISMATCH",
                $"Package hash list does not match payload files. Missing hashes: {string.Join(", ", missing)}; extra hashes: {string.Join(", ", extra)}.");
        }

        foreach (var expected in expectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = SafePackageArchive.GetSafeTargetPath(stagingRoot, expected.Key);
            var actualHash = await ComputeSha256Async(filePath, cancellationToken).ConfigureAwait(false);

            if (!string.Equals(actualHash, expected.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginPackageException(
                    "PACKAGE_HASH_MISMATCH",
                    $"SHA-256 validation failed for package entry '{expected.Key}'.");
            }
        }
    }

    private static PluginPackageException SignatureInvalid(string message)
    {
        return new PluginPackageException("PACKAGE_SIGNATURE_INVALID", message);
    }

    private static void ValidateStructuralSmoke(string stagingRoot, PluginManifest manifest)
    {
        var entryPointSeparator = manifest.EntryPoint.IndexOf(',');
        if (entryPointSeparator <= 0 || entryPointSeparator == manifest.EntryPoint.Length - 1)
        {
            throw new PluginPackageException(
                "PACKAGE_ENTRY_POINT_INVALID",
                "The package entryPoint must use the format 'Namespace.Type, AssemblyName'.");
        }

        string assemblyName;
        try
        {
            assemblyName = new AssemblyName(
                    manifest.EntryPoint[(entryPointSeparator + 1)..].Trim())
                .Name
                ?? throw new ArgumentException("Assembly name is empty.");
        }
        catch (Exception exception) when (exception is ArgumentException or FileLoadException)
        {
            throw new PluginPackageException(
                "PACKAGE_ENTRY_POINT_INVALID",
                "The package entryPoint contains an invalid assembly name.",
                exception);
        }

        var candidatePaths = new[]
        {
            Path.Combine(stagingRoot, assemblyName + ".dll"),
            Path.Combine(stagingRoot, "runtime", assemblyName + ".dll")
        };
        var matches = candidatePaths.Where(File.Exists).ToArray();

        if (matches.Length != 1)
        {
            throw new PluginPackageException(
                "PACKAGE_ENTRY_ASSEMBLY_MISSING",
                $"The package does not contain exactly one runtime assembly for '{assemblyName}'.");
        }

        if (Directory
                .EnumerateFiles(stagingRoot, "ToolBox.PluginSdk.dll", SearchOption.AllDirectories)
                .Any())
        {
            throw new PluginPackageException(
                "PACKAGE_DUPLICATE_PLUGIN_SDK",
                "The package must not carry a private copy of ToolBox.PluginSdk.dll.");
        }
    }

    private static bool IsSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
        {
            return false;
        }

        return value.All(Uri.IsHexDigit);
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureFileIsSafe(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void EnsureFileIsSafe(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new PluginPackageException(
                "PACKAGE_PATH_INVALID",
                $"File '{path}' is a reparse point.");
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
