using System.Diagnostics;
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
    private const int CurrentSchemaVersion = 4;
    private const string SettingsFileName = "ui-settings.json";
    internal const string DefaultTheme = "field";
    internal const string DefaultCardSize = "standard";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _settingsPath;
    private readonly Dictionary<string, bool> _openedPlugins;
    private readonly Dictionary<string, string> _pluginCardSizes;
    private AppLanguage _language;
    private CloseBehavior _closeBehavior;
    private string _theme = DefaultTheme;
    private string? _overviewTitle;
    private string? _overviewHeroTitle;
    private string? _overviewHealthTitle;
    private string? _titleBarCenterText;
    private string _defaultPluginCardSize = DefaultCardSize;
    private bool _dynamicGlow = true;
    private bool _reduceMotion;
    private bool _transparency = true;
    private int _cornerRadius = 16;
    private int _backgroundBrightness = 100;
    private bool _confirmEnable;
    private bool _confirmUninstall = true;
    private bool _showDiagnostics;

    public HostSettingsService(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolBox",
            SettingsFileName);

        var document = Load();
        _language = ParseLanguage(document.Language);
        _closeBehavior = ParseCloseBehavior(document.CloseBehavior);
        _theme = NormalizeTheme(document.Theme);
        _overviewTitle = NormalizeOverviewTitle(document.OverviewTitle);
        _overviewHeroTitle = NormalizeCustomText(document.OverviewHeroTitle, 80);
        _overviewHealthTitle = NormalizeCustomText(document.OverviewHealthTitle, 80);
        _titleBarCenterText = NormalizeSingleLineText(document.TitleBarCenterText, 32);
        _defaultPluginCardSize = NormalizeCardSize(document.DefaultPluginCardSize);
        _dynamicGlow = document.DynamicGlow ?? true;
        _reduceMotion = document.ReduceMotion ?? false;
        _transparency = document.Transparency ?? true;
        _cornerRadius = Math.Clamp(document.CornerRadius ?? 16, 12, 20);
        _backgroundBrightness = Math.Clamp(document.BackgroundBrightness ?? 100, 75, 125);
        _confirmEnable = document.ConfirmEnable ?? false;
        _confirmUninstall = document.ConfirmUninstall ?? true;
        _showDiagnostics = document.ShowDiagnostics ?? false;
        _openedPlugins = new Dictionary<string, bool>(
            document.OpenedPlugins ?? new Dictionary<string, bool>(),
            StringComparer.Ordinal);
        _pluginCardSizes = new Dictionary<string, string>(
            document.PluginCardSizes ?? new Dictionary<string, string>(),
            StringComparer.Ordinal);
    }

    public event EventHandler? Changed;

    public AppLanguage Language
    {
        get
        {
            lock (_gate)
            {
                return _language;
            }
        }
    }

    public CloseBehavior CloseBehavior
    {
        get
        {
            lock (_gate)
            {
                return _closeBehavior;
            }
        }
    }

    public string Theme
    {
        get
        {
            lock (_gate)
            {
                return _theme;
            }
        }
    }

    public string? OverviewTitle
    {
        get
        {
            lock (_gate)
            {
                return _overviewTitle;
            }
        }
    }

    public string? OverviewHeroTitle
    {
        get
        {
            lock (_gate)
            {
                return _overviewHeroTitle;
            }
        }
    }

    public string? OverviewHealthTitle
    {
        get
        {
            lock (_gate)
            {
                return _overviewHealthTitle;
            }
        }
    }

    public string? TitleBarCenterText
    {
        get
        {
            lock (_gate)
            {
                return _titleBarCenterText;
            }
        }
    }

    public string DefaultPluginCardSize
    {
        get
        {
            lock (_gate)
            {
                return _defaultPluginCardSize;
            }
        }
    }

    public bool DynamicGlow
    {
        get
        {
            lock (_gate)
            {
                return _dynamicGlow;
            }
        }
    }

    public bool ReduceMotion
    {
        get
        {
            lock (_gate)
            {
                return _reduceMotion;
            }
        }
    }

    public bool Transparency
    {
        get
        {
            lock (_gate)
            {
                return _transparency;
            }
        }
    }

    public int CornerRadius
    {
        get
        {
            lock (_gate)
            {
                return _cornerRadius;
            }
        }
    }

    public int BackgroundBrightness
    {
        get
        {
            lock (_gate)
            {
                return _backgroundBrightness;
            }
        }
    }

    public bool ConfirmEnable
    {
        get
        {
            lock (_gate)
            {
                return _confirmEnable;
            }
        }
    }

    public bool ConfirmUninstall
    {
        get
        {
            lock (_gate)
            {
                return _confirmUninstall;
            }
        }
    }

    public bool ShowDiagnostics
    {
        get
        {
            lock (_gate)
            {
                return _showDiagnostics;
            }
        }
    }

    public void SetLanguage(AppLanguage language)
    {
        Update(() =>
        {
            if (_language == language)
            {
                return false;
            }

            _language = language;
            return true;
        });
    }

    public void SetCloseBehavior(CloseBehavior behavior)
    {
        Update(() =>
        {
            if (_closeBehavior == behavior)
            {
                return false;
            }

            _closeBehavior = behavior;
            return true;
        });
    }

    public void SetTheme(string theme)
    {
        var normalized = NormalizeTheme(theme);
        Update(() =>
        {
            if (_theme == normalized)
            {
                return false;
            }

            _theme = normalized;
            return true;
        });
    }

    public void SetOverviewTitle(string? title)
    {
        var normalized = NormalizeOverviewTitle(title);
        Update(() =>
        {
            if (string.Equals(_overviewTitle, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            _overviewTitle = normalized;
            return true;
        });
    }

    public void SetOverviewHeroTitle(string? title)
    {
        var normalized = NormalizeCustomText(title, 80);
        Update(() =>
        {
            if (string.Equals(_overviewHeroTitle, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            _overviewHeroTitle = normalized;
            return true;
        });
    }

    public void SetOverviewHealthTitle(string? title)
    {
        var normalized = NormalizeCustomText(title, 80);
        Update(() =>
        {
            if (string.Equals(_overviewHealthTitle, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            _overviewHealthTitle = normalized;
            return true;
        });
    }

    public void SetTitleBarCenterText(string? text)
    {
        var normalized = NormalizeSingleLineText(text, 32);
        Update(() =>
        {
            if (string.Equals(_titleBarCenterText, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            _titleBarCenterText = normalized;
            return true;
        });
    }

    public void SetDefaultPluginCardSize(string size)
    {
        var normalized = NormalizeCardSize(size);
        Update(() =>
        {
            if (_defaultPluginCardSize == normalized)
            {
                return false;
            }

            _defaultPluginCardSize = normalized;
            return true;
        });
    }

    public string GetPluginCardSize(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_gate)
        {
            return _pluginCardSizes.TryGetValue(pluginId, out var size)
                ? NormalizeCardSize(size)
                : _defaultPluginCardSize;
        }
    }

    public bool HasPluginCardSizeOverride(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_gate)
        {
            return _pluginCardSizes.ContainsKey(pluginId);
        }
    }

    public void SetPluginCardSize(string pluginId, string size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var normalized = NormalizeCardSize(size);
        Update(() =>
        {
            if (_pluginCardSizes.TryGetValue(pluginId, out var existing)
                && string.Equals(existing, normalized, StringComparison.Ordinal))
            {
                return false;
            }

            _pluginCardSizes[pluginId] = normalized;
            return true;
        });
    }

    public void ClearPluginCardSize(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        Update(() => _pluginCardSizes.Remove(pluginId));
    }

    public void SetAppearanceOption(
        bool? dynamicGlow = null,
        bool? reduceMotion = null,
        bool? transparency = null,
        int? cornerRadius = null,
        int? backgroundBrightness = null)
    {
        Update(() =>
        {
            var changed = false;
            if (dynamicGlow.HasValue && _dynamicGlow != dynamicGlow.Value)
            {
                _dynamicGlow = dynamicGlow.Value;
                changed = true;
            }

            if (reduceMotion.HasValue && _reduceMotion != reduceMotion.Value)
            {
                _reduceMotion = reduceMotion.Value;
                changed = true;
            }

            if (transparency.HasValue && _transparency != transparency.Value)
            {
                _transparency = transparency.Value;
                changed = true;
            }

            var normalizedCornerRadius = cornerRadius.HasValue
                ? Math.Clamp(cornerRadius.Value, 12, 20)
                : _cornerRadius;
            if (_cornerRadius != normalizedCornerRadius)
            {
                _cornerRadius = normalizedCornerRadius;
                changed = true;
            }

            var normalizedBrightness = backgroundBrightness.HasValue
                ? Math.Clamp(backgroundBrightness.Value, 75, 125)
                : _backgroundBrightness;
            if (_backgroundBrightness != normalizedBrightness)
            {
                _backgroundBrightness = normalizedBrightness;
                changed = true;
            }

            return changed;
        });
    }

    public void SetPluginManagementOption(bool? confirmEnable = null, bool? confirmUninstall = null, bool? showDiagnostics = null)
    {
        Update(() =>
        {
            var changed = false;
            if (confirmEnable.HasValue && _confirmEnable != confirmEnable.Value)
            {
                _confirmEnable = confirmEnable.Value;
                changed = true;
            }

            if (confirmUninstall.HasValue && _confirmUninstall != confirmUninstall.Value)
            {
                _confirmUninstall = confirmUninstall.Value;
                changed = true;
            }

            if (showDiagnostics.HasValue && _showDiagnostics != showDiagnostics.Value)
            {
                _showDiagnostics = showDiagnostics.Value;
                changed = true;
            }

            return changed;
        });
    }

    public void ResetAppearance()
    {
        Update(() =>
        {
            var changed = _theme != DefaultTheme
                || _overviewTitle is not null
                || _overviewHeroTitle is not null
                || _overviewHealthTitle is not null
                || _titleBarCenterText is not null
                || _defaultPluginCardSize != DefaultCardSize
                || !_dynamicGlow
                || _reduceMotion
                || !_transparency
                || _cornerRadius != 16
                || _backgroundBrightness != 100;

            _theme = DefaultTheme;
            _overviewTitle = null;
            _overviewHeroTitle = null;
            _overviewHealthTitle = null;
            _titleBarCenterText = null;
            _defaultPluginCardSize = DefaultCardSize;
            _dynamicGlow = true;
            _reduceMotion = false;
            _transparency = true;
            _cornerRadius = 16;
            _backgroundBrightness = 100;
            return changed;
        });
    }

    public bool IsPluginOpened(string pluginId, bool defaultValue = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        lock (_gate)
        {
            return _openedPlugins.TryGetValue(pluginId, out var opened) ? opened : defaultValue;
        }
    }

    public void SetPluginOpened(string pluginId, bool opened)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        Update(() =>
        {
            if (_openedPlugins.TryGetValue(pluginId, out var existing) && existing == opened)
            {
                return false;
            }

            _openedPlugins[pluginId] = opened;
            return true;
        });
    }

    public void RemovePlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        Update(() => _openedPlugins.Remove(pluginId));
    }

    private void Update(Func<bool> mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var changed = false;
        lock (_gate)
        {
            if (!mutation())
            {
                return;
            }

            PersistLocked();
            changed = true;
        }

        if (changed)
        {
            RaiseChangedSafely();
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

    private void PersistLocked()
    {
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("The host settings path has no parent directory.");
            Directory.CreateDirectory(directory);

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_settingsPath)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       options: FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, CreateDocumentLocked(), JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_settingsPath))
            {
                try
                {
                    File.Replace(temporaryPath, _settingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Move(temporaryPath, _settingsPath, overwrite: true);
                }
                catch (NotSupportedException)
                {
                    File.Move(temporaryPath, _settingsPath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }

            temporaryPath = null;
        }
        catch (IOException)
        {
            // In-memory preferences remain usable when the settings directory is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // In-memory preferences remain usable when the settings directory is read-only.
        }
        catch (ArgumentException)
        {
            // A malformed custom path must not prevent the Host from starting.
        }
        catch (NotSupportedException)
        {
            // Unsupported filesystem semantics leave the in-memory state usable.
        }
        catch (InvalidOperationException)
        {
            // An invalid custom path leaves the in-memory state usable.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Best-effort cleanup; the unique name prevents future collisions.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup; the settings value is still valid in memory.
                }
            }
        }
    }

    private HostSettingsDocument CreateDocumentLocked()
    {
        return new HostSettingsDocument(
            CurrentSchemaVersion,
            _language.ToString(),
            _closeBehavior.ToString(),
            new Dictionary<string, bool>(_openedPlugins, StringComparer.Ordinal),
            _theme,
            _overviewTitle,
            _overviewHeroTitle,
            _overviewHealthTitle,
            _titleBarCenterText,
            _defaultPluginCardSize,
            _dynamicGlow,
            _reduceMotion,
            _transparency,
            _cornerRadius,
            _backgroundBrightness,
            _confirmEnable,
            _confirmUninstall,
            _showDiagnostics,
            new Dictionary<string, string>(_pluginCardSizes, StringComparer.Ordinal));
    }

    private void RaiseChangedSafely()
    {
        var handlers = Changed?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.OfType<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"ToolBox settings change notification failed: {exception}");
            }
        }
    }

    private static AppLanguage ParseLanguage(string? value)
    {
        if (Enum.TryParse<AppLanguage>(value, ignoreCase: true, out var language)
            && Enum.IsDefined(language))
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
               && Enum.IsDefined(behavior)
            ? behavior
            : CloseBehavior.MinimizeToTray;
    }

    private static string NormalizeTheme(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "field" => "field",
            "arctic" => "arctic",
            "ember" => "ember",
            "moss" or "forest" => "moss",
            _ => DefaultTheme
        };
    }

    private static string? NormalizeOverviewTitle(string? value)
    {
        return NormalizeCustomText(value, 28);
    }

    private static string? NormalizeCustomText(string? value, int maxLength)
    {
        var normalized = value?
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return TruncateUnicode(normalized, maxLength);
    }

    private static string? NormalizeSingleLineText(string? value, int maxLength)
    {
        var normalized = value?
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        normalized = string.Join(
            " ",
            normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return TruncateUnicode(normalized, maxLength);
    }

    private static string TruncateUnicode(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = maxLength;
        if (length > 0
            && length < value.Length
            && char.IsHighSurrogate(value[length - 1])
            && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return value[..length];
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
        string? OverviewHeroTitle = null,
        string? OverviewHealthTitle = null,
        string? TitleBarCenterText = null,
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
