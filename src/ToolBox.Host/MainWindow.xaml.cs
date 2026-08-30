using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using ToolBox.PluginSdk;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ToolBox.Host;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmWindowCornerDoNotRound = 1;
    private const int DwmWindowCornerRound = 2;
    private const double WindowCornerRadius = 10;
    private readonly IHostApplicationCommands _applicationCommands;
    private System.Windows.Media.Animation.Storyboard? _glowStoryboard;
    private bool _allowClose;

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
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The window can close between the mouse event and DragMove.
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnWindowFrameLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowFrameClip();
    }

    private void OnWindowFrameSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateWindowFrameClip();
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowFrameClip();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateWindowFrameClip();
    }

    private void UpdateWindowFrameClip()
    {
        if (WindowFrame.ActualWidth <= 0 || WindowFrame.ActualHeight <= 0)
        {
            return;
        }

        var radius = WindowState == WindowState.Maximized
            ? 0
            : WindowCornerRadius;
        Chrome.CornerRadius = new CornerRadius(radius);
        ApplyDwmCornerPreference(WindowState == WindowState.Maximized);
        WindowFrame.Clip = new RectangleGeometry(
            new Rect(0, 0, WindowFrame.ActualWidth, WindowFrame.ActualHeight),
            radius,
            radius);
    }

    private void ApplyDwmCornerPreference(bool maximized)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var preference = maximized
            ? DwmWindowCornerDoNotRound
            : DwmWindowCornerRound;
        var result = DwmSetWindowAttribute(
            handle,
            DwmWindowCornerPreferenceAttribute,
            ref preference,
            Marshal.SizeOf<int>());
        if (result != 0)
        {
            Debug.WriteLine($"DWM window corner preference was not applied (HRESULT 0x{result:X8}).");
        }
    }

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
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

    internal void PrepareForShutdown()
    {
        _allowClose = true;
    }

    private void OnOverviewNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            HidePluginDetails();
            viewModel.SelectPage(ShellPage.Overview);
            AnimatePageTransition();
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveCardPanels();
        UpdateGlowAnimation();
    }

    private void ApplyResponsiveCardPanels()
    {
        if (Resources["PluginCardTemplate"] is not DataTemplate pluginCardTemplate)
        {
            return;
        }

        foreach (var itemsControl in FindVisualChildren<ItemsControl>(this))
        {
            if (!ReferenceEquals(itemsControl.ItemTemplate, pluginCardTemplate))
            {
                continue;
            }

            var panelFactory = new FrameworkElementFactory(typeof(ResponsiveCardPanel));
            panelFactory.SetValue(ResponsiveCardPanel.ColumnGapProperty, 14d);
            panelFactory.SetValue(ResponsiveCardPanel.RowGapProperty, 14d);
            panelFactory.SetValue(ResponsiveCardPanel.MinColumnWidthProperty, 360d);
            itemsControl.ItemsPanel = new ItemsPanelTemplate(panelFactory);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnWindowUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _glowStoryboard?.Remove(this);
        _glowStoryboard = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.DynamicGlow)
            or nameof(MainWindowViewModel.ReduceMotion)
            or nameof(MainWindowViewModel.Theme)
            or nameof(MainWindowViewModel.Transparency))
        {
            UpdateGlowAnimation();
        }
    }

    private void UpdateGlowAnimation()
    {
        if (DataContext is not MainWindowViewModel viewModel
            || viewModel.ReduceMotion
            || !viewModel.DynamicGlow)
        {
            _glowStoryboard?.Remove(this);
            _glowStoryboard = null;
            GlowOneTransform.X = 0;
            return;
        }

        if (_glowStoryboard is not null)
        {
            return;
        }

        _glowStoryboard = new System.Windows.Media.Animation.Storyboard
        {
            AutoReverse = true,
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
        };
        var first = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = -14,
            To = 18,
            Duration = new Duration(TimeSpan.FromSeconds(26)),
            EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };
        System.Windows.Media.Animation.Storyboard.SetTarget(first, GlowOneTransform);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(first, new PropertyPath(TranslateTransform.XProperty));
        _glowStoryboard.Children.Add(first);
        _glowStoryboard.Begin(this, true);
    }

    private void OnPluginsNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            HidePluginDetails();
            viewModel.SelectPage(ShellPage.Plugin);
            AnimatePageTransition();
        }
    }

    private void OnActivityNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            HidePluginDetails();
            viewModel.SelectPage(ShellPage.Activity);
            AnimatePageTransition();
        }
    }

    private void OnPluginNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { DataContext: PluginWorkspaceViewModel workspace })
        {
            viewModel.SelectPluginWorkspace(workspace);
            ShowPluginDetails();
        }
    }

    private void OnSettingsNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            HidePluginDetails();
            viewModel.SelectPage(ShellPage.Settings);
            AnimatePageTransition();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape
            && DataContext is MainWindowViewModel viewModel
            && viewModel.HasSelectedPlugin)
        {
            viewModel.ClearSelectedPlugin();
            HidePluginDetails();
            e.Handled = true;
        }
    }

    private void AnimatePageTransition()
    {
        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PageHost.BeginAnimation(UIElement.OpacityProperty, null);
            PageHost.Opacity = 1;
            PageTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            PageTranslate.X = 0;
            return;
        }

        PageHost.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.25,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        PageTranslate.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 5,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private void OnRefreshStatusClick(object sender, RoutedEventArgs e)
    {
        AnimatePageTransition();
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

    private async void OnWorkspaceOpenedToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is ToggleButton { DataContext: PluginWorkspaceViewModel workspace } toggle)
        {
            await viewModel.ToggleWorkspaceOpenedAsync(workspace);
            toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        }
    }

    private async void OnInstallPluginClick(object sender, RoutedEventArgs e)
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
    }

    private async void OnWorkspaceInstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not FrameworkElement { DataContext: PluginWorkspaceViewModel workspace })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = workspace.InstallDialogTitle,
            Filter = viewModel.Localize("PackageDialogFilter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.InstallWorkspacePackageAsync(workspace, dialog.FileName);
        }
    }

    private async void OnWorkspaceRuntimeToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement element
            && (element.DataContext as PluginWorkspaceViewModel ?? element.Tag as PluginWorkspaceViewModel) is PluginWorkspaceViewModel workspace)
        {
            if (!workspace.IsRuntimeEnabled && viewModel.ConfirmEnable
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
        }
    }

    private async void OnPluginUiActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PluginUiActionViewModel action })
        {
            await action.ExecuteAsync();
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
        await workspace.HandleUiInputAsync(new PluginInputEvent(
            PluginInputEventType.KeyDown,
            Key: key.ToString()));
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
        await workspace.HandleUiInputAsync(new PluginInputEvent(
            PluginInputEventType.KeyUp,
            Key: key.ToString()));
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
        await workspace.HandleUiInputAsync(new PluginInputEvent(
            PluginInputEventType.MouseDown,
            MouseButton: e.ChangedButton.ToString(),
            X: (int)Math.Round(position.X),
            Y: (int)Math.Round(position.Y)));
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
        await workspace.HandleUiInputAsync(new PluginInputEvent(
            PluginInputEventType.MouseUp,
            MouseButton: e.ChangedButton.ToString(),
            X: (int)Math.Round(position.X),
            Y: (int)Math.Round(position.Y)));
    }

    private async void OnWorkspaceUninstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement element
            && (element.DataContext as PluginWorkspaceViewModel ?? element.Tag as PluginWorkspaceViewModel) is PluginWorkspaceViewModel workspace
            && (!viewModel.ConfirmUninstall || ConfirmUninstall(viewModel, workspace.DisplayName)))
        {
            await viewModel.UninstallWorkspaceAsync(workspace);
            if (!viewModel.HasSelectedPlugin)
            {
                HidePluginDetails();
            }
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
            && FindVisualParent<System.Windows.Controls.Button>(source) is not null)
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
            HidePluginDetails();
        }
    }

    private void ShowPluginDetails()
    {
        PluginDetailsOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        PluginDetailsCard.BeginAnimation(UIElement.OpacityProperty, null);
        PluginDetailsCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PluginDetailsCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        PluginDetailsOverlay.Visibility = Visibility.Visible;
        SetPluginDetailsTab("overview");

        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PluginDetailsOverlay.Opacity = 1;
            PluginDetailsCard.Opacity = 1;
            ((ScaleTransform)PluginDetailsCard.RenderTransform).ScaleX = 1;
            ((ScaleTransform)PluginDetailsCard.RenderTransform).ScaleY = 1;
            return;
        }

        PluginDetailsOverlay.Opacity = 0;
        PluginDetailsCard.Opacity = 0.98;
        var scale = (ScaleTransform)PluginDetailsCard.RenderTransform;
        scale.ScaleX = 0.97;
        scale.ScaleY = 0.97;
        PluginDetailsOverlay.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        PluginDetailsCard.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.98,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.97,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.97,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private void HidePluginDetails()
    {
        if (PluginDetailsOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        PluginDetailsOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        PluginDetailsCard.BeginAnimation(UIElement.OpacityProperty, null);
        var scale = (ScaleTransform)PluginDetailsCard.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PluginDetailsOverlay.Visibility = Visibility.Collapsed;
            PluginDetailsOverlay.Opacity = 1;
            PluginDetailsCard.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }

        var fade = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = PluginDetailsOverlay.Opacity,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            PluginDetailsOverlay.Visibility = Visibility.Collapsed;
            PluginDetailsOverlay.Opacity = 1;
            PluginDetailsCard.Opacity = 1;
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        };
        PluginDetailsOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = scale.ScaleX,
            To = 0.985,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = scale.ScaleY,
            To = 0.985,
            Duration = new Duration(TimeSpan.FromMilliseconds(130)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        });
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

    private void OnPluginDetailsTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tab })
        {
            SetPluginDetailsTab(tab);
        }
    }

    private void SetPluginDetailsTab(string tab)
    {
        PluginOverviewTab.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        PluginOverviewTab.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        PluginOperationsTab.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        PluginOperationsTab.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        PluginLogsTab.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        PluginLogsTab.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
        PluginAboutTab.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        PluginAboutTab.ClearValue(System.Windows.Controls.Control.ForegroundProperty);

        var activeTab = tab switch
        {
            "operations" => PluginOperationsTab,
            "logs" => PluginLogsTab,
            "about" => PluginAboutTab,
            _ => PluginOverviewTab
        };
        activeTab.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSoftBrush");
        activeTab.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextBrush");

        PluginOverviewPanel.Visibility = tab == "overview" ? Visibility.Visible : Visibility.Collapsed;
        PluginOperationsPanel.Visibility = tab == "operations" ? Visibility.Visible : Visibility.Collapsed;
        PluginLogsPanel.Visibility = tab == "logs" ? Visibility.Visible : Visibility.Collapsed;
        PluginAboutPanel.Visibility = tab == "about" ? Visibility.Visible : Visibility.Collapsed;
        PluginDetailsScroll.ScrollToTop();

        if (DataContext is MainWindowViewModel { ReduceMotion: true })
        {
            PluginDetailsScroll.BeginAnimation(UIElement.OpacityProperty, null);
            PluginDetailsScroll.Opacity = 1;
            return;
        }

        PluginDetailsScroll.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0.2,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        });
    }

    private void OnPluginMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not PluginWorkspaceViewModel workspace)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var option in new[] { "compact", "standard", "featured" })
        {
            var item = new MenuItem
            {
                Header = viewModelText(option),
                Tag = new CardSizeMenuTarget(workspace, option)
            };
            item.Click += OnPluginCardSizeMenuClick;
            menu.Items.Add(item);
        }

        var reset = new MenuItem { Header = DataContext is MainWindowViewModel vm ? vm.Localize("Reset") : "Reset" };
        reset.Click += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ClearPluginCardSize(workspace);
            }
        };
        menu.Items.Add(new Separator());
        menu.Items.Add(reset);
        button.ContextMenu = menu;
        menu.IsOpen = true;

        string viewModelText(string option)
        {
            return DataContext is MainWindowViewModel viewModel
                ? viewModel.Localize($"CardSize{option switch { "compact" => "Compact", "featured" => "Featured", _ => "Standard" }}")
                : option;
        }
    }

    private void OnPluginCardSizeMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: CardSizeMenuTarget target }
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SetPluginCardSize(target.Workspace, target.Size);
        }
    }

    private void OnPluginSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && sender is System.Windows.Controls.TextBox textBox)
        {
            viewModel.SetPluginSearchText(textBox.Text);
        }
    }

    private void OnPluginFilterClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { Tag: string filter })
        {
            viewModel.SetPluginFilter(filter);
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
            "name" => "status",
            "status" => "version",
            _ => "name"
        });
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
                "plugins" => SettingsSection.Plugins,
                "runtime" => SettingsSection.Runtime,
                "about" => SettingsSection.About,
                _ => SettingsSection.Appearance
            });
            AnimatePageTransition();
        }
    }

    private void OnThemeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { Tag: string theme })
        {
            viewModel.SetTheme(theme);
        }
    }

    private void OnOverviewTitleTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is System.Windows.Controls.TextBox textBox
            && textBox.IsKeyboardFocusWithin)
        {
            viewModel.SetOverviewTitle(textBox.Text);
        }
    }

    private void OnResetOverviewTitleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ResetOverviewTitle();
        }
    }

    private void OnAppearanceToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel
            || sender is not ToggleButton toggle
            || toggle.Tag is not string option)
        {
            return;
        }

        viewModel.SetAppearanceOption(
            dynamicGlow: option == "glow" ? toggle.IsChecked : null,
            reduceMotion: option == "motion" ? toggle.IsChecked : null,
            transparency: option == "transparency" ? toggle.IsChecked : null);
        UpdateGlowAnimation();
    }

    private void OnDefaultCardSizeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { Tag: string size })
        {
            viewModel.SetDefaultPluginCardSize(size);
        }
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
        if (DataContext is MainWindowViewModel viewModel
            && sender is System.Windows.Controls.Slider slider
            && (slider.IsKeyboardFocusWithin || slider.IsMouseOver))
        {
            viewModel.SetAppearanceOption(backgroundBrightness: (int)Math.Round(e.NewValue));
        }
    }

    private void OnCornerRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is System.Windows.Controls.Slider slider
            && (slider.IsKeyboardFocusWithin || slider.IsMouseOver))
        {
            viewModel.SetAppearanceOption(cornerRadius: (int)Math.Round(e.NewValue));
        }
    }

    private void OnOpenDataDirectoryClick(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolBox");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
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

    private sealed record CardSizeMenuTarget(PluginWorkspaceViewModel Workspace, string Size);

}
