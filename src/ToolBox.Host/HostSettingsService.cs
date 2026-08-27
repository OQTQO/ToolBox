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
    private const int CurrentSchemaVersion = 1;
    private const string SettingsFileName = "ui-settings.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly Dictionary<string, bool> _openedPlugins;

    public HostSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox",
            SettingsFileName);

        var document = Load();
        Language = ParseLanguage(document.Language);
        CloseBehavior = ParseCloseBehavior(document.CloseBehavior);
        _openedPlugins = new Dictionary<string, bool>(
            document.OpenedPlugins ?? new Dictionary<string, bool>(),
            StringComparer.Ordinal);
    }

    public event EventHandler? Changed;

    public AppLanguage Language { get; private set; }

    public CloseBehavior CloseBehavior { get; private set; }

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
                new Dictionary<string, bool>(_openedPlugins, StringComparer.Ordinal));
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

    private sealed record HostSettingsDocument(
        int SchemaVersion,
        string? Language,
        string? CloseBehavior,
        Dictionary<string, bool>? OpenedPlugins)
    {
        public static HostSettingsDocument Default { get; } = new(
            CurrentSchemaVersion,
            Language: null,
            CloseBehavior: null,
            OpenedPlugins: null);
    }
}
