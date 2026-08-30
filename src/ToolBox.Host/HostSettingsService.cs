using System.Globalization;
using System.IO;
using System.Text.Json;

namespace ToolBox.Host;

internal enum CloseBehavior
{
    MinimizeToTray,
    Exit
}

internal sealed class HostSettingsService
{
    private const int CurrentSchemaVersion = 2;
    private const string SettingsFileName = "ui-settings.json";
    internal const string DefaultTheme = "violet";
    internal const string DefaultCardSize = "standard";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly Dictionary<string, bool> _openedPlugins;
    private readonly Dictionary<string, string> _pluginCardSizes;

    public HostSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox",
            SettingsFileName);

        var document = Load();
        Language = ParseLanguage(document.Language);
        CloseBehavior = ParseCloseBehavior(document.CloseBehavior);
        Theme = NormalizeTheme(document.Theme);
        OverviewTitle = NormalizeOverviewTitle(document.OverviewTitle);
        DefaultPluginCardSize = NormalizeCardSize(document.DefaultPluginCardSize);
        DynamicGlow = document.DynamicGlow ?? true;
        ReduceMotion = document.ReduceMotion ?? false;
        Transparency = document.Transparency ?? true;
        CornerRadius = Math.Clamp(document.CornerRadius ?? 14, 8, 24);
        BackgroundBrightness = Math.Clamp(document.BackgroundBrightness ?? 100, 75, 125);
        ConfirmEnable = document.ConfirmEnable ?? false;
        ConfirmUninstall = document.ConfirmUninstall ?? true;
        ShowDiagnostics = document.ShowDiagnostics ?? false;
        _openedPlugins = new Dictionary<string, bool>(
            document.OpenedPlugins ?? new Dictionary<string, bool>(),
            StringComparer.Ordinal);
        _pluginCardSizes = new Dictionary<string, string>(
            document.PluginCardSizes ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
    }

    public event EventHandler? Changed;

    public AppLanguage Language { get; private set; }

    public CloseBehavior CloseBehavior { get; private set; }

    public string Theme { get; private set; } = DefaultTheme;

    public string? OverviewTitle { get; private set; }

    public string DefaultPluginCardSize { get; private set; } = DefaultCardSize;

    public bool DynamicGlow { get; private set; } = true;

    public bool ReduceMotion { get; private set; }

    public bool Transparency { get; private set; } = true;

    public int CornerRadius { get; private set; } = 14;

    public int BackgroundBrightness { get; private set; } = 100;

    public bool ConfirmEnable { get; private set; }

    public bool ConfirmUninstall { get; private set; } = true;

    public bool ShowDiagnostics { get; private set; }

    public void SetLanguage(AppLanguage language)
    {
        if (Language == language)
        {
            return;
        }

        Language = language;
        SaveAndNotify();
    }

    public void SetCloseBehavior(CloseBehavior behavior)
    {
        if (CloseBehavior == behavior)
        {
            return;
        }

        CloseBehavior = behavior;
        SaveAndNotify();
    }

    public void SetTheme(string theme)
    {
        var normalized = NormalizeTheme(theme);
        if (Theme == normalized)
        {
            return;
        }

        Theme = normalized;
        SaveAndNotify();
    }

    public void SetOverviewTitle(string? title)
    {
        var normalized = NormalizeOverviewTitle(title);
        if (string.Equals(OverviewTitle, normalized, StringComparison.Ordinal))
        {
            return;
        }

        OverviewTitle = normalized;
        SaveAndNotify();
    }

    public void SetDefaultPluginCardSize(string size)
    {
        var normalized = NormalizeCardSize(size);
        if (DefaultPluginCardSize == normalized)
        {
            return;
        }

        DefaultPluginCardSize = normalized;
        SaveAndNotify();
    }

    public string GetPluginCardSize(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return _pluginCardSizes.TryGetValue(pluginId, out var size)
            ? NormalizeCardSize(size)
            : DefaultPluginCardSize;
    }

    public bool HasPluginCardSizeOverride(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return _pluginCardSizes.ContainsKey(pluginId);
    }

    public void SetPluginCardSize(string pluginId, string size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalized = NormalizeCardSize(size);
        if (_pluginCardSizes.TryGetValue(pluginId, out var existing)
            && string.Equals(existing, normalized, StringComparison.Ordinal))
        {
            return;
        }

        _pluginCardSizes[pluginId] = normalized;
        SaveAndNotify();
    }

    public void ClearPluginCardSize(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (_pluginCardSizes.Remove(pluginId))
        {
            SaveAndNotify();
        }
    }

    public void SetAppearanceOption(
        bool? dynamicGlow = null,
        bool? reduceMotion = null,
        bool? transparency = null,
        int? cornerRadius = null,
        int? backgroundBrightness = null)
    {
        DynamicGlow = dynamicGlow ?? DynamicGlow;
        ReduceMotion = reduceMotion ?? ReduceMotion;
        Transparency = transparency ?? Transparency;
        CornerRadius = Math.Clamp(cornerRadius ?? CornerRadius, 8, 24);
        BackgroundBrightness = Math.Clamp(backgroundBrightness ?? BackgroundBrightness, 75, 125);
        SaveAndNotify();
    }

    public void SetPluginManagementOption(bool? confirmEnable = null, bool? confirmUninstall = null, bool? showDiagnostics = null)
    {
        ConfirmEnable = confirmEnable ?? ConfirmEnable;
        ConfirmUninstall = confirmUninstall ?? ConfirmUninstall;
        ShowDiagnostics = showDiagnostics ?? ShowDiagnostics;
        SaveAndNotify();
    }

    public void ResetAppearance()
    {
        Theme = DefaultTheme;
        OverviewTitle = null;
        DefaultPluginCardSize = DefaultCardSize;
        DynamicGlow = true;
        ReduceMotion = false;
        Transparency = true;
        CornerRadius = 14;
        BackgroundBrightness = 100;
        SaveAndNotify();
    }

    public bool IsPluginOpened(string pluginId, bool defaultValue = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return _openedPlugins.TryGetValue(pluginId, out var opened) ? opened : defaultValue;
    }

    public void SetPluginOpened(string pluginId, bool opened)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (_openedPlugins.TryGetValue(pluginId, out var existing) && existing == opened)
        {
            return;
        }

        _openedPlugins[pluginId] = opened;
        SaveAndNotify();
    }

    public void RemovePlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (_openedPlugins.Remove(pluginId))
        {
            SaveAndNotify();
        }
    }

    private HostSettingsDocument Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return HostSettingsDocument.Default;
            }

            return JsonSerializer.Deserialize<HostSettingsDocument>(
                       File.ReadAllText(_settingsPath),
                       JsonOptions)
                   ?? HostSettingsDocument.Default;
        }
        catch (IOException)
        {
            return HostSettingsDocument.Default;
        }
        catch (JsonException)
        {
            return HostSettingsDocument.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return HostSettingsDocument.Default;
        }
    }

    private void SaveAndNotify()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("The host settings path has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporaryPath = _settingsPath + ".tmp";
            var document = new HostSettingsDocument(
                CurrentSchemaVersion,
                Language.ToString(),
                CloseBehavior.ToString(),
                new Dictionary<string, bool>(_openedPlugins, StringComparer.Ordinal),
                Theme,
                OverviewTitle,
                DefaultPluginCardSize,
                DynamicGlow,
                ReduceMotion,
                Transparency,
                CornerRadius,
                BackgroundBrightness,
                ConfirmEnable,
                ConfirmUninstall,
                ShowDiagnostics,
                new Dictionary<string, string>(_pluginCardSizes, StringComparer.Ordinal));
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch (IOException)
        {
            // In-memory preferences remain usable when the settings directory is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // In-memory preferences remain usable when the settings directory is read-only.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppLanguage ParseLanguage(string? value)
    {
        if (Enum.TryParse<AppLanguage>(value, ignoreCase: true, out var language))
        {
            return language;
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? AppLanguage.Chinese
            : AppLanguage.English;
    }

    private static CloseBehavior ParseCloseBehavior(string? value)
    {
        return Enum.TryParse<CloseBehavior>(value, ignoreCase: true, out var behavior)
            ? behavior
            : CloseBehavior.MinimizeToTray;
    }

    private static string NormalizeTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "arctic" => "arctic",
            "ember" => "ember",
            "moss" or "forest" => "moss",
            _ => DefaultTheme
        };
    }

    private static string? NormalizeOverviewTitle(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized[..Math.Min(normalized.Length, 28)];
    }

    private static string NormalizeCardSize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "compact" => "compact",
            "featured" => "featured",
            _ => DefaultCardSize
        };
    }

    private sealed record HostSettingsDocument(
        int SchemaVersion,
        string? Language,
        string? CloseBehavior,
        Dictionary<string, bool>? OpenedPlugins,
        string? Theme = null,
        string? OverviewTitle = null,
        string? DefaultPluginCardSize = null,
        bool? DynamicGlow = null,
        bool? ReduceMotion = null,
        bool? Transparency = null,
        int? CornerRadius = null,
        int? BackgroundBrightness = null,
        bool? ConfirmEnable = null,
        bool? ConfirmUninstall = null,
        bool? ShowDiagnostics = null,
        Dictionary<string, string>? PluginCardSizes = null)
    {
        public static HostSettingsDocument Default { get; } = new(
            CurrentSchemaVersion,
            Language: null,
            CloseBehavior: null,
            OpenedPlugins: null);
    }
}
