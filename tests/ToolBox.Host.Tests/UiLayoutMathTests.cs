using ToolBox.Host;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class UiLayoutMathTests
{
    private static readonly double[] TwoRows = [128d, 128d];
    private static readonly double[] EmptyRows = [];
    private static readonly double[] InvalidRows = [double.NaN, 128d];

    [Fact]
    public void WheelTargetMovesByOneSeventyTwoPixelNotchAndClampsToBounds()
    {
        Assert.True(SmoothScrollMath.TryGetWheelTarget(300, 120, 900, out var upTarget));
        Assert.Equal(228, upTarget);

        Assert.True(SmoothScrollMath.TryGetWheelTarget(300, -120, 900, out var downTarget));
        Assert.Equal(372, downTarget);

        Assert.True(SmoothScrollMath.TryGetWheelTarget(10, 120, 900, out var topTarget));
        Assert.Equal(0, topTarget);

        Assert.True(SmoothScrollMath.TryGetWheelTarget(890, -120, 900, out var bottomTarget));
        Assert.Equal(900, bottomTarget);
    }

    [Fact]
    public void WheelTargetRejectsEmptyDeltaAndNonScrollableContent()
    {
        Assert.False(SmoothScrollMath.TryGetWheelTarget(10, 0, 900, out _));
        Assert.False(SmoothScrollMath.TryGetWheelTarget(10, 120, 0, out _));
        Assert.False(SmoothScrollMath.CanScrollInWheelDirection(0, 0, 120));
        Assert.False(SmoothScrollMath.CanScrollInWheelDirection(0, 100, 120));
        Assert.True(SmoothScrollMath.CanScrollInWheelDirection(50, 100, -120));
        Assert.False(SmoothScrollMath.CanScrollInWheelDirection(100, 100, -120));
    }

    [Fact]
    public void AnimatedOffsetEasesFromStartToTarget()
    {
        const double start = 0;
        const double target = 240;

        Assert.Equal(start, SmoothScrollMath.GetAnimatedOffset(start, target, 0));
        var middle = SmoothScrollMath.GetAnimatedOffset(start, target, 0.09);
        Assert.InRange(middle, start, target);
        Assert.Equal(target, SmoothScrollMath.GetAnimatedOffset(start, target, 0.18));
        Assert.Equal(target, SmoothScrollMath.GetAnimatedOffset(start, target, 1));
    }

    [Fact]
    public void ResponsiveLayoutUsesOneOrTwoColumnsAtSafeBoundaries()
    {
        Assert.Equal(0, ResponsiveCardLayout.GetColumnCount(0, 320, 12));
        Assert.Equal(1, ResponsiveCardLayout.GetColumnCount(639, 320, 12));
        Assert.Equal(2, ResponsiveCardLayout.GetColumnCount(652, 320, 12));
        Assert.Equal(0, ResponsiveCardLayout.GetColumnCount(double.NaN, 320, 12));
        Assert.Equal(2, ResponsiveCardLayout.GetColumnCount(900, double.PositiveInfinity, 12));
    }

    [Fact]
    public void ResponsiveLayoutKeepsIncompleteLastRowSafe()
    {
        Assert.Equal(0, ResponsiveCardLayout.GetRowCount(0, 2));
        Assert.Equal(1, ResponsiveCardLayout.GetRowCount(1, 2));
        Assert.Equal(2, ResponsiveCardLayout.GetRowCount(3, 2));
        Assert.Equal(2, ResponsiveCardLayout.GetRowCount(4, 2));
        Assert.Equal(3, ResponsiveCardLayout.GetRowCount(5, 2));

        Assert.Equal(268, ResponsiveCardLayout.GetPanelHeight(TwoRows, 12));
        Assert.Equal(0, ResponsiveCardLayout.GetPanelHeight(EmptyRows, 12));
        Assert.Equal(128, ResponsiveCardLayout.GetPanelHeight(InvalidRows, -5));
    }
}
