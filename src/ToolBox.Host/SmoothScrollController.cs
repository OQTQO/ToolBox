using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ToolBox.Host;

/// <summary>
/// WPF adapter for the pure scroll calculations. It only consumes a wheel
/// event when this viewer can move, or when a nested viewer is already at its
/// edge and the page should take over.
/// </summary>
internal sealed class SmoothScrollController
{
    private readonly ScrollViewer _viewer;
    private bool _reduceMotion;
    private bool _isAnimating;
    private double _startOffset;
    private double _targetOffset;
    private long _startTimestamp;
    private bool _isAttached;

    public SmoothScrollController(ScrollViewer viewer, bool reduceMotion = false)
    {
        _viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
        _reduceMotion = reduceMotion;
        Attach();
    }

    public void Attach()
    {
        if (_isAttached)
        {
            return;
        }

        _isAttached = true;
        _viewer.PreviewMouseWheel += OnPreviewMouseWheel;
        _viewer.PreviewKeyDown += OnPreviewKeyDown;
    }

    public void SetReduceMotion(bool reduceMotion)
    {
        _reduceMotion = reduceMotion;
        if (!reduceMotion || !_isAnimating)
        {
            return;
        }

        _viewer.ScrollToVerticalOffset(
            SmoothScrollMath.Clamp(_targetOffset, _viewer.ScrollableHeight));
        StopAnimation();
    }

    public void Reset()
    {
        StopAnimation();
        _viewer.ScrollToVerticalOffset(0);
    }

    public void Detach()
    {
        if (!_isAttached)
        {
            return;
        }

        _isAttached = false;
        _viewer.PreviewMouseWheel -= OnPreviewMouseWheel;
        _viewer.PreviewKeyDown -= OnPreviewKeyDown;
        StopAnimation();
    }

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            // Preserve ScrollViewer's native keyboard behavior while ensuring
            // an earlier wheel animation cannot overwrite the new destination.
            StopAnimation();
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isAttached || e.Delta == 0 || _viewer.ScrollableHeight <= SmoothScrollMath.Epsilon)
        {
            return;
        }

        if (FindNestedScrollViewer(e.OriginalSource as DependencyObject) is { } nested
            && SmoothScrollMath.CanScrollInWheelDirection(
                nested.VerticalOffset,
                nested.ScrollableHeight,
                e.Delta))
        {
            // Let the child text box or embedded surface consume the event
            // while it still has room in the requested direction.
            return;
        }

        var baseOffset = _isAnimating ? _targetOffset : _viewer.VerticalOffset;
        if (!SmoothScrollMath.TryGetWheelTarget(
                baseOffset,
                e.Delta,
                _viewer.ScrollableHeight,
                out var nextOffset))
        {
            // At this viewer's boundary, keep routing the event so an outer
            // scroll container can respond naturally.
            return;
        }

        if (_reduceMotion)
        {
            StopAnimation();
            _viewer.ScrollToVerticalOffset(nextOffset);
            e.Handled = true;
            return;
        }

        _startOffset = _viewer.VerticalOffset;
        _targetOffset = nextOffset;
        _startTimestamp = Stopwatch.GetTimestamp();
        if (!_isAnimating)
        {
            _isAnimating = true;
            CompositionTarget.Rendering += OnRendering;
        }

        e.Handled = true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isAnimating)
        {
            return;
        }

        var elapsedSeconds = (Stopwatch.GetTimestamp() - _startTimestamp)
            / (double)Stopwatch.Frequency;
        var offset = SmoothScrollMath.GetAnimatedOffset(
            _startOffset,
            _targetOffset,
            elapsedSeconds);
        _viewer.ScrollToVerticalOffset(
            SmoothScrollMath.Clamp(offset, _viewer.ScrollableHeight));

        if (elapsedSeconds >= SmoothScrollMath.AnimationDurationSeconds
            || Math.Abs(_viewer.VerticalOffset - _targetOffset) <= SmoothScrollMath.Epsilon)
        {
            _viewer.ScrollToVerticalOffset(
                SmoothScrollMath.Clamp(_targetOffset, _viewer.ScrollableHeight));
            StopAnimation();
        }
    }

    private ScrollViewer? FindNestedScrollViewer(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is ScrollViewer viewer && !ReferenceEquals(viewer, _viewer))
            {
                return viewer;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject source)
    {
        if (source is Visual or Visual3D)
        {
            return VisualTreeHelper.GetParent(source);
        }

        if (source is FrameworkContentElement contentElement)
        {
            return contentElement.Parent;
        }

        return null;
    }

    private void StopAnimation()
    {
        if (!_isAnimating)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _isAnimating = false;
    }
}
