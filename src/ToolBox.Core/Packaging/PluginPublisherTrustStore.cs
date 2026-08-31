using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBox.Core.Packaging;

internal sealed class PluginPublisherTrustStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _path;

    internal PluginPublisherTrustStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    internal async Task TrustOnFirstUseAsync(
        VerifiedPluginPublisher publisher,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var existing = document.Publishers.SingleOrDefault(candidate => string.Equals(
            candidate.PublisherId,
            publisher.PublisherId,
            StringComparison.Ordinal));

        if (existing is not null)
        {
            if (existing.Blocked)
            {
                throw new PluginPackageException(
                    "PACKAGE_PUBLISHER_BLOCKED",
                    $"Publisher '{publisher.PublisherId}' is blocked by local policy.");
            }
            if (!string.Equals(
                    existing.CertificateSha256,
                    publisher.CertificateSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginPackageException(
                    "PACKAGE_PUBLISHER_KEY_CHANGED",
                    $"Publisher '{publisher.PublisherId}' was signed by a different certificate than the locally trusted key.");
            }

            return;
        }

        var updated = new PluginPublisherTrustDocument(
            1,
            [.. document.Publishers, new PluginPublisherTrustEntry(
                publisher.PublisherId,
                publisher.CertificateSha256,
                false,
                DateTimeOffset.UtcNow)]);
        await WriteAsync(updated, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PluginPublisherTrustDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new PluginPublisherTrustDocument(1, []);
        }

        try
        {
            var json = await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false);
            var document = JsonSerializer.Deserialize<PluginPublisherTrustDocument>(json, JsonOptions);
            if (document is null || document.SchemaVersion != 1 || document.Publishers is null)
            {
                throw new InvalidDataException("Publisher trust document is incomplete.");
            }

            return document;
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new PluginPackageException(
                "PACKAGE_TRUST_STORE_INVALID",
                "The local publisher trust store could not be read safely.",
                exception);
        }
    }

    private async Task WriteAsync(
        PluginPublisherTrustDocument document,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Publisher trust store path has no parent directory.");
        Directory.CreateDirectory(parent);
        var temporaryPath = _path + ".tmp";

        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    JsonSerializer.Serialize(document, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginPackageException(
                "PACKAGE_TRUST_STORE_WRITE_FAILED",
                "The local publisher trust store could not be updated.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}

internal sealed record PluginPublisherTrustDocument(
    int SchemaVersion,
    PluginPublisherTrustEntry[] Publishers);

internal sealed record PluginPublisherTrustEntry(
    string PublisherId,
    string CertificateSha256,
    bool Blocked,
    DateTimeOffset TrustedAtUtc);
