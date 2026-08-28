using System.Globalization;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
using ToolBox.PluginSdk.Experimental;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

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

    private void OnKeyboardNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectPage(ShellPage.KeyboardTest);
        }
    }

    private void OnAudioRelayNavigationClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectPage(ShellPage.AudioRelay);
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

    private async void OnKeyboardOpenedToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ToggleKeyboardOpenedAsync();
            if (sender is ToggleButton toggle)
            {
                toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
        }
    }

    private async void OnAudioRelayOpenedToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ToggleAudioRelayOpenedAsync();
            if (sender is ToggleButton toggle)
            {
                toggle.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
            }
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

    private async void OnKeyboardTestToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.KeyboardTest.ToggleAsync();
        }
    }

    private async void OnKeyboardTestInstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = viewModel.Localize("InstallKeyboardDialogTitle"),
            Filter = viewModel.Localize("PackageDialogFilter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.InstallPackageAsync(dialog.FileName);
        }
    }

    private async void OnKeyboardTestApplySettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.KeyboardTest.ApplySettingsAsync();
        }
    }

    private async void OnAudioRelayToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AudioRelay.ToggleAsync();
        }
    }

    private void OnAudioRelayRestartClick(object sender, RoutedEventArgs e)
    {
        _applicationCommands.RequestRestart();
    }

    private async void OnAudioRelayInstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = viewModel.Localize("InstallAudioDialogTitle"),
            Filter = viewModel.Localize("PackageDialogFilter"),
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.InstallPackageAsync(dialog.FileName);
        }
    }

    private async void OnKeyboardUninstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && ConfirmUninstall(viewModel, viewModel.Localize("KeyboardMouse")))
        {
            await viewModel.UninstallKeyboardAsync();
        }
    }

    private async void OnAudioRelayUninstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && ConfirmUninstall(viewModel, viewModel.Localize("PhoneAudioRelay")))
        {
            await viewModel.UninstallAudioRelayAsync();
        }
    }

    private async void OnAudioRelayRefreshClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AudioRelay.RefreshAsync();
        }
    }

    private async void OnAudioRelayConnectClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AudioRelay.ConnectAsync();
        }
    }

    private async void OnAudioRelayDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.AudioRelay.DisconnectAsync();
        }
    }

    private void OnKeyboardSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.KeyboardTest.ObserveKey(GetKeyName(e), isDown: true);
        }
    }

    private void OnKeyboardSurfaceKeyUp(object sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.KeyboardTest.ObserveKey(GetKeyName(e), isDown: false);
        }
    }

    private void OnKeyboardSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && TryGetMouseButton(e.ChangedButton, out var button))
        {
            KeyboardTestSurface.Focus();
            var position = e.GetPosition(KeyboardTestSurface);
            viewModel.KeyboardTest.ObserveMouse(
                button,
                isDown: true,
                (int)Math.Round(position.X),
                (int)Math.Round(position.Y));
        }
    }

    private void OnKeyboardSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel
            && TryGetMouseButton(e.ChangedButton, out var button))
        {
            var position = e.GetPosition(KeyboardTestSurface);
            viewModel.KeyboardTest.ObserveMouse(
                button,
                isDown: false,
                (int)Math.Round(position.X),
                (int)Math.Round(position.Y));
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

    private static string GetKeyName(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key.ToString();
    }

    private static bool TryGetMouseButton(MouseButton button, out KeyboardTestMouseButton mappedButton)
    {
        mappedButton = button switch
        {
            MouseButton.Left => KeyboardTestMouseButton.Left,
            MouseButton.Right => KeyboardTestMouseButton.Right,
            MouseButton.Middle => KeyboardTestMouseButton.Middle,
            _ => default
        };

        return button is MouseButton.Left or MouseButton.Right or MouseButton.Middle;
    }
}
