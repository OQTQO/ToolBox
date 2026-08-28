using System.Windows;
using System.Windows.Input;
using ToolBox.PluginSdk.Experimental;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ToolBox.Host;

public partial class KeyboardTestWorkspaceView
{
    public KeyboardTestWorkspaceView()
    {
        InitializeComponent();
    }

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is KeyboardTestViewModel viewModel)
        {
            await viewModel.ToggleAsync();
        }
    }

    private async void OnApplySettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is KeyboardTestViewModel viewModel)
        {
            await viewModel.ApplySettingsAsync();
        }
    }

    private void OnSurfaceKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is KeyboardTestViewModel viewModel)
        {
            viewModel.ObserveKey(GetKeyName(e), isDown: true);
        }
    }

    private void OnSurfaceKeyUp(object sender, KeyEventArgs e)
    {
        if (DataContext is KeyboardTestViewModel viewModel)
        {
            viewModel.ObserveKey(GetKeyName(e), isDown: false);
        }
    }

    private void OnSurfaceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is KeyboardTestViewModel viewModel
            && sender is IInputElement surface
            && TryGetMouseButton(e.ChangedButton, out var button))
        {
            Keyboard.Focus(surface);
            var position = e.GetPosition(surface);
            viewModel.ObserveMouse(button, isDown: true, (int)Math.Round(position.X), (int)Math.Round(position.Y));
        }
    }

    private void OnSurfaceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is KeyboardTestViewModel viewModel
            && sender is IInputElement surface
            && TryGetMouseButton(e.ChangedButton, out var button))
        {
            var position = e.GetPosition(surface);
            viewModel.ObserveMouse(button, isDown: false, (int)Math.Round(position.X), (int)Math.Round(position.Y));
        }
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
