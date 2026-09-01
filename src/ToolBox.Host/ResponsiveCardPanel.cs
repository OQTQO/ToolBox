using System.Windows;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace ToolBox.Host;

/// <summary>
/// 通用插件卡片面板。
/// 两列时按行统一高度布局，保证 1–4 张卡片组成可预测的 2×2 工作区。
/// </summary>
internal sealed class ResponsiveCardPanel : WpfPanel
{
    public static readonly DependencyProperty ColumnGapProperty =
        DependencyProperty.Register(
            nameof(ColumnGap),
            typeof(double),
            typeof(ResponsiveCardPanel),
            new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty =
        DependencyProperty.Register(
            nameof(RowGap),
            typeof(double),
            typeof(ResponsiveCardPanel),
            new FrameworkPropertyMetadata(14d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinColumnWidth),
            typeof(double),
            typeof(ResponsiveCardPanel),
            new FrameworkPropertyMetadata(360d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var columnGap = ResponsiveCardLayout.NormalizeGap(ColumnGap);
        var rowGap = ResponsiveCardLayout.NormalizeGap(RowGap);
        var minColumnWidth = ResponsiveCardLayout.NormalizeMinColumnWidth(MinColumnWidth);
        var width = ResponsiveCardLayout.ResolveWidth(availableSize.Width, minColumnWidth, columnGap);
        var columns = Math.Max(1, ResponsiveCardLayout.GetColumnCount(width, minColumnWidth, columnGap));
        var itemWidth = ResponsiveCardLayout.GetItemWidth(width, columns, columnGap);
        var rowHeights = MeasureChildren(itemWidth, columns);

        var measuredWidth = double.IsPositiveInfinity(availableSize.Width)
            ? width
            : double.IsFinite(availableSize.Width)
                ? Math.Max(0d, availableSize.Width)
                : 0d;
        return new WpfSize(
            measuredWidth,
            ResponsiveCardLayout.GetPanelHeight(rowHeights, rowGap));
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var columnGap = ResponsiveCardLayout.NormalizeGap(ColumnGap);
        var rowGap = ResponsiveCardLayout.NormalizeGap(RowGap);
        var minColumnWidth = ResponsiveCardLayout.NormalizeMinColumnWidth(MinColumnWidth);
        var width = ResponsiveCardLayout.ResolveWidth(finalSize.Width, minColumnWidth, columnGap);
        var columns = Math.Max(1, ResponsiveCardLayout.GetColumnCount(width, minColumnWidth, columnGap));
        var itemWidth = ResponsiveCardLayout.GetItemWidth(width, columns, columnGap);
        var rowHeights = MeasureChildren(itemWidth, columns);
        var rowY = 0d;

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            var column = index % columns;
            var row = index / columns;
            var x = column * (itemWidth + columnGap);
            var y = rowY;
            var height = rowHeights[row];

            child.Arrange(new Rect(x, y, itemWidth, height));
            if (column == columns - 1 || index == InternalChildren.Count - 1)
            {
                rowY += height + rowGap;
            }
        }

        return finalSize;
    }

    private double[] MeasureChildren(double itemWidth, int columns)
    {
        var rowCount = ResponsiveCardLayout.GetRowCount(InternalChildren.Count, columns);
        var rowHeights = new double[rowCount];
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new WpfSize(itemWidth, double.PositiveInfinity));
        }

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var row = index / columns;
            rowHeights[row] = Math.Max(
                rowHeights[row],
                double.IsFinite(InternalChildren[index].DesiredSize.Height)
                    ? Math.Max(0d, InternalChildren[index].DesiredSize.Height)
                    : 0d);
        }

        return rowHeights;
    }
}
