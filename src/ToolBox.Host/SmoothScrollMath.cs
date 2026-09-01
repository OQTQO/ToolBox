namespace ToolBox.Host;

/// <summary>
/// Pure scroll destination and easing calculations. Keeping this logic outside
/// WPF makes wheel merging and boundary behavior deterministic to test.
/// </summary>
internal static class SmoothScrollMath
{
    internal const double WheelStep = 72d;
    internal const double AnimationDurationSeconds = 0.18d;
    internal const double Epsilon = 0.5d;
    internal const int WheelDeltaPerNotch = 120;

    internal static bool TryGetWheelTarget(
        double baseOffset,
        int wheelDelta,
        double scrollableHeight,
        out double targetOffset)
    {
        targetOffset = Clamp(baseOffset, scrollableHeight);
        if (wheelDelta == 0 || scrollableHeight <= Epsilon)
        {
            return false;
        }

        var notches = wheelDelta / (double)WheelDeltaPerNotch;
        var nextOffset = Clamp(
            targetOffset - notches * WheelStep,
            scrollableHeight);
        if (Math.Abs(nextOffset - targetOffset) <= Epsilon)
        {
            targetOffset = nextOffset;
            return false;
        }

        targetOffset = nextOffset;
        return true;
    }

    internal static bool CanScrollInWheelDirection(
        double offset,
        double scrollableHeight,
        int wheelDelta)
    {
        var maxOffset = Math.Max(0d, scrollableHeight);
        if (wheelDelta == 0 || maxOffset <= Epsilon)
        {
            return false;
        }

        var currentOffset = Clamp(offset, maxOffset);
        return wheelDelta > 0
            ? currentOffset > Epsilon
            : currentOffset < maxOffset - Epsilon;
    }

    internal static double GetAnimatedOffset(
        double startOffset,
        double targetOffset,
        double elapsedSeconds)
    {
        var progress = Math.Clamp(
            elapsedSeconds / AnimationDurationSeconds,
            0d,
            1d);
        var easedProgress = 1d - Math.Pow(1d - progress, 3d);
        return startOffset + (targetOffset - startOffset) * easedProgress;
    }

    internal static double Clamp(double offset, double scrollableHeight)
    {
        var maxOffset = double.IsFinite(scrollableHeight)
            ? Math.Max(0d, scrollableHeight)
            : 0d;
        var safeOffset = double.IsFinite(offset) ? offset : 0d;
        return Math.Clamp(safeOffset, 0d, maxOffset);
    }
}
