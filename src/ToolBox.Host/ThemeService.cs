using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

internal static class ThemeService
{
    private static readonly ThemePalette FieldPalette = new(
        Canvas: "#EDF2EB",
        Rail: "#FFFDF7",
        Surface: "#FFFDF7",
        SurfaceRaised: "#E6EEE3",
        SurfaceStrong: "#D8E3D8",
        Line: "#C5D1C4",
        LineStrong: "#21372A",
        Text: "#16251C",
        MutedText: "#66766B",
        FaintText: "#718076",
        Accent: "#CFFF52",
        AccentStrong: "#B8E940",
        AccentSoft: "#E6EEE3",
        AccentText: "#435B34",
        AccentMutedText: "#40552F",
        AccentInverseMuted: "#B4C4B2",
        SidebarSelected: "#E6EEE3",
        Healthy: "#CFFF52",
        Warning: "#8C9B51",
        Error: "#A94D3E",
        Scrim: "#3316251C");

    private static readonly Dictionary<string, ThemePalette> Palettes =
        new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["field"] = FieldPalette,
            // Existing ids remain readable in saved settings. They intentionally
            // resolve to the selected visual system so a previous theme cannot
            // bring the rejected dark shell back into the application.
            ["violet"] = FieldPalette,
            ["arctic"] = FieldPalette,
            ["ember"] = FieldPalette,
            ["moss"] = FieldPalette
        };

    internal static string Normalize(string? theme)
    {
        return Palettes.ContainsKey(theme ?? string.Empty)
            ? "field"
            : HostSettingsService.DefaultTheme;
    }

    internal static void Apply(
        string? theme,
        bool transparency = true,
        bool dynamicGlow = false,
        int backgroundBrightness = 100,
        int cornerRadius = 16)
    {
        // Retained for settings-file and caller compatibility. UI 03 no longer
        // renders a dynamic glow field.
        _ = dynamicGlow;
        _ = transparency;

        var application = WpfApplication.Current;
        if (application is null)
        {
            return;
        }

        var palette = Palettes[Normalize(theme)];
        foreach (var pair in palette.Values)
        {
            var color = ColorFromHex(pair.Value);
            if (pair.Key is "CanvasBrush"
                or "RailBrush"
                or "SurfaceBrush"
                or "SurfaceRaisedBrush"
                or "SurfaceStrongBrush"
                or "SidebarSelectedBrush")
            {
                color = ScaleBrightness(color, backgroundBrightness);
            }

            if (application.Resources[pair.Key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
                brush.Opacity = 1;
            }
            else
            {
                application.Resources[pair.Key] = new SolidColorBrush(color);
            }
        }

        application.Resources["PanelCornerRadius"] = new CornerRadius(
            Math.Clamp(cornerRadius, 12, 20));

        SetColor(
            application,
            "DialogBrush",
            ScaleBrightness(ColorFromHex(palette.Surface), backgroundBrightness));
        SetColor(
            application,
            "AccentTextBrush",
            ColorFromHex(palette.AccentText));
        SetColor(
            application,
            "AccentMutedTextBrush",
            ColorFromHex(palette.AccentMutedText));
        SetColor(
            application,
            "AccentInverseMutedBrush",
            ColorFromHex(palette.AccentInverseMuted));
        SetOpacity(application, "DialogBrush", 1);
        SetOpacity(application, "SurfaceBrush", 1);
        SetOpacity(application, "SurfaceRaisedBrush", 1);
        SetOpacity(application, "SurfaceStrongBrush", 1);
    }

    private static MediaColor ColorFromHex(string hex)
    {
        return (MediaColor)MediaColorConverter.ConvertFromString(hex)!;
    }

    private static MediaColor ScaleBrightness(MediaColor color, int percentage)
    {
        var scale = Math.Clamp(percentage, 75, 125) / 100d;
        return MediaColor.FromArgb(
            color.A,
            (byte)Math.Clamp((int)Math.Round(color.R * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.G * scale), 0, 255),
            (byte)Math.Clamp((int)Math.Round(color.B * scale), 0, 255));
    }

    private static void SetOpacity(
        WpfApplication application,
        string key,
        double opacity)
    {
        if (application.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Opacity = opacity;
        }
    }

    private static void SetColor(
        WpfApplication application,
        string key,
        MediaColor color)
    {
        if (application.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            brush.Opacity = 1;
        }
        else
        {
            application.Resources[key] = new SolidColorBrush(color);
        }
    }

    private sealed record ThemePalette(
        string Canvas,
        string Rail,
        string Surface,
        string SurfaceRaised,
        string SurfaceStrong,
        string Line,
        string LineStrong,
        string Text,
        string MutedText,
        string FaintText,
        string Accent,
        string AccentStrong,
        string AccentSoft,
        string AccentText,
        string AccentMutedText,
        string AccentInverseMuted,
        string SidebarSelected,
        string Healthy,
        string Warning,
        string Error,
        string Scrim)
    {
        public IReadOnlyDictionary<string, string> Values { get; } =
            new Dictionary<string, string>
            {
                ["CanvasBrush"] = Canvas,
                ["RailBrush"] = Rail,
                ["SurfaceBrush"] = Surface,
                ["SurfaceRaisedBrush"] = SurfaceRaised,
                ["SurfaceStrongBrush"] = SurfaceStrong,
                ["LineBrush"] = Line,
                ["LineStrongBrush"] = LineStrong,
                ["TextBrush"] = Text,
                ["MutedTextBrush"] = MutedText,
                ["FaintTextBrush"] = FaintText,
                ["AccentBrush"] = Accent,
                ["AccentStrongBrush"] = AccentStrong,
                ["AccentSoftBrush"] = AccentSoft,
                ["AccentTextBrush"] = AccentText,
                ["AccentMutedTextBrush"] = AccentMutedText,
                ["AccentInverseMutedBrush"] = AccentInverseMuted,
                ["SidebarSelectedBrush"] = SidebarSelected,
                ["HealthyBrush"] = Healthy,
                ["WarningBrush"] = Warning,
                ["ErrorBrush"] = Error,
                ["ScrimBrush"] = Scrim
            };
    }
}
