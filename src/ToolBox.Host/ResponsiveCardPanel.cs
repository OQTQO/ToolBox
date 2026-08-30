using System.Windows;
using WpfPanel = System.Windows.Controls.Panel;
using WpfSize = System.Windows.Size;

namespace ToolBox.Host;

/// <summary>
/// 通用插件卡片面板。
/// 两列时按列独立布局，避免 UniformGrid 因某一张卡片较高而把整行拉出大片空白。
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
        var width = ResolveWidth(availableSize.Width);
        var columns = GetColumnCount(width);
        var itemWidth = GetItemWidth(width, columns);
        var columnHeights = MeasureChildren(itemWidth, columns);

        return new WpfSize(
            double.IsInfinity(availableSize.Width) ? width : availableSize.Width,
            GetPanelHeight(columnHeights));
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var width = ResolveWidth(finalSize.Width);
        var columns = GetColumnCount(width);
        var itemWidth = GetItemWidth(width, columns);
        var columnY = new double[columns];

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var child = InternalChildren[index];
            var column = index % columns;
            var x = column * (itemWidth + ColumnGap);
            var y = columnY[column];
            var height = child.DesiredSize.Height;

            child.Arrange(new Rect(x, y, itemWidth, height));
            columnY[column] = y + height + RowGap;
        }

        return finalSize;
    }

    private double[] MeasureChildren(double itemWidth, int columns)
    {
        var columnHeights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new WpfSize(itemWidth, double.PositiveInfinity));
        }

        for (var index = 0; index < InternalChildren.Count; index++)
        {
            var column = index % columns;
            var rowInColumn = index / columns;
            if (rowInColumn > 0)
            {
                columnHeights[column] += RowGap;
            }

            columnHeights[column] += InternalChildren[index].DesiredSize.Height;
        }

        return columnHeights;
    }

    private double ResolveWidth(double availableWidth)
    {
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return MinColumnWidth * 2 + ColumnGap;
        }

        return availableWidth;
    }

    private int GetColumnCount(double width) =>
        width >= MinColumnWidth * 2 + ColumnGap ? 2 : 1;

    private double GetItemWidth(double width, int columns) =>
        columns == 1 ? width : Math.Max(0, (width - ColumnGap) / 2);

    private static double GetPanelHeight(IReadOnlyList<double> columnHeights)
    {
        var height = 0d;
        foreach (var columnHeight in columnHeights)
        {
            height = Math.Max(height, columnHeight);
        }

        return Math.Max(0, height);
    }
}
