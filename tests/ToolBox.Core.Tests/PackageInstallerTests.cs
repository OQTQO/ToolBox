using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HappyPathPlugin;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class PackageInstallerTests
{
    private static readonly string[] Version01 = ["0.1.0"];
    private static readonly string[] Versions01And02 = ["0.1.0", "0.2.0"];
    private static readonly string[] Version02 = ["0.2.0"];

    [Fact]
    public async Task PackageInstallsSideBySideSnapshotsDataLoadsAndUninstalls()
    {
        var root = CreateTemporaryRoot();
        var pluginsRoot = Path.Combine(root, "Plugins");
        var dataRoot = Path.Combine(root, "PluginData");

        try
        {
            using var installer = new PluginPackageInstaller(pluginsRoot, dataRoot);
            var packageV1 = CreateHappyPathPackage(root, "0.1.0");
            var installedV1 = await installer.InstallAsync(packageV1);

            Assert.Equal("com.toolbox.happy-path", installedV1.PluginId);
            Assert.Equal("0.1.0", installedV1.Version);
            Assert.Null(installedV1.PreviousActiveVersion);
            Assert.True(installedV1.AutomaticRollbackSupported);
            Assert.Matches("^[a-f0-9]{64}$", installedV1.PublisherCertificateSha256);
            Assert.Equal(Version01, installer.GetInstalledVersions(installedV1.PluginId));
            Assert.Equal(installedV1.VersionDirectory, installer.GetActiveVersionDirectory(installedV1.PluginId));

            var runtime = new InProcessPluginRuntime();
            var discovered = runtime.DiscoverSingle(installedV1.VersionDirectory);
            await using (var loaded = runtime.Load(discovered))
            {
                await loaded.StartAsync();
                await loaded.StopAndUnloadAsync();
            }

            var currentDataRoot = Path.Combine(dataRoot, installedV1.PluginId, "current");
            Directory.CreateDirectory(Path.Combine(currentDataRoot, "config"));
            Directory.CreateDirectory(Path.Combine(currentDataRoot, "state"));
            await File.WriteAllTextAsync(
                Path.Combine(currentDataRoot, "config", "settings.json"),
                "{\"theme\":\"dark\"}");
            await File.WriteAllTextAsync(
                Path.Combine(currentDataRoot, "state", "session.json"),
                "{\"counter\":7}");

            var packageV2 = CreateHappyPathPackage(root, "0.2.0");
            var installedV2 = await installer.InstallAsync(packageV2);

            Assert.Equal("0.1.0", installedV2.PreviousActiveVersion);
            Assert.Equal(Versions01And02, installer.GetInstalledVersions(installedV1.PluginId));
            Assert.Equal(installedV2.VersionDirectory, installer.GetActiveVersionDirectory(installedV1.PluginId));

            var statePath = Path.Combine(pluginsRoot, installedV1.PluginId, "state.json");
            using (var stateDocument = JsonDocument.Parse(await File.ReadAllTextAsync(statePath)))
            {
                var state = stateDocument.RootElement;
                Assert.Equal("0.2.0", state.GetProperty("activeVersion").GetString());
                Assert.Equal("0.1.0", state.GetProperty("lastKnownGoodVersion").GetString());
                Assert.Equal("committed", state.GetProperty("phase").GetString());
                Assert.True(state.GetProperty("automaticRollbackSupported").GetBoolean());
            }

            var rollbackRoot = Path.Combine(
                dataRoot,
                installedV1.PluginId,
                "rollback",
                "before-0.2.0");
            Assert.Equal(
                "{\"theme\":\"dark\"}",
                await File.ReadAllTextAsync(Path.Combine(rollbackRoot, "config", "settings.json")));
            Assert.Equal(
                "{\"counter\":7}",
                await File.ReadAllTextAsync(Path.Combine(rollbackRoot, "state", "session.json")));

            var uninstalledV1 = await installer.UninstallAsync(installedV1.PluginId, "0.1.0");
            Assert.False(uninstalledV1.WasActive);
            Assert.Equal("0.2.0", uninstalledV1.ActiveVersionAfterUninstall);
            Assert.Equal(Version02, installer.GetInstalledVersions(installedV1.PluginId));
            Assert.Equal(installedV2.VersionDirectory, installer.GetActiveVersionDirectory(installedV1.PluginId));
            Assert.True(File.Exists(Path.Combine(currentDataRoot, "config", "settings.json")));

            var uninstalledV2 = await installer.UninstallAsync(installedV1.PluginId, "0.2.0");
            Assert.True(uninstalledV2.WasActive);
            Assert.Null(uninstalledV2.ActiveVersionAfterUninstall);
            Assert.Empty(installer.GetInstalledVersions(installedV1.PluginId));
            Assert.Null(installer.GetActiveVersionDirectory(installedV1.PluginId));
            Assert.True(File.Exists(Path.Combine(currentDataRoot, "state", "session.json")));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ExistingRollbackSnapshotDoesNotBlockPortableHostInstall()
    {
        var root = CreateTemporaryRoot();
        var dataRoot = Path.Combine(root, "PluginData");

        try
        {
            var package = CreateHappyPathPackage(root, "0.1.0");
            using (var firstInstaller = new PluginPackageInstaller(
                       Path.Combine(root, "HostA", "Plugins"),
                       dataRoot))
            {
                await firstInstaller.InstallAsync(package);
            }

            using (var secondInstaller = new PluginPackageInstaller(
                       Path.Combine(root, "HostB", "Plugins"),
                       dataRoot))
            {
                var installed = await secondInstaller.InstallAsync(package);
                Assert.Equal("0.1.0", installed.Version);
                Assert.NotNull(secondInstaller.GetActiveVersionDirectory(installed.PluginId));
            }

            var rollbackRoot = Path.Combine(
                dataRoot,
                "com.toolbox.happy-path",
                "rollback");
            var snapshots = Directory.GetDirectories(rollbackRoot)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(2, snapshots.Length);
            Assert.Contains("before-0.1.0", snapshots);
            Assert.Contains(snapshots, name => name?.StartsWith(
                "before-0.1.0-",
                StringComparison.Ordinal) == true);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task BadZipPackageRejectsTraversalAndCleansStaging()
    {
        var root = CreateTemporaryRoot();
        var pluginsRoot = Path.Combine(root, "Plugins");
        var packagePath = CreateArchive(
            root,
            "bad-zip.tpk",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["../escaped.txt"] = Encoding.UTF8.GetBytes("escape")
            });

        try
        {
            using var installer = new PluginPackageInstaller(pluginsRoot, Path.Combine(root, "PluginData"));
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal("BAD_ZIP_PACKAGE", exception.ErrorCode);
            Assert.False(File.Exists(Path.Combine(root, "escaped.txt")));
            AssertNoStagingChildren(pluginsRoot);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ActiveLocatorRejectsAnUncommittedState()
    {
        var root = CreateTemporaryRoot();

        try
        {
            var pluginsRoot = Path.Combine(root, "Plugins");
            using var installer = new PluginPackageInstaller(
                pluginsRoot,
                Path.Combine(root, "PluginData"));
            var installed = await installer.InstallAsync(CreateHappyPathPackage(root, "0.1.0"));
            var statePath = Path.Combine(pluginsRoot, installed.PluginId, "state.json");
            var stateJson = await File.ReadAllTextAsync(statePath);
            stateJson = stateJson.Replace(
                "\"phase\": \"committed\"",
                "\"phase\": \"activating\"",
                StringComparison.Ordinal);
            await File.WriteAllTextAsync(statePath, stateJson);

            var exception = Assert.Throws<PluginPackageException>(
                () => installer.GetActiveVersionDirectory(installed.PluginId));

            Assert.Equal("PACKAGE_STATE_NOT_COMMITTED", exception.ErrorCode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task ActiveLocatorRejectsAMissingActiveVersionDirectory()
    {
        var root = CreateTemporaryRoot();

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var installed = await installer.InstallAsync(CreateHappyPathPackage(root, "0.1.0"));
            Directory.Delete(installed.VersionDirectory, recursive: true);

            var exception = Assert.Throws<PluginPackageException>(
                () => installer.GetActiveVersionDirectory(installed.PluginId));

            Assert.Equal("PACKAGE_ACTIVE_VERSION_MISSING", exception.ErrorCode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task PackageMetadataRequiresSupportedFormatAndMatchingIdentity()
    {
        var root = CreateTemporaryRoot();

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));

            var unsupportedFormat = CreateHappyPathPackage(
                root,
                "0.5.0",
                packageFormatVersion: 1);
            var formatException = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(unsupportedFormat));
            Assert.Equal("BAD_MANIFEST_PACKAGE", formatException.ErrorCode);

            var mismatchedIdentity = CreateHappyPathPackage(
                root,
                "0.5.1",
                metadataPluginId: "com.toolbox.other-plugin");
            var identityException = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(mismatchedIdentity));
            Assert.Equal("BAD_MANIFEST_PACKAGE", identityException.ErrorCode);
            Assert.Empty(installer.GetInstalledVersions("com.toolbox.happy-path"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task BadZipPackageRejectsCaseCollision()
    {
        var root = CreateTemporaryRoot();
        var packagePath = CreateArchive(
            root,
            "case-collision.tpk",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["runtime/plugin.dll"] = [1, 2, 3],
                ["runtime/PLUGIN.dll"] = [4, 5, 6]
            });

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal("BAD_ZIP_PACKAGE", exception.ErrorCode);
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task BadManifestPackageIsRejectedBeforeInstall()
    {
        var root = CreateTemporaryRoot();
        var packagePath = CreateArchive(
            root,
            "bad-manifest.tpk",
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["manifest.json"] = Encoding.UTF8.GetBytes("{ \"formatVersion\": 1,"),
                ["package.json"] = Encoding.UTF8.GetBytes("{}")
            });

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal("BAD_MANIFEST_PACKAGE", exception.ErrorCode);
            AssertNoStagingChildren(Path.Combine(root, "Plugins"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task IncompatibleApiPackageIsRejectedWithExplicitError()
    {
        var root = CreateTemporaryRoot();
        var packagePath = CreateHappyPathPackage(root, "0.3.0", pluginApiMajor: 2);

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal("INCOMPATIBLE_API_PLUGIN", exception.ErrorCode);
            Assert.False(Directory.Exists(Path.Combine(
                root,
                "Plugins",
                "com.toolbox.happy-path")));
            AssertNoStagingChildren(Path.Combine(root, "Plugins"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task HashMismatchLeavesNoInstalledVersion()
    {
        var root = CreateTemporaryRoot();
        var packagePath = CreateHappyPathPackage(root, "0.4.0", tamperHash: true);

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal("PACKAGE_HASH_MISMATCH", exception.ErrorCode);
            AssertNoStagingChildren(Path.Combine(root, "Plugins"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Theory]
    [InlineData(true, false, "PACKAGE_SIGNATURE_REQUIRED")]
    [InlineData(false, true, "PACKAGE_SIGNATURE_INVALID")]
    public async Task PackageRequiresAValidPublisherSignature(
        bool omitSignature,
        bool tamperSignature,
        string expectedErrorCode)
    {
        var root = CreateTemporaryRoot();
        var packagePath = CreateHappyPathPackage(
            root,
            "0.4.1",
            omitSignature: omitSignature,
            tamperSignature: tamperSignature);

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal(expectedErrorCode, exception.ErrorCode);
            AssertNoStagingChildren(Path.Combine(root, "Plugins"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task TrustOnFirstUseRejectsPublisherKeyChanges()
    {
        var root = CreateTemporaryRoot();

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));
            await installer.InstallAsync(CreateHappyPathPackage(root, "0.4.2"));

            using var replacementSigner = new TestPackageSigner();
            var replacementPackage = CreateHappyPathPackage(
                root,
                "0.4.3",
                signer: replacementSigner);
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(replacementPackage));

            Assert.Equal("PACKAGE_PUBLISHER_KEY_CHANGED", exception.ErrorCode);
            Assert.Equal(["0.4.2"], installer.GetInstalledVersions("com.toolbox.happy-path"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Fact]
    public async Task FailedInstallDoesNotPersistPublisherTrust()
    {
        var root = CreateTemporaryRoot();
        var pluginsRoot = Path.Combine(root, "Plugins");
        var dataRoot = Path.Combine(root, "PluginData");
        Directory.CreateDirectory(pluginsRoot);
        await File.WriteAllTextAsync(
            Path.Combine(pluginsRoot, "com.toolbox.happy-path"),
            "This file intentionally blocks creation of the plugin directory.");

        try
        {
            using var installer = new PluginPackageInstaller(pluginsRoot, dataRoot);
            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(CreateHappyPathPackage(root, "0.4.4")));

            Assert.Equal("PACKAGE_PATH_INVALID", exception.ErrorCode);
            Assert.False(File.Exists(Path.Combine(
                dataRoot,
                ".platform",
                "trusted-publishers.json")));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Theory]
    [InlineData(true, false, "PACKAGE_DUPLICATE_PLUGIN_SDK")]
    [InlineData(false, true, "PACKAGE_ENTRY_ASSEMBLY_MISSING")]
    public async Task StructuralValidationRejectsUnsafeRuntimeLayouts(
        bool includePrivateSdk,
        bool omitRuntimeAssembly,
        string expectedErrorCode)
    {
        var root = CreateTemporaryRoot();
        var packagePath = CreateHappyPathPackage(
            root,
            "0.6.0",
            includePrivateSdk: includePrivateSdk,
            omitRuntimeAssembly: omitRuntimeAssembly);

        try
        {
            using var installer = new PluginPackageInstaller(
                Path.Combine(root, "Plugins"),
                Path.Combine(root, "PluginData"));

            var exception = await Assert.ThrowsAsync<PluginPackageException>(
                () => installer.InstallAsync(packagePath));

            Assert.Equal(expectedErrorCode, exception.ErrorCode);
            AssertNoStagingChildren(Path.Combine(root, "Plugins"));
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static string CreateHappyPathPackage(
        string root,
        string version,
        int pluginApiMajor = 1,
        bool automaticRollbackSupported = true,
        bool tamperHash = false,
        int packageFormatVersion = 2,
        string? metadataPluginId = null,
        string? metadataPluginVersion = null,
        bool includePrivateSdk = false,
        bool omitRuntimeAssembly = false,
        bool omitSignature = false,
        bool tamperSignature = false,
        TestPackageSigner? signer = null)
    {
        var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HappyPathPlugin.manifest.json"))
            .Replace("\"version\": \"0.1.0\"", $"\"version\": \"{version}\"", StringComparison.Ordinal)
            .Replace("\"pluginApiMajor\": 1", $"\"pluginApiMajor\": {pluginApiMajor}", StringComparison.Ordinal);
        var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["manifest.json"] = Encoding.UTF8.GetBytes(manifest)
        };

        if (!omitRuntimeAssembly)
        {
            payload["runtime/HappyPathPlugin.dll"] = File.ReadAllBytes(
                typeof(HappyPathPlugin.HappyPathPlugin).Assembly.Location);
        }

        if (includePrivateSdk)
        {
            payload["runtime/ToolBox.PluginSdk.dll"] = File.ReadAllBytes(typeof(IPlugin).Assembly.Location);
        }

        var files = payload
            .Select(entry => new
            {
                path = entry.Key,
                sha256 = Convert.ToHexString(SHA256.HashData(entry.Value)).ToLowerInvariant()
            })
            .ToArray();

        if (tamperHash)
        {
            files[1] = new
            {
                path = files[1].path,
                sha256 = new string('0', 64)
            };
        }

        payload["package.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new
            {
                packageFormatVersion,
                pluginId = metadataPluginId ?? "com.toolbox.happy-path",
                pluginVersion = metadataPluginVersion ?? version,
                automaticRollbackSupported,
                files
            }));
        if (!omitSignature)
        {
            payload["signature.json"] = (signer ?? TestPackageSigner.Shared).CreateSignature(
                payload["package.json"],
                "toolbox.tests");
            if (tamperSignature)
            {
                payload["package.json"] = [.. payload["package.json"], (byte)' '];
            }
        }

        return CreateArchive(root, $"HappyPath-{version}-{Guid.NewGuid():N}.tpk", payload);
    }

    private static string CreateArchive(
        string root,
        string fileName,
        IReadOnlyDictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(root);
        var packagePath = Path.Combine(root, fileName);

        using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
        {
            foreach (var entry in entries)
            {
                var zipEntry = archive.CreateEntry(entry.Key, CompressionLevel.Optimal);
                using var stream = zipEntry.Open();
                stream.Write(entry.Value, 0, entry.Value.Length);
            }
        }

        return packagePath;
    }

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "ToolBoxPackageTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void AssertNoStagingChildren(string pluginsRoot)
    {
        var stagingRoot = Path.Combine(pluginsRoot, ".staging");
        if (Directory.Exists(stagingRoot))
        {
            Assert.Empty(Directory.EnumerateFileSystemEntries(stagingRoot));
        }
    }

    private static void DeleteTemporaryRoot(string root)
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(root); attempt++)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) when (Directory.Exists(root))
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Thread.Sleep(50);
            }
        }

        Assert.False(Directory.Exists(root), $"Package test directory could not be cleaned: '{root}'.");
    }
}
