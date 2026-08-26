using System.Diagnostics;

namespace ToolBox.Core.Lifetime;

public sealed class ShutdownDeadline : IDisposable
{
    private readonly CancellationToken _externalCancellation;
    private readonly CancellationTokenSource _cancellation;
    private readonly long _deadlineTimestamp;
    private bool _disposed;

    private ShutdownDeadline(
        TimeSpan timeout,
        CancellationTokenSource cancellation,
        CancellationToken externalCancellation)
    {
        _externalCancellation = externalCancellation;
        _cancellation = cancellation;
        _deadlineTimestamp = Stopwatch.GetTimestamp()
            + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
    }

    public CancellationToken Token => _cancellation.Token;

    public bool IsExpired => Stopwatch.GetTimestamp() >= _deadlineTimestamp;

    public bool IsExternallyCancelled => _externalCancellation.IsCancellationRequested;

    public TimeSpan Remaining
    {
        get
        {
            var remainingTicks = _deadlineTimestamp - Stopwatch.GetTimestamp();

            if (remainingTicks <= 0)
            {
                return TimeSpan.Zero;
            }

            return TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
        }
    }

    public static ShutdownDeadline Start(
        PluginShutdownOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellation.CancelAfter(options.Timeout);
        return new ShutdownDeadline(options.Timeout, cancellation, cancellationToken);
    }

    public void ThrowIfExpired()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Dispose();
    }
}
