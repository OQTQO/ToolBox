using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfApplication = System.Windows.Application;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace ToolBox.Host;

internal static class ThemeService
{
    private static readonly IReadOnlyDictionary<string, ThemePalette> Palettes =
        new Dictionary<string, ThemePalette>(StringComparer.OrdinalIgnoreCase)
        {
            ["violet"] = new(
                Canvas: "#0D0F1B",
                CanvasDeep: "#080A12",
                Rail: "#111425",
                Surface: "#171A2B",
                SurfaceRaised: "#1D2135",
                SurfaceStrong: "#252A42",
                Line: "#323650",
                LineStrong: "#4B4F70",
                Text: "#F4F2FC",
                MutedText: "#A7A8BE",
                FaintText: "#70738E",
                Accent: "#B9A6FF",
                AccentStrong: "#8D75F2",
                AccentSoft: "#332D5D",
                Healthy: "#78E5B0",
                Warning: "#F1C56B",
                Error: "#FF8E9C",
                GlowOne: "#5847B8",
                GlowTwo: "#51458D",
                Shadow: "#03040A"),
            ["arctic"] = new(
                Canvas: "#09131C",
                CanvasDeep: "#061019",
                Rail: "#0D1B27",
                Surface: "#112432",
                SurfaceRaised: "#173141",
                SurfaceStrong: "#214457",
                Line: "#2D4A5B",
                LineStrong: "#4B7283",
                Text: "#EEF8FB",
                MutedText: "#9EBBC4",
                FaintText: "#6D8F9A",
                Accent: "#8EE7F5",
                AccentStrong: "#3EBBCF",
                AccentSoft: "#1C4D5C",
                Healthy: "#72E4BE",
                Warning: "#F2C36A",
                Error: "#FF9B9B",
                GlowOne: "#1D7B9D",
                GlowTwo: "#4555A5",
                Shadow: "#020A10"),
            ["ember"] = new(
                Canvas: "#160E12",
                CanvasDeep: "#0D090C",
                Rail: "#1F1218",
                Surface: "#28171B",
                SurfaceRaised: "#332021",
                SurfaceStrong: "#49302A",
                Line: "#5A3835",
                LineStrong: "#81514A",
                Text: "#FFF3E6",
                MutedText: "#C9A99A",
                FaintText: "#8F6E67",
                Accent: "#F0B56B",
                AccentStrong: "#C77A3E",
                AccentSoft: "#5A3426",
                Healthy: "#8FE1AC",
                Warning: "#F0BD5B",
                Error: "#FF918B",
                GlowOne: "#8E3B3B",
                GlowTwo: "#815429",
                Shadow: "#090406"),
            ["moss"] = new(
                Canvas: "#0B1515",
                CanvasDeep: "#07100F",
                Rail: "#10201D",
                Surface: "#162A27",
                SurfaceRaised: "#1D3530",
                SurfaceStrong: "#28463E",
                Line: "#31564C",
                LineStrong: "#568074",
                Text: "#EFF9F0",
                MutedText: "#A3BDAF",
                FaintText: "#718D81",
                Accent: "#99E2BF",
                AccentStrong: "#4DBA91",
                AccentSoft: "#205341",
                Healthy: "#8FE8B6",
                Warning: "#E9C56B",
                Error: "#FF9691",
                GlowOne: "#2F755F",
                GlowTwo: "#2F6380",
                Shadow: "#020908")
        };

    internal static string Normalize(string? theme)
    {
        return Palettes.ContainsKey(theme ?? string.Empty) ? theme! : HostSettingsService.DefaultTheme;
    }

    internal static void Apply(string? theme, bool transparency = true, bool dynamicGlow = true, int backgroundBrightness = 100, int cornerRadius = 14)
    {
        var application = WpfApplication.Current;
        if (application is null)
        {
            return;
        }

        var palette = Palettes[Normalize(theme)];
        foreach (var pair in palette.Values)
        {
            var color = ColorFromHex(pair.Value);
            if (pair.Key is "CanvasBrush" or "CanvasDeepBrush" or "RailBrush" or "SurfaceBrush" or "SurfaceRaisedBrush" or "SurfaceStrongBrush")
            {
                color = ScaleBrightness(color, backgroundBrightness);
            }

            if (application.Resources[pair.Key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                application.Resources[pair.Key] = new SolidColorBrush(color);
            }
        }

        application.Resources["PanelCornerRadius"] = new CornerRadius(Math.Clamp(cornerRadius, 8, 24));
        UpdateGlassPanel(application, palette, transparency, backgroundBrightness);
        SetColor(application, "DialogBrush", ScaleBrightness(ColorFromHex(palette.SurfaceStrong), backgroundBrightness));
        SetOpacity(application, "DialogBrush", 1);
        SetOpacity(application, "SurfaceBrush", transparency ? 0.9 : 1);
        SetOpacity(application, "SurfaceRaisedBrush", transparency ? 0.88 : 1);
        SetOpacity(application, "SurfaceStrongBrush", transparency ? 0.94 : 1);
        SetOpacity(application, "GlowOneBrush", dynamicGlow ? 0.18 : 0);
        SetOpacity(application, "GlowTwoBrush", dynamicGlow ? 0.12 : 0);
        if (application.Resources["PanelShadowEffect"] is DropShadowEffect shadow && !shadow.IsFrozen)
        {
            shadow.Color = ColorFromHex(palette.Shadow);
            shadow.Opacity = transparency ? 0.28 : 0.34;
        }
    }

    internal static IReadOnlyList<ThemePreview> Previews { get; } =
        [
            new("violet", "紫夜", "安静、精致", "#0D0F1B", "#B9A6FF", "#1D2135"),
            new("arctic", "冰川", "清晰、冷静", "#09131C", "#8EE7F5", "#173141"),
            new("ember", "琥珀", "温暖、克制", "#160E12", "#F0B56B", "#332021"),
            new("moss", "森林", "柔和、耐看", "#0B1515", "#99E2BF", "#1D3530")
        ];

    private static SolidColorBrush CreateBrush(string hex)
    {
        return new SolidColorBrush(ColorFromHex(hex));
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

    private static void SetOpacity(WpfApplication application, string key, double opacity)
    {
        if (application.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Opacity = opacity;
        }
    }

    private static void SetColor(WpfApplication application, string key, MediaColor color)
    {
        if (application.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
        }
        else
        {
            application.Resources[key] = new SolidColorBrush(color);
        }
    }

    private static void UpdateGlassPanel(WpfApplication application, ThemePalette palette, bool transparency, int backgroundBrightness)
    {
        var glass = new LinearGradientBrush
        {
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(1, 1)
        };
        var alpha = transparency ? (byte)224 : byte.MaxValue;
        glass.GradientStops.Add(new GradientStop(WithAlpha(ScaleBrightness(ColorFromHex(palette.Surface), backgroundBrightness), alpha), 0));
        glass.GradientStops.Add(new GradientStop(WithAlpha(ScaleBrightness(ColorFromHex(palette.SurfaceRaised), backgroundBrightness), transparency ? (byte)214 : byte.MaxValue), 0.55));
        glass.GradientStops.Add(new GradientStop(WithAlpha(ScaleBrightness(ColorFromHex(palette.SurfaceStrong), backgroundBrightness), transparency ? (byte)230 : byte.MaxValue), 1));
        application.Resources["GlassPanelBackground"] = glass;
    }

    private static MediaColor WithAlpha(MediaColor color, byte alpha)
    {
        return MediaColor.FromArgb(alpha, color.R, color.G, color.B);
    }

    private sealed record ThemePalette(
        string Canvas,
        string CanvasDeep,
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
        string Healthy,
        string Warning,
        string Error,
        string GlowOne,
        string GlowTwo,
        string Shadow)
    {
        public IReadOnlyDictionary<string, string> Values { get; } = new Dictionary<string, string>
        {
            ["CanvasBrush"] = Canvas,
            ["CanvasDeepBrush"] = CanvasDeep,
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
            ["BlueBrush"] = Accent,
            ["BlueDeepBrush"] = AccentStrong,
            ["SurfaceBlueBrush"] = AccentSoft,
            ["MintBrush"] = Healthy,
            ["HealthyBrush"] = Healthy,
            ["AmberBrush"] = Warning,
            ["WarningBrush"] = Warning,
            ["RedBrush"] = Error,
            ["ErrorBrush"] = Error,
            ["GlowOneBrush"] = CreateBrushValue(GlowOne),
            ["GlowTwoBrush"] = CreateBrushValue(GlowTwo),
            ["ShadowBrush"] = Shadow
        };

        private static string CreateBrushValue(string value) => value;
    }

    internal sealed record ThemePreview(
        string Id,
        string Name,
        string Description,
        string Background,
        string Accent,
        string Surface);
}
