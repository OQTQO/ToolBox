using System.Windows.Threading;

namespace ToolBox.Host;

internal interface IHostUiDispatcher
{
    void Dispatch(Action action);
}

internal sealed class WpfHostUiDispatcher(Dispatcher dispatcher) : IHostUiDispatcher
{
    private readonly Dispatcher _dispatcher = dispatcher
        ?? throw new ArgumentNullException(nameof(dispatcher));

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action);
        }
    }
}

internal sealed class ImmediateHostUiDispatcher : IHostUiDispatcher
{
    public static ImmediateHostUiDispatcher Instance { get; } = new();

    private ImmediateHostUiDispatcher()
    {
    }

    public void Dispatch(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        action();
    }
}
