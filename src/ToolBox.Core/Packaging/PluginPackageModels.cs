using System.Text.Json.Serialization;

namespace ToolBox.Core.Packaging;

public sealed class PluginPackageOptions
{
    public int MaxEntryCount { get; init; } = 512;

    public long MaxEntryBytes { get; init; } = 64L * 1024 * 1024;

    public long MaxTotalBytes { get; init; } = 256L * 1024 * 1024;

    public double MaxCompressionRatio { get; init; } = 1_000;

    public void Validate()
    {
        if (MaxEntryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntryCount));
        }

        if (MaxEntryBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxEntryBytes));
        }

        if (MaxTotalBytes <= 0 || MaxTotalBytes < MaxEntryBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxTotalBytes));
        }

        if (!double.IsFinite(MaxCompressionRatio) || MaxCompressionRatio < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCompressionRatio));
        }
    }
}

public sealed record PluginPackageInstallResult(
    string PluginId,
    string Version,
    string VersionDirectory,
    string? PreviousActiveVersion,
    bool AutomaticRollbackSupported);

public sealed record PluginPackageUninstallResult(
    string PluginId,
    string Version,
    bool WasActive,
    string? ActiveVersionAfterUninstall);

public sealed record PluginDataSnapshot(
    string PluginId,
    string BeforeVersion,
    string SnapshotDirectory,
    int ConfigFileCount,
    int StateFileCount,
    bool AutomaticRollbackSupported);

public enum PluginPackageStatePhase
{
    Idle,
    Staged,
    Activating,
    Committed,
    RollbackPending
}

public sealed record PluginPackageState(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("candidateVersion")] string? CandidateVersion,
    [property: JsonPropertyName("activeVersion")] string? ActiveVersion,
    [property: JsonPropertyName("lastKnownGoodVersion")] string? LastKnownGoodVersion,
    [property: JsonPropertyName("transactionId")] string TransactionId,
    [property: JsonPropertyName("phase")] PluginPackageStatePhase Phase,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("automaticRollbackSupported")] bool AutomaticRollbackSupported,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc)
{
    public static PluginPackageState CreateInitial(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        return new PluginPackageState(
            SchemaVersion: 1,
            PluginId: pluginId,
            CandidateVersion: null,
            ActiveVersion: null,
            LastKnownGoodVersion: null,
            TransactionId: string.Empty,
            Phase: PluginPackageStatePhase.Idle,
            Revision: 0,
            AutomaticRollbackSupported: true,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }
}

internal sealed record PluginPackageMetadata(
    [property: JsonPropertyName("packageFormatVersion")] int PackageFormatVersion,
    [property: JsonPropertyName("pluginId")] string PluginId,
    [property: JsonPropertyName("pluginVersion")] string PluginVersion,
    [property: JsonPropertyName("automaticRollbackSupported")] bool AutomaticRollbackSupported,
    [property: JsonPropertyName("files")] PluginPackageFileHash[] Files);

internal sealed record PluginPackageFileHash(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256);
