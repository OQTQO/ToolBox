using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
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
            viewModel.SelectPage(ShellPage.Overview);
        }
    }

    private void OnPluginNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && sender is FrameworkElement { DataContext: PluginWorkspaceViewModel workspace })
        {
            viewModel.SelectPluginWorkspace(workspace);
        }
    }

    private void OnSettingsNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectPage(ShellPage.Settings);
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
            && sender is FrameworkElement { DataContext: PluginWorkspaceViewModel workspace })
        {
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
            && sender is FrameworkElement { DataContext: PluginWorkspaceViewModel workspace }
            && ConfirmUninstall(viewModel, workspace.DisplayName))
        {
            await viewModel.UninstallWorkspaceAsync(workspace);
        }
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

}
