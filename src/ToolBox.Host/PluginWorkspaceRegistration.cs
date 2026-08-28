using System.ComponentModel;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace ToolBox.Host;

internal sealed class PluginWorkspaceRegistration
{
    public required string PluginId { get; init; }

    public required string DisplayNameResourceKey { get; init; }

    public required string InstallDialogTitleResourceKey { get; init; }

    public required Geometry IconGeometry { get; init; }

    public required object PageViewModel { get; init; }

    public required INotifyPropertyChanged StateSource { get; init; }

    public required Func<bool> GetIsInstalled { get; init; }

    public required Func<bool> GetIsRuntimeEnabled { get; init; }

    public required Func<string> GetInstalledVersion { get; init; }

    public required Func<bool> GetIsInstallEnabled { get; init; }

    public required Func<bool> GetIsUninstallEnabled { get; init; }

    public required Func<Brush> GetStatusAccentBrush { get; init; }

    public required Func<bool> GetHasError { get; init; }

    public required Func<string> GetErrorMessage { get; init; }

    public required Func<bool> GetRequiresHostRestart { get; init; }

    public required Func<bool, Task<bool>> SetRuntimeEnabledAsync { get; init; }

    public required Func<string, Task> InstallPackageAsync { get; init; }

    public required Func<Task> UninstallAsync { get; init; }

    public required Action Dispose { get; init; }
}
