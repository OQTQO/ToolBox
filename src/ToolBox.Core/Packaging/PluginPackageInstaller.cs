using System.Text.Json;
using System.Text.Json.Serialization;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;

namespace ToolBox.Core.Packaging;

public sealed class PluginPackageInstaller : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _pluginsRoot;
    private readonly string _pluginDataRoot;
    private readonly PluginPackageOptions _options;
    private readonly PluginPackageValidator _packageValidator;
    private readonly PluginPublisherTrustStore _publisherTrustStore;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private int _disposed;

    public PluginPackageInstaller(
        string pluginsRoot,
        string pluginDataRoot,
        PluginPackageOptions? options = null,
        PluginManifestParser? manifestParser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDataRoot);

        _pluginsRoot = Path.GetFullPath(pluginsRoot);
        _pluginDataRoot = Path.GetFullPath(pluginDataRoot);
        _options = options ?? new PluginPackageOptions();
        _options.Validate();
        _packageValidator = new PluginPackageValidator(manifestParser ?? new PluginManifestParser());
        _publisherTrustStore = new PluginPublisherTrustStore(
            Path.Combine(_pluginDataRoot, ".platform", "trusted-publishers.json"));
    }

    public string PluginsRoot => _pluginsRoot;

    public string PluginDataRoot => _pluginDataRoot;

    /// <summary>
    /// Returns plugin roots that may contain a committed installation.
    /// Transaction staging folders are intentionally never exposed to callers.
    /// </summary>
    public IReadOnlyList<string> GetInstalledPluginIds()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        EnsureDirectoryRoot(_pluginsRoot);

        return Directory
            .EnumerateDirectories(_pluginsRoot)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                ".staging",
                StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                try
                {
                    EnsureDirectoryIsSafe(path);
                    ValidatePathSegment(Path.GetFileName(path), "plugin id");
                    return true;
                }
                catch (PluginPackageException)
                {
                    return false;
                }
            })
            .Select(path => Path.GetFileName(path))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public async Task<PluginPackageInstallResult> InstallAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        string? stagingRoot = null;
        string? finalVersionDirectory = null;
        string? pluginRoot = null;
        string? statePath = null;
        string? snapshotDirectory = null;
        PluginPackageState? previousState = null;
        var stateWasWritten = false;
        var stagingWasMoved = false;
        var pluginRootExisted = false;

        try
        {
            var safePackagePath = ValidatePackageFile(packagePath);
            EnsureDirectoryRoot(_pluginsRoot);
            EnsureDirectoryRoot(_pluginDataRoot);

            stagingRoot = CreateStagingRoot();
            var extractedFiles = await SafePackageArchive
                .ExtractAsync(safePackagePath, stagingRoot, _options, cancellationToken)
                .ConfigureAwait(false);

            var validation = await _packageValidator
                .ValidateAsync(stagingRoot, extractedFiles, cancellationToken)
                .ConfigureAwait(false);
            var manifest = validation.Manifest;
            var packageMetadata = validation.Metadata;
            ValidatePathSegment(manifest.Id, "plugin id");
            ValidatePathSegment(manifest.Version, "plugin version");

            pluginRoot = GetPluginRoot(manifest.Id);
            EnsureExistingAncestorsAreSafe(pluginRoot);
            if (File.Exists(pluginRoot))
            {
                throw new PluginPackageException(
                    "PACKAGE_PATH_INVALID",
                    $"Plugin root '{pluginRoot}' is a file.");
            }

            pluginRootExisted = Directory.Exists(pluginRoot);
            if (pluginRootExisted)
            {
                EnsureDirectoryIsSafe(pluginRoot);
            }

            finalVersionDirectory = Path.Combine(
                pluginRoot,
                "versions",
                manifest.Version);
            statePath = Path.Combine(pluginRoot, "state.json");

            if (Directory.Exists(finalVersionDirectory) || File.Exists(finalVersionDirectory))
            {
                throw new PluginPackageException(
                    "PACKAGE_VERSION_ALREADY_INSTALLED",
                    $"Plugin version '{manifest.Id}/{manifest.Version}' is already installed.");
            }

            EnsureExistingAncestorsAreSafe(finalVersionDirectory);

            previousState = await ReadStateAsync(pluginRoot, manifest.Id, cancellationToken)
                .ConfigureAwait(false);
            var snapshot = CreateSnapshot(
                manifest.Id,
                manifest.Version,
                packageMetadata.AutomaticRollbackSupported);
            snapshotDirectory = string.IsNullOrWhiteSpace(snapshot.SnapshotDirectory)
                ? null
                : snapshot.SnapshotDirectory;

            var transactionId = Guid.NewGuid().ToString("N");
            var stagedState = CreateState(
                manifest,
                previousState,
                packageMetadata.AutomaticRollbackSupported,
                transactionId,
                PluginPackageStatePhase.Staged,
                (previousState?.Revision ?? 0) + 1);

            await WriteStateAsync(statePath, stagedState, cancellationToken).ConfigureAwait(false);
            stateWasWritten = true;

            var versionsDirectory = Path.Combine(pluginRoot, "versions");
            EnsureDirectoryRoot(versionsDirectory);
            Directory.Move(stagingRoot, finalVersionDirectory);
            stagingRoot = null;
            stagingWasMoved = true;

            var activatingState = stagedState with
            {
                Phase = PluginPackageStatePhase.Activating,
                Revision = stagedState.Revision + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await WriteStateAsync(statePath, activatingState, cancellationToken).ConfigureAwait(false);

            var committedState = activatingState with
            {
                CandidateVersion = null,
                ActiveVersion = manifest.Version,
                LastKnownGoodVersion = previousState?.ActiveVersion
                    ?? previousState?.LastKnownGoodVersion
                    ?? manifest.Version,
                Phase = PluginPackageStatePhase.Committed,
                Revision = activatingState.Revision + 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await WriteStateAsync(statePath, committedState, cancellationToken).ConfigureAwait(false);
            await _publisherTrustStore
                .TrustOnFirstUseAsync(validation.Publisher, cancellationToken)
                .ConfigureAwait(false);

            return new PluginPackageInstallResult(
                manifest.Id,
                manifest.Version,
                finalVersionDirectory,
                previousState?.ActiveVersion,
                packageMetadata.AutomaticRollbackSupported,
                validation.Publisher.CertificateSha256);
        }
        catch (Exception exception)
        {
            if (stagingWasMoved && finalVersionDirectory is not null)
            {
                TryDeleteDirectory(finalVersionDirectory);
            }

            if (stateWasWritten && statePath is not null)
            {
                await TryRestoreStateAsync(statePath, previousState).ConfigureAwait(false);
            }

            if (snapshotDirectory is not null)
            {
                TryDeleteDirectory(snapshotDirectory);
            }

            if (!pluginRootExisted && pluginRoot is not null)
            {
                TryDeleteDirectory(pluginRoot);
            }

            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (exception is PluginPackageException)
            {
                throw;
            }

            throw new PluginPackageException(
                "PACKAGE_INSTALL_FAILED",
                "The plugin package could not be installed.",
                exception);
        }
        finally
        {
            if (stagingRoot is not null)
            {
                TryDeleteDirectory(stagingRoot);
            }

            _operationGate.Release();
        }
    }

    public async Task<PluginPackageUninstallResult> UninstallAsync(
        string pluginId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ValidatePathSegment(pluginId, "plugin id");
        ValidatePathSegment(version, "plugin version");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var pluginRoot = GetPluginRoot(pluginId);
            EnsureExistingAncestorsAreSafe(pluginRoot);
            if (!Directory.Exists(pluginRoot))
            {
                throw new PluginPackageException(
                    "PACKAGE_VERSION_NOT_FOUND",
                    $"Plugin version '{pluginId}/{version}' is not installed.");
            }

            EnsureDirectoryIsSafe(pluginRoot);
            var versionsDirectory = Path.Combine(pluginRoot, "versions");
            EnsureExistingAncestorsAreSafe(versionsDirectory);

            if (!Directory.Exists(versionsDirectory))
            {
                throw new PluginPackageException(
                    "PACKAGE_VERSION_NOT_FOUND",
                    $"Plugin version '{pluginId}/{version}' is not installed.");
            }

            EnsureDirectoryIsSafe(versionsDirectory);
            var versionDirectory = Path.Combine(pluginRoot, "versions", version);

            if (!Directory.Exists(versionDirectory))
            {
                throw new PluginPackageException(
                    "PACKAGE_VERSION_NOT_FOUND",
                    $"Plugin version '{pluginId}/{version}' is not installed.");
            }

            EnsureDirectoryIsSafe(versionDirectory);

            var statePath = Path.Combine(pluginRoot, "state.json");
            var previousState = await ReadStateAsync(pluginRoot, pluginId, cancellationToken)
                .ConfigureAwait(false);
            var wasActive = string.Equals(
                previousState?.ActiveVersion,
                version,
                StringComparison.OrdinalIgnoreCase);
            var replacementVersion = wasActive
                ? SelectReplacementVersion(pluginRoot, version)
                : previousState?.ActiveVersion;

            var transactionId = Guid.NewGuid().ToString("N");
            var pendingState = CreateUninstallState(
                pluginId,
                previousState,
                transactionId,
                PluginPackageStatePhase.RollbackPending,
                (previousState?.Revision ?? 0) + 1);
            await WriteStateAsync(statePath, pendingState, cancellationToken).ConfigureAwait(false);

            var uninstallRoot = Path.Combine(
                _pluginsRoot,
                ".staging",
                "uninstall",
                transactionId);
            EnsureDirectoryRoot(uninstallRoot);
            var movedVersionDirectory = Path.Combine(uninstallRoot, version);

            try
            {
                Directory.Move(versionDirectory, movedVersionDirectory);

                var committedState = pendingState with
                {
                    CandidateVersion = null,
                    ActiveVersion = replacementVersion,
                    LastKnownGoodVersion = replacementVersion,
                    Phase = PluginPackageStatePhase.Committed,
                    Revision = pendingState.Revision + 1,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await WriteStateAsync(statePath, committedState, cancellationToken).ConfigureAwait(false);
                TryDeleteDirectory(uninstallRoot);

                return new PluginPackageUninstallResult(
                    pluginId,
                    version,
                    wasActive,
                    replacementVersion);
            }
            catch
            {
                if (!Directory.Exists(versionDirectory) && Directory.Exists(movedVersionDirectory))
                {
                    Directory.Move(movedVersionDirectory, versionDirectory);
                }

                await TryRestoreStateAsync(statePath, previousState).ConfigureAwait(false);
                TryDeleteDirectory(uninstallRoot);
                throw;
            }
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (exception is PluginPackageException)
            {
                throw;
            }

            throw new PluginPackageException(
                "PACKAGE_UNINSTALL_FAILED",
                "The plugin package version could not be uninstalled.",
                exception);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<PluginDataSnapshot> SnapshotDataAsync(
        string pluginId,
        string beforeVersion,
        bool automaticRollbackSupported = true,
        CancellationToken cancellationToken = default)
    {
        ValidatePathSegment(pluginId, "plugin id");
        ValidatePathSegment(beforeVersion, "plugin version");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureDirectoryRoot(_pluginDataRoot);
            return CreateSnapshot(pluginId, beforeVersion, automaticRollbackSupported);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public IReadOnlyList<string> GetInstalledVersions(string pluginId)
    {
        ValidatePathSegment(pluginId, "plugin id");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var versionsDirectory = Path.Combine(GetPluginRoot(pluginId), "versions");

        EnsureExistingAncestorsAreSafe(versionsDirectory);

        if (!Directory.Exists(versionsDirectory))
        {
            return Array.Empty<string>();
        }

        EnsureDirectoryIsSafe(versionsDirectory);
        return Directory
            .EnumerateDirectories(versionsDirectory)
            .Where(path =>
            {
                try
                {
                    EnsureDirectoryIsSafe(path);
                    return true;
                }
                catch (PluginPackageException)
                {
                    return false;
                }
            })
            .Select(Path.GetFileName)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public string? GetActiveVersionDirectory(string pluginId)
    {
        ValidatePathSegment(pluginId, "plugin id");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var pluginRoot = GetPluginRoot(pluginId);
        EnsureExistingAncestorsAreSafe(pluginRoot);

        if (!Directory.Exists(pluginRoot))
        {
            return null;
        }

        EnsureDirectoryIsSafe(pluginRoot);
        var statePath = Path.Combine(pluginRoot, "state.json");
        if (!File.Exists(statePath))
        {
            return null;
        }

        var state = ReadStateAsync(pluginRoot, pluginId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (state is null || string.IsNullOrWhiteSpace(state.ActiveVersion))
        {
            return null;
        }

        if (state.Phase != PluginPackageStatePhase.Committed)
        {
            throw new PluginPackageException(
                "PACKAGE_STATE_NOT_COMMITTED",
                $"The active plugin state is not committed (phase: '{state.Phase}').");
        }

        try
        {
            ValidatePathSegment(state.ActiveVersion, "active plugin version");
        }
        catch (PluginPackageException exception)
        {
            throw new PluginPackageException(
                "PACKAGE_STATE_INVALID",
                "The active plugin version in package state is invalid.",
                exception);
        }

        var versionDirectory = Path.Combine(
            pluginRoot,
            "versions",
            state.ActiveVersion);
        EnsureExistingAncestorsAreSafe(versionDirectory);

        if (!Directory.Exists(versionDirectory))
        {
            throw new PluginPackageException(
                "PACKAGE_ACTIVE_VERSION_MISSING",
                $"The active plugin version directory is missing: '{versionDirectory}'.");
        }

        EnsureDirectoryIsSafe(versionDirectory);
        return Path.GetFullPath(versionDirectory);
    }

    private PluginDataSnapshot CreateSnapshot(
        string pluginId,
        string beforeVersion,
        bool automaticRollbackSupported)
    {
        if (!automaticRollbackSupported)
        {
            return new PluginDataSnapshot(
                pluginId,
                beforeVersion,
                string.Empty,
                0,
                0,
                false);
        }

        var currentRoot = Path.Combine(_pluginDataRoot, pluginId, "current");
        var snapshotRoot = Path.Combine(
            _pluginDataRoot,
            pluginId,
            "rollback",
            "before-" + beforeVersion);
        if (Directory.Exists(snapshotRoot) || File.Exists(snapshotRoot))
        {
            snapshotRoot += "-" + Guid.NewGuid().ToString("N");
        }
        var snapshotCreated = false;

        try
        {
            EnsureExistingAncestorsAreSafe(currentRoot);
            if (Directory.Exists(currentRoot))
            {
                EnsureDirectoryIsSafe(currentRoot);
            }

            EnsureExistingAncestorsAreSafe(snapshotRoot);
            EnsureDirectoryRoot(snapshotRoot);
            snapshotCreated = true;
            var configCount = CopyDataDirectory(
                Path.Combine(currentRoot, "config"),
                Path.Combine(snapshotRoot, "config"));
            var stateCount = CopyDataDirectory(
                Path.Combine(currentRoot, "state"),
                Path.Combine(snapshotRoot, "state"));

            return new PluginDataSnapshot(
                pluginId,
                beforeVersion,
                snapshotRoot,
                configCount,
                stateCount,
                true);
        }
        catch (PluginPackageException)
        {
            if (snapshotCreated)
            {
                TryDeleteDirectory(snapshotRoot);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (snapshotCreated)
            {
                TryDeleteDirectory(snapshotRoot);
            }

            throw new PluginPackageException(
                "PACKAGE_SNAPSHOT_FAILED",
                "Plugin Config/State could not be safely snapshotted.",
                exception);
        }
    }

    private static int CopyDataDirectory(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return 0;
        }

        EnsureDirectoryIsSafe(sourceDirectory);
        EnsureDirectoryRoot(targetDirectory);
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            EnsureFileIsSafe(file);
            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            EnsureExistingAncestorsAreSafe(targetFile);
            if (File.Exists(targetFile))
            {
                throw new PluginPackageException(
                    "PACKAGE_SNAPSHOT_FAILED",
                    $"The snapshot target '{targetFile}' already exists.");
            }

            File.Copy(file, targetFile, overwrite: false);
            EnsureFileIsSafe(targetFile);
            count++;
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            EnsureDirectoryIsSafe(directory);
            count += CopyDataDirectory(
                directory,
                Path.Combine(targetDirectory, Path.GetFileName(directory)));
        }

        return count;
    }

    private static PluginPackageState CreateState(
        PluginManifest manifest,
        PluginPackageState? previousState,
        bool automaticRollbackSupported,
        string transactionId,
        PluginPackageStatePhase phase,
        long revision)
    {
        return new PluginPackageState(
            SchemaVersion: 1,
            PluginId: manifest.Id,
            CandidateVersion: manifest.Version,
            ActiveVersion: previousState?.ActiveVersion,
            LastKnownGoodVersion: previousState?.LastKnownGoodVersion,
            TransactionId: transactionId,
            Phase: phase,
            Revision: revision,
            AutomaticRollbackSupported: automaticRollbackSupported,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static PluginPackageState CreateUninstallState(
        string pluginId,
        PluginPackageState? previousState,
        string transactionId,
        PluginPackageStatePhase phase,
        long revision)
    {
        return new PluginPackageState(
            SchemaVersion: 1,
            PluginId: pluginId,
            CandidateVersion: null,
            ActiveVersion: previousState?.ActiveVersion,
            LastKnownGoodVersion: previousState?.LastKnownGoodVersion,
            TransactionId: transactionId,
            Phase: phase,
            Revision: revision,
            AutomaticRollbackSupported: previousState?.AutomaticRollbackSupported ?? true,
            UpdatedAtUtc: DateTimeOffset.UtcNow);
    }

    private static async Task<PluginPackageState?> ReadStateAsync(
        string pluginRoot,
        string pluginId,
        CancellationToken cancellationToken)
    {
        var statePath = Path.Combine(pluginRoot, "state.json");
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            EnsureFileIsSafe(statePath);
            var json = await File.ReadAllTextAsync(statePath, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<PluginPackageState>(json, JsonOptions);

            if (state is null
                || state.SchemaVersion != 1
                || !string.Equals(state.PluginId, pluginId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The package state identity or schema is invalid.");
            }

            return state;
        }
        catch (PluginPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new PluginPackageException(
                "PACKAGE_STATE_INVALID",
                "The plugin version state could not be read safely.",
                exception);
        }
    }

    private static async Task WriteStateAsync(
        string statePath,
        PluginPackageState state,
        CancellationToken cancellationToken)
    {
        var parentDirectory = Path.GetDirectoryName(statePath)
            ?? throw new PluginPackageException(
                "PACKAGE_STATE_WRITE_FAILED",
                "The plugin state path has no parent directory.");
        EnsureDirectoryRoot(parentDirectory);

        if (File.Exists(statePath))
        {
            EnsureFileIsSafe(statePath);
        }

        var temporaryPath = statePath + ".tmp";
        var previousPath = statePath + ".previous";

        if (File.Exists(temporaryPath))
        {
            EnsureFileIsSafe(temporaryPath);
        }

        if (File.Exists(previousPath))
        {
            EnsureFileIsSafe(previousPath);
        }

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 8 * 1024,
                             options: FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(statePath))
            {
                try
                {
                    File.Replace(temporaryPath, statePath, previousPath, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(statePath, previousPath, overwrite: true);
                    File.Move(temporaryPath, statePath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, statePath);
            }
        }
        catch (PluginPackageException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginPackageException(
                "PACKAGE_STATE_WRITE_FAILED",
                "Plugin version state could not be written atomically.",
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
                    // Preserve the original state error; the next operation will reject a stale temp file.
                }
            }
        }
    }

    private static async Task TryRestoreStateAsync(
        string statePath,
        PluginPackageState? previousState)
    {
        try
        {
            if (previousState is null)
            {
                if (File.Exists(statePath))
                {
                    File.Delete(statePath);
                }
            }
            else
            {
                await WriteStateAsync(statePath, previousState, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
            // The original package operation remains the actionable failure.
        }
    }

    private static string? SelectReplacementVersion(string pluginRoot, string removedVersion)
    {
        EnsureExistingAncestorsAreSafe(pluginRoot);

        if (!Directory.Exists(pluginRoot))
        {
            return null;
        }

        EnsureDirectoryIsSafe(pluginRoot);
        var versionsDirectory = Path.Combine(pluginRoot, "versions");
        EnsureExistingAncestorsAreSafe(versionsDirectory);

        if (!Directory.Exists(versionsDirectory))
        {
            return null;
        }

        EnsureDirectoryIsSafe(versionsDirectory);

        var candidates = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(versionsDirectory))
        {
            EnsureDirectoryIsSafe(directory);
            var version = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(version)
                && !string.Equals(version, removedVersion, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(version);
            }
        }

        return candidates
            .OrderByDescending(version => version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private string CreateStagingRoot()
    {
        var stagingParent = Path.Combine(_pluginsRoot, ".staging");
        EnsureDirectoryRoot(stagingParent);

        var stagingRoot = Path.Combine(stagingParent, Guid.NewGuid().ToString("N"));
        EnsureDirectoryRoot(stagingRoot);
        return stagingRoot;
    }

    private string GetPluginRoot(string pluginId)
    {
        return Path.Combine(_pluginsRoot, pluginId);
    }

    private static string ValidatePackageFile(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var fullPath = Path.GetFullPath(packagePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The plugin package was not found.", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".tpk", StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginPackageException(
                "PACKAGE_EXTENSION_INVALID",
                "Plugin packages must use the .tpk extension.");
        }

        try
        {
            EnsureFileIsSafe(fullPath);
        }
        catch (PluginPackageException exception)
        {
            throw new PluginPackageException(
                "BAD_ZIP_PACKAGE",
                "The package file is a reparse point and cannot be trusted.",
                exception);
        }

        return fullPath;
    }

    private static void ValidatePathSegment(string value, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value is "." or ".."
            || value.Any(character =>
                char.IsWhiteSpace(character)
                || character is '/' or '\\' or ':'
                || Path.GetInvalidFileNameChars().Contains(character)))
        {
            throw new PluginPackageException(
                "PACKAGE_PATH_INVALID",
                $"The {label} cannot be used as a directory name.");
        }
    }

    private static void EnsureDirectoryRoot(string path)
    {
        EnsureExistingAncestorsAreSafe(path);

        if (File.Exists(path))
        {
            throw new PluginPackageException(
                "PACKAGE_PATH_INVALID",
                $"Directory path '{path}' is occupied by a file.");
        }

        Directory.CreateDirectory(path);
        EnsureExistingAncestorsAreSafe(path);
        EnsureDirectoryIsSafe(path);
    }

    private static void EnsureExistingAncestorsAreSafe(string path)
    {
        var current = Path.GetFullPath(path);

        while (true)
        {
            if (File.Exists(current))
            {
                throw new PluginPackageException(
                    "PACKAGE_PATH_INVALID",
                    $"Path component '{current}' is a file.");
            }

            if (Directory.Exists(current))
            {
                EnsureDirectoryIsSafe(current);
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = parent;
        }
    }

    private static void EnsureDirectoryIsSafe(string path)
    {
        if (Directory.Exists(path)
            && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new PluginPackageException(
                "PACKAGE_PATH_INVALID",
                $"Directory '{path}' is a reparse point.");
        }
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                EnsureDirectoryIsSafe(path);
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup must not replace the original package failure.
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

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _operationGate.Dispose();
        }
    }
}
