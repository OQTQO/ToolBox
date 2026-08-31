using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

public sealed partial class MainWindowViewModel
{
    public void SetPluginSearchText(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (string.Equals(_pluginSearchText, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _pluginSearchText = normalized;
        RefreshVisiblePluginWorkspaces();
        OnPropertyChanged(nameof(PluginSearchText));
    }

    public void SetPluginFilter(string filter)
    {
        _pluginFilter = filter is "running" or "disabled" or "attention" ? filter : "all";
        RefreshVisiblePluginWorkspaces();
        OnPropertyChanged(nameof(PluginFilter));
    }

    public void SetPluginSort(string sort)
    {
        _pluginSort = sort is "status" or "version" ? sort : "name";
        RefreshVisiblePluginWorkspaces();
        OnPropertyChanged(nameof(PluginSort));
    }

    public async Task ToggleWorkspaceOpenedAsync(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EnsureWorkspace(workspace);

        ClearPluginManagerError();
        if (!await workspace.SetOpenedAsync(!workspace.IsOpened).ConfigureAwait(false))
        {
            CapturePluginError(workspace);
        }
    }

    public async Task ToggleWorkspaceRuntimeAsync(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EnsureWorkspace(workspace);

        ClearPluginManagerError();
        if (!await workspace.SetRuntimeEnabledAsync(!workspace.IsRuntimeEnabled).ConfigureAwait(false))
        {
            CapturePluginError(workspace);
        }
    }

    public async Task InstallWorkspacePackageAsync(
        PluginWorkspaceViewModel workspace,
        string packagePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        EnsureWorkspace(workspace);

        try
        {
            ClearPluginManagerError();
            var manifest = _packageInspector.ReadManifest(packagePath);
            EnsureOutOfProcessSupport(manifest);
            if (!string.Equals(manifest.Id, workspace.PluginId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(string.Format(
                    CultureInfo.CurrentCulture,
                    T("UnsupportedPluginPackage"),
                    manifest.Id));
            }

            await workspace.InstallPackageAsync(packagePath).ConfigureAwait(false);
            _settings.SetPluginOpened(manifest.Id, opened: false);
            RefreshPluginWorkspaces();
        }
        catch (Exception exception)
        {
            RecordPackageInstallFailure(exception);
        }
    }

    public async Task InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ClearPluginManagerError();

        try
        {
            var manifest = _packageInspector.ReadManifest(packagePath);
            EnsureOutOfProcessSupport(manifest);
            var existing = _pluginWorkspaces.SingleOrDefault(candidate => string.Equals(
                candidate.PluginId,
                manifest.Id,
                StringComparison.Ordinal));

            if (existing is not null)
            {
                await InstallWorkspacePackageAsync(existing, packagePath).ConfigureAwait(false);
                return;
            }

            var result = await _packageInstaller.InstallAsync(packagePath).ConfigureAwait(false);
            _settings.SetPluginOpened(result.PluginId, opened: false);
            _logger.Log(
                LogLevel.Information,
                "Package",
                $"Installed plugin package '{result.PluginId}' version '{result.Version}'.",
                pluginId: result.PluginId,
                pluginVersion: result.Version);
            RefreshPluginWorkspaces();
        }
        catch (Exception exception)
        {
            RecordPackageInstallFailure(exception);
        }
    }

    public async Task UninstallWorkspaceAsync(PluginWorkspaceViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        EnsureWorkspace(workspace);

        try
        {
            ClearPluginManagerError();
            var result = await workspace.UninstallAsync().ConfigureAwait(false);
            if (result.ActiveVersionAfterUninstall is null)
            {
                _settings.RemovePlugin(workspace.PluginId);
            }

            RefreshPluginWorkspaces();
        }
        catch (Exception exception)
        {
            SetPluginManagerError(exception.Message);
            _logger.Error(
                "Package",
                $"Plugin '{workspace.PluginId}' could not be uninstalled.",
                errorCode: exception is PluginPackageException packageException
                    ? packageException.ErrorCode
                    : "PACKAGE_UNINSTALL_FAILED",
                exception: exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _diagnostics.Changed -= OnDiagnosticsChanged;
        _logger.EventWritten -= OnEventWritten;
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;
        foreach (var workspace in _pluginWorkspaces.ToArray())
        {
            workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            workspace.Dispose();
        }

        _pluginWorkspaces.Clear();
        _installedPluginWorkspaces.Clear();
        _openedPluginWorkspaces.Clear();
        _visiblePluginWorkspaces.Clear();
    }

    private void RefreshPluginWorkspaces()
    {
        // Package installation/uninstallation completes on a thread-pool
        // continuation. ObservableCollection is bound to WPF, so all catalog
        // replacement must be marshalled back to the dispatcher thread.
        _uiDispatcher.Dispatch(RefreshPluginWorkspacesCore);
    }

    private void RefreshPluginWorkspacesCore()
    {
        if (_disposed)
        {
            return;
        }

        var selectedPluginId = _selectedPluginWorkspace?.PluginId;
        foreach (var workspace in _pluginWorkspaces.ToArray())
        {
            workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            workspace.Dispose();
        }

        _pluginWorkspaces.Clear();
        var snapshot = _pluginCatalog.Scan();
        foreach (var issue in snapshot.Issues)
        {
            _logger.Error(
                "Package",
                $"Plugin '{issue.PluginId}' was skipped during discovery: {issue.Message}",
                errorCode: issue.ErrorCode,
                exception: issue.Exception);
        }

        foreach (var descriptor in snapshot.Plugins)
        {
            var workspace = new PluginWorkspaceViewModel(
                descriptor,
                _packageInstaller,
                _runtime,
                _logger,
                _localization,
                _settings,
                _uiDispatcher);
            workspace.PropertyChanged += OnWorkspacePropertyChanged;
            _pluginWorkspaces.Add(workspace);
        }

        var selected = selectedPluginId is null
            ? null
            : _pluginWorkspaces.SingleOrDefault(workspace =>
                string.Equals(workspace.PluginId, selectedPluginId, StringComparison.Ordinal)
                && workspace.IsOpened);
        SetSelectedPluginWorkspace(selected);
        RefreshWorkspaceCollections();
        RefreshVisiblePluginWorkspaces();
    }

    private void RefreshWorkspaceCollections()
    {
        _uiDispatcher.Dispatch(RefreshWorkspaceCollectionsCore);
    }

    private void RefreshWorkspaceCollectionsCore()
    {
        ReplaceCollection(_installedPluginWorkspaces, _pluginWorkspaces.Where(workspace => workspace.IsInstalled));
        ReplaceCollection(_openedPluginWorkspaces, _pluginWorkspaces.Where(workspace => workspace.IsOpened));

        OnPropertyChanged(nameof(HasInstalledPlugins));
        OnPropertyChanged(nameof(HasNoInstalledPlugins));
        OnPropertyChanged(nameof(HasOpenedPlugins));
        OnPropertyChanged(nameof(InstalledPluginCount));
        OnPropertyChanged(nameof(OpenedPluginCount));
        OnPropertyChanged(nameof(InstalledPluginCountLabel));
        OnPropertyChanged(nameof(PluginCountSummaryLabel));
        OnPropertyChanged(nameof(RunningPluginCount));
        OnPropertyChanged(nameof(AttentionPluginCount));
        OnPropertyChanged(nameof(HasVisiblePlugins));
        OnPropertyChanged(nameof(HasNoVisiblePlugins));
        RefreshVisiblePluginWorkspacesCore();
    }

    private void RefreshVisiblePluginWorkspaces()
    {
        _uiDispatcher.Dispatch(RefreshVisiblePluginWorkspacesCore);
    }

    private void RefreshVisiblePluginWorkspacesCore()
    {
        if (_disposed)
        {
            return;
        }

        IEnumerable<PluginWorkspaceViewModel> items = InstalledPluginWorkspaces;
        items = _pluginFilter switch
        {
            "running" => items.Where(workspace => workspace.LifecycleState == PluginLifecycleState.Running),
            "disabled" => items.Where(workspace => workspace.LifecycleState == PluginLifecycleState.Disabled),
            "attention" => items.Where(workspace => workspace.LifecycleState is PluginLifecycleState.Faulted or PluginLifecycleState.DisableFailed or PluginLifecycleState.RestartRequired or PluginLifecycleState.Quarantined),
            _ => items
        };

        if (!string.IsNullOrWhiteSpace(_pluginSearchText))
        {
            items = items.Where(workspace => workspace.DisplayName.Contains(_pluginSearchText, StringComparison.CurrentCultureIgnoreCase)
                || workspace.Publisher.Contains(_pluginSearchText, StringComparison.CurrentCultureIgnoreCase)
                || workspace.PluginId.Contains(_pluginSearchText, StringComparison.OrdinalIgnoreCase));
        }

        items = _pluginSort switch
        {
            "status" => items.OrderBy(workspace => workspace.LifecycleState).ThenBy(workspace => workspace.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            "version" => items.OrderByDescending(workspace => workspace.InstalledVersion, StringComparer.Ordinal).ThenBy(workspace => workspace.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => items.OrderBy(workspace => workspace.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };

        ReplaceCollection(_visiblePluginWorkspaces, items);
        OnPropertyChanged(nameof(HasVisiblePlugins));
        OnPropertyChanged(nameof(HasNoVisiblePlugins));
    }

    private void CapturePluginError(PluginWorkspaceViewModel workspace)
    {
        if (workspace.HasError)
        {
            SetPluginManagerError(workspace.ErrorMessage);
        }
    }

    private void EnsureWorkspace(PluginWorkspaceViewModel workspace)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_pluginWorkspaces.Contains(workspace))
        {
            throw new ArgumentOutOfRangeException(nameof(workspace));
        }
    }

    private static void ReplaceCollection(
        ObservableCollection<PluginWorkspaceViewModel> target,
        IEnumerable<PluginWorkspaceViewModel> source)
    {
        var items = source.ToArray();
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    private void ClearPluginManagerError()
    {
        SetPluginManagerError(null);
    }

    private void SetPluginManagerError(string? message)
    {
        _uiDispatcher.Dispatch(() =>
        {
            if (_disposed)
            {
                return;
            }

            _pluginManagerError = message;
            OnPropertyChanged(nameof(HasPluginManagerError));
            OnPropertyChanged(nameof(PluginManagerError));
        });
    }

    private void RecordPackageInstallFailure(Exception exception)
    {
        SetPluginManagerError(exception.Message);
        _logger.Error(
            "Package",
            "The selected plugin package could not be installed.",
            errorCode: exception is PluginPackageException packageException
                ? packageException.ErrorCode
                : "PACKAGE_INSTALL_FAILED",
            exception: exception);
    }

    private static void EnsureOutOfProcessSupport(PluginManifest manifest)
    {
        if (manifest.Runtime is null
            || !manifest.Runtime.SupportedModes.Contains(PluginExecutionMode.OutOfProcess))
        {
            throw new PluginLoadException(
                "PLUGIN_RUNTIME_MODE_UNSUPPORTED",
                $"Plugin '{manifest.Id}' does not support the required 'outOfProcess' execution mode.");
        }
    }
}
