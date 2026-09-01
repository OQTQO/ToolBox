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
        var entered = false;
        try
        {
            await _lifecycleOperationGate.WaitAsync().ConfigureAwait(false);
            entered = true;
            if (_disposed)
            {
                return false;
            }

            if (!opened && IsRuntimeEnabled && !await DisableAsync().ConfigureAwait(false))
            {
                return false;
            }

            _settings.SetPluginOpened(PluginId, opened);
            RefreshState();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            if (entered)
            {
                ReleaseSemaphore(_lifecycleOperationGate);
            }
        }
    }

    internal async Task<bool> SetRuntimeEnabledAsync(bool enabled)
    {
        var entered = false;
        try
        {
            await _lifecycleOperationGate.WaitAsync().ConfigureAwait(false);
            entered = true;
            if (_disposed)
            {
                return false;
            }

            return enabled ? await EnableAsync().ConfigureAwait(false) : await DisableAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        finally
        {
            if (entered)
            {
                ReleaseSemaphore(_lifecycleOperationGate);
            }
        }
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

        _lifecycleOperationGate.Dispose();
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
            var session = await _runtime.StartAsync(VersionDirectory).ConfigureAwait(false);
            _session = session;
            await session.StartPluginAsync().ConfigureAwait(false);
            await RefreshUiAsync().ConfigureAwait(false);
            _state = session.State;
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
            var session = _session;
            if (session is not null)
            {
                _state = session.State;
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
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
            var session = _session;
            if (session is null)
            {
                return !IsRuntimeEnabled;
            }

            try
            {
                await session.StopAsync().ConfigureAwait(false);
                _state = session.State;
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
                _state = session.State;
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
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    SetError(exception);
                    _logger.Error(
                        "Plugin",
                        $"Plugin '{PluginId}' cleanup after disable failed.",
                        errorCode: "PLUGIN_DISABLE_CLEANUP_FAILED",
                        exception: exception);
                }

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
        if (Interlocked.CompareExchange(ref _operationInProgress, 1, 0) != 0)
        {
            return false;
        }

        RefreshState();
        return true;
    }

    private void EndOperationIfStarted()
    {
        Interlocked.Exchange(ref _operationInProgress, 0);
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

        try
        {
            if (!await _uiOperationGate.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        var session = _session;
        if (session is null || !IsRuntimeEnabled)
        {
            ReleaseSemaphore(_uiOperationGate);
            return;
        }

        Interlocked.Exchange(ref _uiOperationInProgress, 1);
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
            Interlocked.Exchange(ref _uiOperationInProgress, 0);
            RefreshUiState();
            ReleaseSemaphore(_uiOperationGate);
        }
    }

    private async Task HandleUiInputCoreAsync(PluginInputEvent input)
    {
        if (!IsPluginInputEnabled || InputSurface is null)
        {
            return;
        }

        try
        {
            await _uiOperationGate.WaitAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        var session = _session;
        if (session is null || !IsRuntimeEnabled)
        {
            ReleaseSemaphore(_uiOperationGate);
            return;
        }

        Interlocked.Exchange(ref _uiOperationInProgress, 1);
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
            Interlocked.Exchange(ref _uiOperationInProgress, 0);
            RefreshUiState();
            ReleaseSemaphore(_uiOperationGate);
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

    private static void ReleaseSemaphore(SemaphoreSlim semaphore)
    {
        try
        {
            semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown can dispose the workspace while an input task is unwinding.
        }
        catch (SemaphoreFullException)
        {
            // A cancelled/retried UI task must not surface a second dispatcher error.
        }
    }
}
