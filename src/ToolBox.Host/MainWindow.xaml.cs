using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ToolBox.PluginSdk.Experimental;

namespace ToolBox.Host;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

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

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
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
            Title = "Install Keyboard & Mouse Test package",
            Filter = "ToolBox packages (*.tpk)|*.tpk|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.KeyboardTest.InstallPackageAsync(dialog.FileName);
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

    private async void OnAudioRelayInstallClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Install Phone Audio Relay package",
            Filter = "ToolBox packages (*.tpk)|*.tpk|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.AudioRelay.InstallPackageAsync(dialog.FileName);
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
