using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace ToolBox.Host;

public sealed class PluginWorkspaceViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PluginWorkspaceRegistration _registration;
    private readonly LocalizationService _localization;
    private readonly HostSettingsService _settings;
    private readonly IHostUiDispatcher _uiDispatcher;
    private bool _isSelected;
    private bool _disposed;

    internal PluginWorkspaceViewModel(
        PluginWorkspaceRegistration registration,
        LocalizationService localization,
        HostSettingsService settings,
        IHostUiDispatcher uiDispatcher)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        _localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

        _registration.StateSource.PropertyChanged += OnStateSourcePropertyChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PluginId => _registration.PluginId;

    public string DisplayName => _localization[_registration.DisplayNameResourceKey];

    public string InstallDialogTitle => _localization[_registration.InstallDialogTitleResourceKey];

    public Geometry IconGeometry => _registration.IconGeometry;

    public object PageViewModel => _registration.PageViewModel;

    public bool IsInstalled => _registration.GetIsInstalled();

    public bool IsOpened => IsInstalled && _settings.IsPluginOpened(PluginId);

    public bool IsRuntimeEnabled => _registration.GetIsRuntimeEnabled();

    public string InstalledVersion => _registration.GetInstalledVersion();

    public bool IsInstallEnabled => _registration.GetIsInstallEnabled();

    public bool IsUninstallEnabled => _registration.GetIsUninstallEnabled();

    public Brush StatusAccentBrush => _registration.GetStatusAccentBrush();

    public bool HasError => _registration.GetHasError();

    public string ErrorMessage => _registration.GetErrorMessage();

    public bool RequiresHostRestart => _registration.GetRequiresHostRestart();

    public string OpenedStateLabel => IsOpened
        ? _localization["StatusOpened"]
        : _localization["StatusClosed"];

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    internal async Task<bool> SetOpenedAsync(bool opened)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsInstalled)
        {
            return false;
        }

        if (!opened && !await _registration.SetRuntimeEnabledAsync(false))
        {
            RefreshState();
            return false;
        }

        _settings.SetPluginOpened(PluginId, opened);
        RefreshState();
        return true;
    }

    internal async Task InstallPackageAsync(string packagePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _registration.InstallPackageAsync(packagePath);
        if (IsInstalled && !HasError)
        {
            _settings.SetPluginOpened(PluginId, opened: true);
        }

        RefreshState();
    }

    internal async Task UninstallAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _registration.UninstallAsync();
        if (!IsInstalled)
        {
            _settings.RemovePlugin(PluginId);
        }

        RefreshState();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registration.StateSource.PropertyChanged -= OnStateSourcePropertyChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _settings.Changed -= OnSettingsChanged;
        _registration.Dispose();
    }

    private void OnStateSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshState();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(InstallDialogTitle));
            OnPropertyChanged(nameof(OpenedStateLabel));
        });
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(IsOpened));
            OnPropertyChanged(nameof(OpenedStateLabel));
        });
    }

    private void RefreshState()
    {
        _uiDispatcher.Dispatch(() =>
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(IsOpened));
            OnPropertyChanged(nameof(IsRuntimeEnabled));
            OnPropertyChanged(nameof(InstalledVersion));
            OnPropertyChanged(nameof(IsInstallEnabled));
            OnPropertyChanged(nameof(IsUninstallEnabled));
            OnPropertyChanged(nameof(StatusAccentBrush));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
            OnPropertyChanged(nameof(RequiresHostRestart));
            OnPropertyChanged(nameof(OpenedStateLabel));
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
