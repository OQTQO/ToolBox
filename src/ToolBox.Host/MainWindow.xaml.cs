using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolBox.PluginSdk;
using Application = System.Windows.Application;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ToolBox.Host;

public partial class MainWindow : Window
{
    private readonly IHostApplicationCommands _applicationCommands;
    private readonly SmoothScrollController _pageScrollController;
    private readonly SmoothScrollController _pluginDetailsScrollController;
    private readonly DispatcherTimer _settingsCommitTimer;
    private readonly HashSet<string> _activeUiOperations = new(StringComparer.Ordinal);
    private bool _allowClose;
    private bool _viewModelSubscribed;
    private bool _isResettingOverviewCopy;
    private bool _isCommittingSettings;
    private bool _hasFittedToWorkArea;
    private bool _hasPendingOverviewTitle;
    private bool _hasPendingOverviewHealthTitle;
    private bool _hasPendingTitleBarCenterText;
    private bool _hasPendingBackgroundBrightness;
    private bool _hasPendingCornerRadius;
    private IInputElement? _pluginDetailsReturnFocus;
    private string? _pendingOverviewTitle;
    private string? _pendingOverviewHealthTitle;
    private string? _pendingTitleBarCenterText;
    private int _pendingBackgroundBrightness;
    private int _pendingCornerRadius;

    public MainWindow(MainWindowViewModel viewModel)
        : this(
            viewModel,
            Application.Current as IHostApplicationCommands
                ?? throw new InvalidOperationException("The ToolBox application lifetime is unavailable."))
    {
    }

    internal MainWindow(
        MainWindowViewModel viewModel,
        IHostApplicationCommands applicationCommands)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _applicationCommands = applicationCommands
            ?? throw new ArgumentNullException(nameof(applicationCommands));

        InitializeComponent();
        DataContext = viewModel;
        _pageScrollController = new SmoothScrollController(PageScrollViewer, viewModel.ReduceMotion);
        _pluginDetailsScrollController = new SmoothScrollController(PluginDetailsScroll, viewModel.ReduceMotion);
        _settingsCommitTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(225)
        };
        _settingsCommitTimer.Tick += OnSettingsCommitTimerTick;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModelSubscribed = true;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (!_hasFittedToWorkArea)
        {
            FitWindowToWorkArea();
            _hasFittedToWorkArea = true;
        }

        _pageScrollController.Attach();
        _pluginDetailsScrollController.Attach();
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (!_viewModelSubscribed)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                _viewModelSubscribed = true;
            }

            _pageScrollController.SetReduceMotion(viewModel.ReduceMotion);
            _pluginDetailsScrollController.SetReduceMotion(viewModel.ReduceMotion);
        }

        UpdatePluginSearchPlaceholder();
    }

    private void OnWindowUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && _viewModelSubscribed)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModelSubscribed = false;
        }

        _pageScrollController.Detach();
        _pluginDetailsScrollController.Detach();
    }

    private void FitWindowToWorkArea()
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        const double workAreaInset = 24;
        var workArea = SystemParameters.WorkArea;
        var targetWidth = Math.Min(
            Width,
            Math.Max(MinWidth, workArea.Width - workAreaInset));
        var targetHeight = Math.Min(
            Height,
            Math.Max(MinHeight, workArea.Height - workAreaInset));

        if (Math.Abs(Width - targetWidth) > 0.5)
        {
            Width = targetWidth;
        }

        if (Math.Abs(Height - targetHeight) > 0.5)
        {
            Height = targetHeight;
        }

        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2d);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2d);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ReduceMotion)
            && DataContext is MainWindowViewModel viewModel)
        {
            _pageScrollController.SetReduceMotion(viewModel.ReduceMotion);
            _pluginDetailsScrollController.SetReduceMotion(viewModel.ReduceMotion);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        CommitPendingSettings();
        if (!_allowClose)
        {
            e.Cancel = true;
            if (DataContext is MainWindowViewModel viewModel && viewModel.CloseToTray)
            {
                _applicationCommands.HideMainWindowToTray();
            }
            else
            {
                _applicationCommands.RequestShutdown();
            }
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        CommitPendingSettings();
        _settingsCommitTimer.Stop();
        _settingsCommitTimer.Tick -= OnSettingsCommitTimerTick;
        if (DataContext is MainWindowViewModel viewModel && _viewModelSubscribed)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModelSubscribed = false;
        }

        _pageScrollController.Detach();
        _pluginDetailsScrollController.Detach();
        base.OnClosed(e);
    }

    internal void PrepareForShutdown()
    {
        _allowClose = true;
    }

    private void OnOverviewNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(ShellPage.Overview);
    }

    private void OnPluginsNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(ShellPage.Plugin);
    }

    private void OnActivityNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(ShellPage.Activity);
    }

    private void OnSettingsNavigationClick(object sender, RoutedEventArgs e)
    {
        NavigateTo(ShellPage.Settings);
    }

    private void NavigateTo(ShellPage page)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (page != ShellPage.Plugin)
        {
            HidePluginDetails();
        }

        viewModel.SelectPage(page);
        _pageScrollController.Reset();
        AnimatePageTransition();
    }

    private void OnWindowPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel viewModel && viewModel.HasSelectedPlugin)
        {
            viewModel.ClearSelectedPlugin();
            HidePluginDetails();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.K
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && DataContext is MainWindowViewModel pluginViewModel)
        {
            pluginViewModel.SelectPage(ShellPage.Plugin);
            HidePluginDetails();
            _pageScrollController.Reset();
            PluginSearchBox.Focus();
            PluginSearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void AnimatePageTransition()
    {
        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PageHost.BeginAnimation(UIElement.OpacityProperty, null);
            PageHost.Opacity = 1;
            return;
        }

        PageHost.Opacity = 0.35;
        PageHost.BeginAnimation(
            UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.35,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
    }

    private async void OnInstallPluginClick(object sender, RoutedEventArgs e)
    {
        const string operationKey = "install-package";
        if (!TryBeginUiOperation(operationKey))
        {
            return;
        }

        try
        {
            await RunUiOperationAsync("install plugin", async () =>
            {
                if (DataContext is not MainWindowViewModel viewModel)
                {
                    return;
                }

                var dialog = new OpenFileDialog
                {
                    Title = viewModel.Localize("InstallPluginDialogTitle"),
                    Filter = viewModel.Localize("PackageDialogFilter"),
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog(this) == true)
                {
                    await viewModel.InstallPackageAsync(dialog.FileName);
                }
            });
        }
        finally
        {
            EndUiOperation(operationKey);
        }
    }

    private async void OnWorkspaceRuntimeToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not FrameworkElement element
            || (element.DataContext as PluginWorkspaceViewModel ?? element.Tag as PluginWorkspaceViewModel) is not PluginWorkspaceViewModel workspace)
        {
            return;
        }

        var operationKey = $"runtime:{workspace.PluginId}";
        if (!TryBeginUiOperation(operationKey))
        {
            return;
        }

        try
        {
            await RunUiOperationAsync("change plugin runtime", async () =>
            {
                if (!workspace.IsRuntimeEnabled
                    && viewModel.ConfirmEnable
                    && MessageBox.Show(
                        this,
                        workspace.DisplayName,
                        viewModel.Localize("EnablePlugin"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.Yes) != MessageBoxResult.Yes)
                {
                    return;
                }

                await viewModel.ToggleWorkspaceRuntimeAsync(workspace);
            });
        }
        finally
        {
            EndUiOperation(operationKey);
        }
    }

    private async void OnPluginUiActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginUiActionViewModel action })
        {
            return;
        }

        var operationKey = $"ui-action:{action.OperationKey}";
        if (!TryBeginUiOperation(operationKey))
        {
            return;
        }

        try
        {
            await RunUiOperationAsync("run plugin control", action.ExecuteAsync);
        }
        finally
        {
            EndUiOperation(operationKey);
        }
    }

    private async void OnPluginInputKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginWorkspaceViewModel workspace }
            || workspace.InputSurface?.CaptureKeyboard != true)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        await RunUiOperationAsync(
            "send plugin key input",
            () => workspace.HandleUiInputAsync(new PluginInputEvent(
                PluginInputEventType.KeyDown,
                Key: key.ToString())));
    }

    private async void OnPluginInputKeyUp(object sender, WpfKeyEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginWorkspaceViewModel workspace }
            || workspace.InputSurface?.CaptureKeyboard != true)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        await RunUiOperationAsync(
            "send plugin key input",
            () => workspace.HandleUiInputAsync(new PluginInputEvent(
                PluginInputEventType.KeyUp,
                Key: key.ToString())));
    }

    private async void OnPluginInputMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginWorkspaceViewModel workspace } surface
            || workspace.InputSurface?.CaptureMouse != true)
        {
            return;
        }

        Keyboard.Focus(surface);
        e.Handled = true;
        var position = e.GetPosition(surface);
        await RunUiOperationAsync(
            "send plugin mouse input",
            () => workspace.HandleUiInputAsync(new PluginInputEvent(
                PluginInputEventType.MouseDown,
                MouseButton: e.ChangedButton.ToString(),
                X: (int)Math.Round(position.X),
                Y: (int)Math.Round(position.Y))));
    }

    private async void OnPluginInputMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PluginWorkspaceViewModel workspace } surface
            || workspace.InputSurface?.CaptureMouse != true)
        {
            return;
        }

        e.Handled = true;
        var position = e.GetPosition(surface);
        await RunUiOperationAsync(
            "send plugin mouse input",
            () => workspace.HandleUiInputAsync(new PluginInputEvent(
                PluginInputEventType.MouseUp,
                MouseButton: e.ChangedButton.ToString(),
                X: (int)Math.Round(position.X),
                Y: (int)Math.Round(position.Y))));
    }

    private async void OnWorkspaceUninstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not FrameworkElement element
            || (element.DataContext as PluginWorkspaceViewModel ?? element.Tag as PluginWorkspaceViewModel) is not PluginWorkspaceViewModel workspace)
        {
            return;
        }

        var operationKey = $"uninstall:{workspace.PluginId}";
        if (!TryBeginUiOperation(operationKey))
        {
            return;
        }

        try
        {
            await RunUiOperationAsync("uninstall plugin", async () =>
            {
                if (viewModel.ConfirmUninstall && !ConfirmUninstall(viewModel, workspace.DisplayName))
                {
                    return;
                }

                await viewModel.UninstallWorkspaceAsync(workspace);
                if (!viewModel.HasSelectedPlugin)
                {
                    HidePluginDetails();
                }
            });
        }
        finally
        {
            EndUiOperation(operationKey);
        }
    }

    private void OnPluginDetailsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement element
            && (element.Tag as PluginWorkspaceViewModel ?? element.DataContext as PluginWorkspaceViewModel) is PluginWorkspaceViewModel workspace)
        {
            viewModel.SelectPluginWorkspace(workspace);
            ShowPluginDetails();
        }
    }

    private void OnPluginCardMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && FindVisualParent<WpfButton>(source) is not null)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { DataContext: PluginWorkspaceViewModel workspace })
        {
            viewModel.SelectPluginWorkspace(workspace);
            ShowPluginDetails();
            e.Handled = true;
        }
    }

    private void OnClosePluginDetailsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearSelectedPlugin();
        }

        HidePluginDetails();
    }

    private void ShowPluginDetails()
    {
        if (PluginDetailsOverlay.Visibility != Visibility.Visible)
        {
            _pluginDetailsReturnFocus = Keyboard.FocusedElement;
        }

        PluginDetailsOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        PluginDetailsTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PluginDetailsOverlay.Visibility = Visibility.Visible;
        SetPluginDetailsTab(HostUiState.PluginDetailsTabs.Overview);
        PluginDetailsCloseButton.Focus();

        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PluginDetailsOverlay.Opacity = 1;
            PluginDetailsTranslate.X = 0;
            return;
        }

        PluginDetailsOverlay.Opacity = 0;
        PluginDetailsTranslate.X = PluginDetailsCard.ActualWidth > 0
            ? PluginDetailsCard.ActualWidth
            : 520;
        PluginDetailsOverlay.BeginAnimation(
            UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
        PluginDetailsTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                From = PluginDetailsTranslate.X,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            });
    }

    private void HidePluginDetails()
    {
        if (PluginDetailsOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        PluginDetailsOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        PluginDetailsTranslate.BeginAnimation(TranslateTransform.XProperty, null);

        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PluginDetailsOverlay.Visibility = Visibility.Collapsed;
            PluginDetailsOverlay.Opacity = 1;
            PluginDetailsTranslate.X = 520;
            RestorePluginDetailsFocus();
            return;
        }

        var slide = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = PluginDetailsTranslate.X,
            To = PluginDetailsCard.ActualWidth > 0 ? PluginDetailsCard.ActualWidth : 520,
            Duration = new Duration(TimeSpan.FromMilliseconds(160)),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
            }
        };
        slide.Completed += (_, _) =>
        {
            PluginDetailsOverlay.Visibility = Visibility.Collapsed;
            PluginDetailsOverlay.Opacity = 1;
            PluginDetailsTranslate.X = 520;
            RestorePluginDetailsFocus();
        };
        PluginDetailsOverlay.BeginAnimation(
            UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation
            {
                From = PluginDetailsOverlay.Opacity,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(140)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                }
            });
        PluginDetailsTranslate.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void OnPluginDetailsOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, PluginDetailsOverlay))
        {
            OnClosePluginDetailsClick(sender, e);
            e.Handled = true;
        }
    }

    private void RestorePluginDetailsFocus()
    {
        var target = _pluginDetailsReturnFocus;
        _pluginDetailsReturnFocus = null;
        if (target is UIElement element
            && element.IsVisible
            && element.IsEnabled)
        {
            element.Focus();
        }
    }

    private void OnPluginDetailsTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tab })
        {
            SetPluginDetailsTab(tab);
        }
    }

    private void SetPluginDetailsTab(string? tab)
    {
        var normalizedTab = HostUiState.PluginDetailsTabs.IsKnown(tab)
            ? tab!
            : HostUiState.PluginDetailsTabs.Overview;
        var tabs = new[]
        {
            PluginOverviewTab,
            PluginOperationsTab,
            PluginLogsTab,
            PluginAboutTab
        };
        foreach (var current in tabs)
        {
            current.ClearValue(WpfControl.BackgroundProperty);
            current.ClearValue(WpfControl.ForegroundProperty);
            current.SetValue(
                AutomationProperties.ItemStatusProperty,
                "Not selected");
        }

        var activeTab = normalizedTab switch
        {
            HostUiState.PluginDetailsTabs.Operations => PluginOperationsTab,
            HostUiState.PluginDetailsTabs.Logs => PluginLogsTab,
            HostUiState.PluginDetailsTabs.About => PluginAboutTab,
            _ => PluginOverviewTab
        };
        activeTab.SetResourceReference(WpfControl.BackgroundProperty, "AccentBrush");
        activeTab.SetResourceReference(WpfControl.ForegroundProperty, "TextBrush");
        activeTab.SetValue(AutomationProperties.ItemStatusProperty, "Selected");

        PluginOverviewPanel.Visibility = normalizedTab == HostUiState.PluginDetailsTabs.Overview
            ? Visibility.Visible
            : Visibility.Collapsed;
        PluginOperationsPanel.Visibility = normalizedTab == HostUiState.PluginDetailsTabs.Operations
            ? Visibility.Visible
            : Visibility.Collapsed;
        PluginLogsPanel.Visibility = normalizedTab == HostUiState.PluginDetailsTabs.Logs
            ? Visibility.Visible
            : Visibility.Collapsed;
        PluginAboutPanel.Visibility = normalizedTab == HostUiState.PluginDetailsTabs.About
            ? Visibility.Visible
            : Visibility.Collapsed;
        _pluginDetailsScrollController.Reset();
    }

    private void OnPluginMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: PluginWorkspaceViewModel workspace }
            || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var menu = new ContextMenu();
        var uninstall = new MenuItem
        {
            Header = viewModel.Localize("Uninstall"),
            Tag = workspace
        };
        uninstall.Click += OnWorkspaceUninstallClick;
        menu.Items.Add(uninstall);
        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
    }

    private void OnPluginSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is WpfTextBox textBox)
        {
            viewModel.SetPluginSearchText(textBox.Text);
            _pageScrollController.Reset();
            UpdatePluginSearchPlaceholder();
        }
    }

    private void UpdatePluginSearchPlaceholder()
    {
        PluginSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(PluginSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnPluginFilterClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { Tag: string filter })
        {
            viewModel.SetPluginFilter(filter);
            _pageScrollController.Reset();
            AnimatePageTransition();
        }
    }

    private void OnPluginSortClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SetPluginSort(viewModel.PluginSort switch
        {
            HostUiState.PluginSorts.Name => HostUiState.PluginSorts.Status,
            HostUiState.PluginSorts.Status => HostUiState.PluginSorts.Version,
            _ => HostUiState.PluginSorts.Name
        });
        _pageScrollController.Reset();
    }

    private void OnClearEventsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearEvents();
        }
    }

    private void OnSettingsSectionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { Tag: string section })
        {
            viewModel.SelectSettingsSection(section switch
            {
                HostUiState.SettingsSections.Plugins => SettingsSection.Plugins,
                HostUiState.SettingsSections.Runtime => SettingsSection.Runtime,
                HostUiState.SettingsSections.About => SettingsSection.About,
                _ => SettingsSection.Appearance
            });
            AnimatePageTransition();
        }
    }

    private void OnOverviewTitleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            && sender is WpfTextBox textBox
            && textBox.IsKeyboardFocusWithin
            && !_isResettingOverviewCopy
            && !_isCommittingSettings)
        {
            QueueOverviewTitleCommit(textBox.Text);
        }
    }

    private void OnOverviewHealthTitleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            && sender is WpfTextBox textBox
            && textBox.IsKeyboardFocusWithin
            && !_isResettingOverviewCopy
            && !_isCommittingSettings)
        {
            QueueOverviewHealthTitleCommit(textBox.Text);
        }
    }

    private void OnTitleBarCenterTextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            && sender is WpfTextBox textBox
            && textBox.IsKeyboardFocusWithin
            && !_isResettingOverviewCopy
            && !_isCommittingSettings)
        {
            QueueTitleBarCenterTextCommit(textBox.Text);
        }
    }

    private void OnOverviewCopyLostFocus(object sender, RoutedEventArgs e)
    {
        CommitPendingSettings();
    }

    private void OnResetOverviewCopyClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            CommitPendingSettings();
            _isResettingOverviewCopy = true;
            try
            {
                viewModel.ResetOverviewTitle();
                viewModel.ResetOverviewHealthTitle();
                viewModel.ResetTitleBarCenterText();
            }
            finally
            {
                _isResettingOverviewCopy = false;
            }
        }
    }

    private void OnSetChineseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetLanguage(AppLanguage.Chinese);
        }
    }

    private void OnSetEnglishClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetLanguage(AppLanguage.English);
        }
    }

    private void OnSetTrayCloseBehaviorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetCloseBehavior(CloseBehavior.MinimizeToTray);
        }
    }

    private void OnSetExitCloseBehaviorClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetCloseBehavior(CloseBehavior.Exit);
        }
    }

    private void OnAppearanceToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not ToggleButton toggle
            || toggle.Tag as string != "motion")
        {
            return;
        }

        viewModel.SetAppearanceOption(reduceMotion: toggle.IsChecked);
    }

    private void OnPluginOptionToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not ToggleButton toggle
            || toggle.Tag is not string option)
        {
            return;
        }

        viewModel.SetPluginManagementOption(
            confirmEnable: option == "enable" ? toggle.IsChecked : null,
            confirmUninstall: option == "uninstall" ? toggle.IsChecked : null,
            showDiagnostics: option == "diagnostics" ? toggle.IsChecked : null);
    }

    private void OnRestoreAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ResetAppearance();
        }
    }

    private void OnBackgroundBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider
            && !_isCommittingSettings
            && (slider.IsKeyboardFocusWithin || slider.IsMouseOver))
        {
            _pendingBackgroundBrightness = (int)Math.Round(e.NewValue);
            _hasPendingBackgroundBrightness = true;
            ScheduleSettingsCommit();
        }
    }

    private void OnCornerRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider
            && !_isCommittingSettings
            && (slider.IsKeyboardFocusWithin || slider.IsMouseOver))
        {
            _pendingCornerRadius = (int)Math.Round(e.NewValue);
            _hasPendingCornerRadius = true;
            ScheduleSettingsCommit();
        }
    }

    private void OnSettingsSliderCommit(object sender, RoutedEventArgs e)
    {
        CommitPendingSettings();
    }

    private void OnSettingsSliderKeyUp(object sender, WpfKeyEventArgs e)
    {
        CommitPendingSettings();
    }

    private async void OnOpenDataDirectoryClick(object sender, RoutedEventArgs e)
    {
        const string operationKey = "open-data-directory";
        if (!TryBeginUiOperation(operationKey))
        {
            return;
        }

        try
        {
            await RunUiOperationAsync("open data directory", () =>
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ToolBox");
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true,
                    ArgumentList = { path }
                });
                return Task.CompletedTask;
            });
        }
        finally
        {
            EndUiOperation(operationKey);
        }
    }

    private void QueueOverviewTitleCommit(string? value)
    {
        _pendingOverviewTitle = value;
        _hasPendingOverviewTitle = true;
        ScheduleSettingsCommit();
    }

    private void QueueOverviewHealthTitleCommit(string? value)
    {
        _pendingOverviewHealthTitle = value;
        _hasPendingOverviewHealthTitle = true;
        ScheduleSettingsCommit();
    }

    private void QueueTitleBarCenterTextCommit(string? value)
    {
        _pendingTitleBarCenterText = value;
        _hasPendingTitleBarCenterText = true;
        ScheduleSettingsCommit();
    }

    private void ScheduleSettingsCommit()
    {
        _settingsCommitTimer.Stop();
        _settingsCommitTimer.Start();
    }

    private void OnSettingsCommitTimerTick(object? sender, EventArgs e)
    {
        CommitPendingSettings();
    }

    private void CommitPendingSettings()
    {
        _settingsCommitTimer.Stop();
        if (DataContext is not MainWindowViewModel viewModel
            || (!_hasPendingOverviewTitle
                && !_hasPendingOverviewHealthTitle
                && !_hasPendingTitleBarCenterText
                && !_hasPendingBackgroundBrightness
                && !_hasPendingCornerRadius))
        {
            ClearPendingSettings();
            return;
        }

        var hasTitle = _hasPendingOverviewTitle;
        var hasHealthTitle = _hasPendingOverviewHealthTitle;
        var hasTitleBarCenterText = _hasPendingTitleBarCenterText;
        var hasBrightness = _hasPendingBackgroundBrightness;
        var hasRadius = _hasPendingCornerRadius;
        var title = _pendingOverviewTitle;
        var healthTitle = _pendingOverviewHealthTitle;
        var titleBarCenterText = _pendingTitleBarCenterText;
        var brightness = _pendingBackgroundBrightness;
        var radius = _pendingCornerRadius;
        ClearPendingSettings();

        _isCommittingSettings = true;
        try
        {
            if (hasTitle)
            {
                viewModel.SetOverviewTitle(title);
            }

            if (hasHealthTitle)
            {
                viewModel.SetOverviewHealthTitle(healthTitle);
            }

            if (hasTitleBarCenterText)
            {
                viewModel.SetTitleBarCenterText(titleBarCenterText);
            }

            if (hasBrightness || hasRadius)
            {
                viewModel.SetAppearanceOption(
                    cornerRadius: hasRadius ? radius : null,
                    backgroundBrightness: hasBrightness ? brightness : null);
            }
        }
        catch (Exception exception)
        {
            viewModel.ReportUiFailure("commit settings", exception);
        }
        finally
        {
            _isCommittingSettings = false;
        }
    }

    private void ClearPendingSettings()
    {
        _hasPendingOverviewTitle = false;
        _hasPendingOverviewHealthTitle = false;
        _hasPendingTitleBarCenterText = false;
        _hasPendingBackgroundBrightness = false;
        _hasPendingCornerRadius = false;
        _pendingOverviewTitle = null;
        _pendingOverviewHealthTitle = null;
        _pendingTitleBarCenterText = null;
    }

    private async Task RunUiOperationAsync(string operation, Func<Task> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(action);
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            try
            {
                if (DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.ReportUiFailure(operation, exception);
                }
                else
                {
                    Debug.WriteLine($"ToolBox UI operation '{operation}' failed: {exception}");
                }
            }
            catch (Exception reportingException)
            {
                // Error reporting must not turn a handled plugin/filesystem
                // failure back into an unhandled async-void dispatcher error.
                Debug.WriteLine($"ToolBox UI operation '{operation}' error reporting failed: {reportingException}");
            }
        }
    }

    private bool TryBeginUiOperation(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && _activeUiOperations.Add(key);
    }

    private void EndUiOperation(string key)
    {
        _activeUiOperations.Remove(key);
    }

    private bool ConfirmUninstall(MainWindowViewModel viewModel, string pluginName)
    {
        var message = string.Format(
            CultureInfo.CurrentCulture,
            viewModel.Localize("UninstallConfirm"),
            pluginName);
        return MessageBox.Show(
            this,
            message,
            viewModel.Localize("UninstallTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private static T? FindVisualParent<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

}
