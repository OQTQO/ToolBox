using System.IO;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;

namespace ToolBox.Host;

internal static class HostPackageSmokeRunner
{
    public static async Task<HostSmokeResult> RunAsync(HostSmokeCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var workerPath = Path.GetFullPath(command.WorkerPath);
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException("The ToolBox PluginWorker executable was not found.", workerPath);
        }

        var workingRoot = Path.GetFullPath(command.WorkingRoot);
        var pluginsRoot = Path.Combine(workingRoot, "Plugins");
        var dataRoot = Path.Combine(workingRoot, "PluginData");
        var logsRoot = Path.Combine(workingRoot, "Logs");
        var settingsPath = Path.Combine(workingRoot, "host-settings.json");
        Directory.CreateDirectory(workingRoot);

        var settings = new HostSettingsService(settingsPath);
        var localization = new LocalizationService(settings);
        await using var logger = new StructuredLogger(
            new LoggerOptions { DirectoryPath = logsRoot },
            "smoke-" + Guid.NewGuid().ToString("N"),
            HostSmokeCommandLine.GetHostVersion());
        using var installer = new PluginPackageInstaller(pluginsRoot, dataRoot);
        using var viewModel = new MainWindowViewModel(
            new HostDiagnostics(
                "smoke-" + Guid.NewGuid().ToString("N"),
                "smoke-" + Guid.NewGuid().ToString("N"),
                HostSmokeCommandLine.GetHostVersion()),
            logger,
            new InstalledPluginCatalog(installer),
            installer,
            new OutOfProcessPluginRuntime(workerPath),
            localization,
            settings,
            dataRoot: workingRoot);

        var results = new List<HostSmokePackageResult>();
        foreach (var packagePath in command.PackagePaths)
        {
            results.Add(await RunPackageAsync(
                    Path.GetFullPath(packagePath),
                    viewModel,
                    installer)
                .ConfigureAwait(false));
        }

        return new HostSmokeResult(
            results.All(result => result.Error is null
                && result.Installed
                && result.Enabled
                && result.Disabled
                && result.Uninstalled),
            HostSmokeCommandLine.GetHostVersion(),
            workerPath,
            results,
            null);
    }

    private static async Task<HostSmokePackageResult> RunPackageAsync(
        string packagePath,
        MainWindowViewModel viewModel,
        PluginPackageInstaller installer)
    {
        var pluginId = string.Empty;
        var version = string.Empty;
        var installed = false;
        var enabled = false;
        var disabled = false;
        var uninstalled = false;

        try
        {
            var manifest = new PluginPackageInspector().ReadManifest(packagePath);
            pluginId = manifest.Id;
            version = manifest.Version;

            await viewModel.InstallPackageAsync(packagePath).ConfigureAwait(false);
            EnsureNoManagerError(viewModel, "install");
            var workspace = viewModel.PluginWorkspaces.Single(candidate =>
                string.Equals(candidate.PluginId, pluginId, StringComparison.Ordinal));
            EnsureState(
                workspace.InstalledVersion == version
                    && workspace.LifecycleState == PluginLifecycleState.Disabled
                    && installer.GetActiveVersionDirectory(pluginId) is not null,
                $"Host did not commit plugin '{pluginId}' version '{version}' in the disabled state.");
            installed = true;

            await viewModel.ToggleWorkspaceRuntimeAsync(workspace).ConfigureAwait(false);
            EnsureNoManagerError(viewModel, "enable");
            EnsureState(
                workspace.IsRuntimeEnabled
                    && workspace.LifecycleState == PluginLifecycleState.Running
                    && workspace.HasPluginUi,
                $"Host did not start plugin '{pluginId}' through PluginWorker with a UI snapshot.");
            enabled = true;

            await viewModel.ToggleWorkspaceRuntimeAsync(workspace).ConfigureAwait(false);
            EnsureNoManagerError(viewModel, "disable");
            EnsureState(
                !workspace.IsRuntimeEnabled
                    && workspace.LifecycleState == PluginLifecycleState.Disabled,
                $"Host did not stop plugin '{pluginId}' cleanly.");
            disabled = true;

            await viewModel.UninstallWorkspaceAsync(workspace).ConfigureAwait(false);
            EnsureNoManagerError(viewModel, "uninstall");
            EnsureState(
                viewModel.PluginWorkspaces.All(candidate => !string.Equals(
                    candidate.PluginId,
                    pluginId,
                    StringComparison.Ordinal))
                    && installer.GetActiveVersionDirectory(pluginId) is null
                    && installer.GetInstalledVersions(pluginId).Count == 0,
                $"Host did not remove plugin '{pluginId}' after the smoke test.");
            uninstalled = true;

            return new HostSmokePackageResult(
                packagePath,
                pluginId,
                version,
                installed,
                enabled,
                disabled,
                uninstalled,
                null);
        }
        catch (Exception exception)
        {
            return new HostSmokePackageResult(
                packagePath,
                pluginId,
                version,
                installed,
                enabled,
                disabled,
                uninstalled,
                exception.Message);
        }
    }

    private static void EnsureNoManagerError(MainWindowViewModel viewModel, string operation)
    {
        if (viewModel.HasPluginManagerError)
        {
            throw new InvalidOperationException(
                $"Host {operation} failed: {viewModel.PluginManagerError}");
        }
    }

    private static void EnsureState(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
