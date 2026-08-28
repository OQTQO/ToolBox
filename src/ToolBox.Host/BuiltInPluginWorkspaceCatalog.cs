using System.Windows.Media;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;

namespace ToolBox.Host;

internal static class BuiltInPluginWorkspaceCatalog
{
    private const string KeyboardPluginId = "com.toolbox.keyboard-test";
    private const string AudioRelayPluginId = "com.toolbox.audio-relay";

    public static IReadOnlyList<PluginWorkspaceRegistration> CreateFromInstalledPackages(
        IStructuredLogger logger,
        PluginPackageInstaller packageInstaller,
        LocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(packageInstaller);
        ArgumentNullException.ThrowIfNull(localization);

        return Create(
            logger,
            packageInstaller,
            localization,
            ResolveActiveDirectory(packageInstaller, logger, KeyboardPluginId, "Keyboard & Mouse Test"),
            ResolveActiveDirectory(packageInstaller, logger, AudioRelayPluginId, "Phone Audio Relay"));
    }

    internal static IReadOnlyList<PluginWorkspaceRegistration> Create(
        IStructuredLogger logger,
        PluginPackageInstaller packageInstaller,
        LocalizationService localization,
        string? keyboardPluginDirectory,
        string? audioRelayPluginDirectory)
    {
        var keyboard = new KeyboardTestViewModel(
            logger,
            keyboardPluginDirectory,
            packageInstaller,
            localization);
        var audio = new AudioRelayViewModel(
            logger,
            audioRelayPluginDirectory,
            packageInstaller,
            localization);

        return
        [
            new PluginWorkspaceRegistration
            {
                PluginId = KeyboardPluginId,
                DisplayNameResourceKey = "KeyboardMouse",
                InstallDialogTitleResourceKey = "InstallKeyboardDialogTitle",
                IconGeometry = Geometry.Parse("M 1,3 L 19,3 L 19,15 L 1,15 Z M 5,7 L 5.1,7 M 8,7 L 8.1,7 M 11,7 L 11.1,7 M 14,7 L 14.1,7 M 6,11 L 14,11"),
                PageViewModel = keyboard,
                StateSource = keyboard,
                GetIsInstalled = () => keyboard.IsInstalled,
                GetIsRuntimeEnabled = () => keyboard.IsRuntimeEnabled,
                GetInstalledVersion = () => keyboard.InstalledVersion,
                GetIsInstallEnabled = () => keyboard.IsInstallEnabled,
                GetIsUninstallEnabled = () => keyboard.IsUninstallEnabled,
                GetStatusAccentBrush = () => keyboard.StatusAccentBrush,
                GetHasError = () => keyboard.HasError,
                GetErrorMessage = () => keyboard.ErrorMessage,
                GetRequiresHostRestart = static () => false,
                SetRuntimeEnabledAsync = keyboard.SetRuntimeEnabledAsync,
                InstallPackageAsync = keyboard.InstallPackageAsync,
                UninstallAsync = keyboard.UninstallAsync,
                Dispose = keyboard.Dispose
            },
            new PluginWorkspaceRegistration
            {
                PluginId = AudioRelayPluginId,
                DisplayNameResourceKey = "PhoneAudioRelay",
                InstallDialogTitleResourceKey = "InstallAudioDialogTitle",
                IconGeometry = Geometry.Parse("M 6,15 L 6,5 L 15,3 L 15,13 M 6,15 C 6,17 2,17 2,15 C 2,13 6,13 6,15 M 15,13 C 15,15 11,15 11,13 C 11,11 15,11 15,13"),
                PageViewModel = audio,
                StateSource = audio,
                GetIsInstalled = () => audio.IsInstalled,
                GetIsRuntimeEnabled = () => audio.IsRuntimeEnabled,
                GetInstalledVersion = () => audio.InstalledVersion,
                GetIsInstallEnabled = () => audio.IsInstallEnabled,
                GetIsUninstallEnabled = () => audio.IsUninstallEnabled,
                GetStatusAccentBrush = () => audio.StatusAccentBrush,
                GetHasError = () => audio.HasError,
                GetErrorMessage = () => audio.ErrorMessage,
                GetRequiresHostRestart = () => audio.RequiresHostRestart,
                SetRuntimeEnabledAsync = audio.SetRuntimeEnabledAsync,
                InstallPackageAsync = audio.InstallPackageAsync,
                UninstallAsync = audio.UninstallAsync,
                Dispose = audio.Dispose
            }
        ];
    }

    private static string? ResolveActiveDirectory(
        PluginPackageInstaller packageInstaller,
        IStructuredLogger logger,
        string pluginId,
        string productName)
    {
        try
        {
            return packageInstaller.GetActiveVersionDirectory(pluginId);
        }
        catch (PluginPackageException exception)
        {
            logger.Error(
                "Package",
                $"The active {productName} package could not be resolved.",
                errorCode: exception.ErrorCode,
                exception: exception);
            return null;
        }
    }
}
