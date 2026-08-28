using System.Windows;

namespace ToolBox.Host;

public partial class AudioRelayWorkspaceView
{
    public static readonly RoutedEvent RestartRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(RestartRequested),
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(AudioRelayWorkspaceView));

    public AudioRelayWorkspaceView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler RestartRequested
    {
        add => AddHandler(RestartRequestedEvent, value);
        remove => RemoveHandler(RestartRequestedEvent, value);
    }

    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioRelayViewModel viewModel)
        {
            await viewModel.ToggleAsync();
        }
    }

    private void OnRestartClick(object sender, RoutedEventArgs e)
    {
        RaiseEvent(new RoutedEventArgs(RestartRequestedEvent));
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioRelayViewModel viewModel)
        {
            await viewModel.RefreshAsync();
        }
    }

    private async void OnConnectClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioRelayViewModel viewModel)
        {
            await viewModel.ConnectAsync();
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AudioRelayViewModel viewModel)
        {
            await viewModel.DisconnectAsync();
        }
    }
}
