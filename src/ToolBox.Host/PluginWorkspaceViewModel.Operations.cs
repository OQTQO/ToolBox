using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ToolBox.Core.Diagnostics;
using ToolBox.Core.Packaging;
using ToolBox.Core.Plugins;
using ToolBox.Core.Plugins.Worker;
using ToolBox.PluginSdk;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

public sealed partial class PluginWorkspaceViewModel
{
    internal async Task<bool> SetOpenedAsync(bool opened)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!opened && IsRuntimeEnabled && !await SetRuntimeEnabledAsync(false).ConfigureAwait(false))
        {
            return false;
        }

        _settings.SetPluginOpened(PluginId, opened);
        RefreshState();
        return true;
    }

    internal Task<bool> SetRuntimeEnabledAsync(bool enabled)
    {
        return enabled ? EnableAsync() : DisableAsync();
    }

    internal async Task<PluginPackageInstallResult> InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInstallEnabled)
        {
            throw new InvalidOperationException(_localization["DisablePluginBeforeUpdate"]);
        }

        var result = await _packageInstaller.InstallAsync(packagePath).ConfigureAwait(false);
        _logger.Log(
            LogLevel.Information,
            "Package",
            $"Installed plugin package '{result.PluginId}' version '{result.Version}'.",
            pluginId: result.PluginId,
            pluginVersion: result.Version);
        return result;
    }

    internal async Task<PluginPackageUninstallResult> UninstallAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsUninstallEnabled)
        {
            throw new InvalidOperationException(_localization["DisablePluginBeforeUninstall"]);
        }

        var result = await _packageInstaller.UninstallAsync(PluginId, InstalledVersion).ConfigureAwait(false);
        _logger.Log(
            LogLevel.Information,
            "Package",
            $"Uninstalled plugin package '{PluginId}' version '{InstalledVersion}'.",
            pluginId: PluginId,
            pluginVersion: InstalledVersion);
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;

        try
        {
            _session?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            _logger.Error(
                "Plugin",
                $"Plugin '{PluginId}' cleanup failed.",
                errorCode: "PLUGIN_DISPOSE_FAILED",
                exception: exception);
        }
        finally
        {
            _session = null;
        }

        _uiOperationGate.Dispose();
    }

    private async Task<bool> EnableAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!SupportsOutOfProcess)
        {
            SetError(new PluginLoadException(
                "PLUGIN_RUNTIME_MODE_UNSUPPORTED",
                $"Plugin '{PluginId}' does not support 'outOfProcess' execution."));
            return false;
        }

        if (!BeginOperation())
        {
            return IsRuntimeEnabled;
        }

        if (LifecycleState != PluginLifecycleState.Disabled)
        {
            EndOperationIfStarted();
            return IsRuntimeEnabled;
        }

        try
        {
            ClearError();
            _state = _state.TransitionTo(PluginLifecycleState.Starting);
            RefreshState();
            _session = await _runtime.StartAsync(VersionDirectory).ConfigureAwait(false);
            await _session.StartPluginAsync().ConfigureAwait(false);
            await RefreshUiAsync().ConfigureAwait(false);
            _state = _session.State;
            _logger.Log(
                LogLevel.Information,
                "Plugin",
                $"Plugin '{PluginId}' enabled.",
                pluginId: PluginId,
                pluginVersion: InstalledVersion);
            return true;
        }
        catch (Exception exception)
        {
            if (_session is not null)
            {
                _state = _session.State;
                try
                {
                    await _session.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // The session already owns the termination deadline.
                }

                _session = null;
            }

            ApplyUiSnapshot(null);

            if (_state.LifecycleState == PluginLifecycleState.Starting)
            {
                _state = _state.TransitionTo(
                    PluginLifecycleState.Faulted,
                    errorCode: GetErrorCode(exception, "PLUGIN_START_FAILED"),
                    errorMessage: exception.Message);
            }

            SetError(exception);
            _logger.Error(
                "Plugin",
                $"Plugin '{PluginId}' could not be enabled.",
                errorCode: GetErrorCode(exception, "PLUGIN_START_FAILED"),
                exception: exception);
            return false;
        }
        finally
        {
            EndOperationIfStarted();
            RefreshState();
        }
    }

    private async Task<bool> DisableAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!BeginOperation())
        {
            return !IsRuntimeEnabled;
        }

        try
        {
            if (_session is null)
            {
                return !IsRuntimeEnabled;
            }

            try
            {
                await _session.StopAsync().ConfigureAwait(false);
                _state = _session.State;
                _logger.Log(
                    LogLevel.Information,
                    "Plugin",
                    $"Plugin '{PluginId}' disabled.",
                    pluginId: PluginId,
                    pluginVersion: InstalledVersion);
                return _state.LifecycleState == PluginLifecycleState.Disabled;
            }
            catch (Exception exception)
            {
                _state = _session.State;
                SetError(exception);
                _logger.Error(
                    "Plugin",
                    $"Plugin '{PluginId}' could not be disabled.",
                    errorCode: GetErrorCode(exception, "PLUGIN_STOP_FAILED"),
                    exception: exception);
                return false;
            }
            finally
            {
                await _session.DisposeAsync().ConfigureAwait(false);
                _session = null;
                ApplyUiSnapshot(null);
            }
        }
        finally
        {
            EndOperationIfStarted();
            RefreshState();
        }
    }

    private bool BeginOperation()
    {
        if (_operationInProgress)
        {
            return false;
        }

        _operationInProgress = true;
        RefreshState();
        return true;
    }

    private void EndOperationIfStarted()
    {
        _operationInProgress = false;
        RefreshState();
    }

    internal Task ExecuteUiActionAsync(PluginUiActionViewModel action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteUiActionCoreAsync(action);
    }

    internal Task HandleUiInputAsync(PluginInputEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return HandleUiInputCoreAsync(input);
    }

    private async Task RefreshUiAsync()
    {
        var session = _session;
        if (session is null)
        {
            ApplyUiSnapshot(null);
            return;
        }

        try
        {
            ClearUiError();
            ApplyUiSnapshot(await session.GetUiSnapshotAsync().ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            _state = session.State;
            RefreshState();
            SetUiError(exception);
        }
    }

    private async Task ExecuteUiActionCoreAsync(PluginUiActionViewModel action)
    {
        if (!IsPluginUiActionEnabled || !action.Descriptor.IsEnabled)
        {
            return;
        }

        await _uiOperationGate.WaitAsync().ConfigureAwait(false);
        var session = _session;
        if (session is null || !IsRuntimeEnabled)
        {
            _uiOperationGate.Release();
            return;
        }

        _uiOperationInProgress = true;
        RefreshUiState();

        try
        {
            ClearUiError();
            var snapshot = await session.ExecuteUiActionAsync(
                    action.Descriptor.Id,
                    action.Descriptor.Argument)
                .ConfigureAwait(false);
            ApplyUiSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            _state = session.State;
            RefreshState();
            SetUiError(exception);
        }
        finally
        {
            _uiOperationInProgress = false;
            RefreshUiState();
            _uiOperationGate.Release();
        }
    }

    private async Task HandleUiInputCoreAsync(PluginInputEvent input)
    {
        if (!IsPluginInputEnabled || InputSurface is null)
        {
            return;
        }

        await _uiOperationGate.WaitAsync().ConfigureAwait(false);
        var session = _session;
        if (session is null || !IsRuntimeEnabled)
        {
            _uiOperationGate.Release();
            return;
        }

        _uiOperationInProgress = true;
        RefreshUiState();

        try
        {
            ClearUiError();
            ApplyUiSnapshot(await session.SendUiInputAsync(input).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            _state = session.State;
            RefreshState();
            SetUiError(exception);
        }
        finally
        {
            _uiOperationInProgress = false;
            RefreshUiState();
            _uiOperationGate.Release();
        }
    }

    private void SetError(Exception exception)
    {
        _errorMessage = exception.Message;
        RefreshState();
    }

    private void ClearError()
    {
        _errorMessage = null;
        RefreshState();
    }

    private void SetUiError(Exception exception)
    {
        _uiErrorMessage = exception.Message;
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(HasPluginUiError));
            OnPropertyChanged(nameof(PluginUiErrorMessage));
            RefreshUiStateCore();
        });
    }

    private void ClearUiError()
    {
        _uiErrorMessage = null;
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(HasPluginUiError));
            OnPropertyChanged(nameof(PluginUiErrorMessage));
            RefreshUiStateCore();
        });
    }
}
