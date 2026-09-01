namespace ToolBox.Host;

internal static class ResponsiveCardLayout
{
    internal static double NormalizeGap(double value) =>
        double.IsFinite(value) ? Math.Max(0d, value) : 0d;

    internal static double NormalizeMinColumnWidth(double value) =>
        double.IsFinite(value) ? Math.Max(1d, value) : 1d;

    internal static double ResolveWidth(double availableWidth, double minColumnWidth, double columnGap)
    {
        var safeMinWidth = NormalizeMinColumnWidth(minColumnWidth);
        var safeGap = NormalizeGap(columnGap);
        if (double.IsPositiveInfinity(availableWidth))
        {
            return safeMinWidth * 2d + safeGap;
        }

        return double.IsFinite(availableWidth)
            ? Math.Max(0d, availableWidth)
            : 0d;
    }

    internal static int GetColumnCount(double width, double minColumnWidth, double columnGap)
    {
        var safeWidth = ResolveWidth(width, minColumnWidth, columnGap);
        if (safeWidth <= 0d)
        {
            return 0;
        }

        var safeMinWidth = NormalizeMinColumnWidth(minColumnWidth);
        var safeGap = NormalizeGap(columnGap);
        return safeWidth >= safeMinWidth * 2d + safeGap ? 2 : 1;
    }

    internal static double GetItemWidth(double width, int columns, double columnGap)
    {
        var safeGap = NormalizeGap(columnGap);
        return columns <= 1
            ? Math.Max(0d, double.IsFinite(width) ? width : 0d)
            : Math.Max(0d, (width - safeGap) / 2d);
    }

    internal static int GetRowCount(int itemCount, int columns)
    {
        if (itemCount <= 0)
        {
            return 0;
        }

        return (itemCount + Math.Max(1, columns) - 1) / Math.Max(1, columns);
    }

    internal static double GetPanelHeight(IReadOnlyList<double> rowHeights, double rowGap)
    {
        ArgumentNullException.ThrowIfNull(rowHeights);
        if (rowHeights.Count == 0)
        {
            return 0d;
        }

        var safeGap = NormalizeGap(rowGap);
        var height = rowHeights.Sum(value => double.IsFinite(value) ? Math.Max(0d, value) : 0d);
        return Math.Max(0d, height + (rowHeights.Count - 1) * safeGap);
    }
}
