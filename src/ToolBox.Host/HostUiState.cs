namespace ToolBox.Host;

internal static class HostUiState
{
    internal static class PluginFilters
    {
        public const string All = "all";
        public const string Running = "running";
        public const string Disabled = "disabled";
        public const string Attention = "attention";

        public static bool IsKnown(string? value) => value is All or Running or Disabled or Attention;
    }

    internal static class PluginSorts
    {
        public const string Name = "name";
        public const string Status = "status";
        public const string Version = "version";

        public static bool IsKnown(string? value) => value is Name or Status or Version;
    }

    internal static class CardSizes
    {
        // These values remain valid for legacy settings JSON only. The current
        // Host does not expose a card-size switch and always renders the
        // information-rich featured card treatment.
        public const string Compact = "compact";
        public const string Standard = "standard";
        public const string Featured = "featured";

        public static bool IsKnown(string? value) => value is Compact or Standard or Featured;
    }

    internal static class PluginDetailsTabs
    {
        public const string Overview = "overview";
        public const string Operations = "operations";
        public const string Logs = "logs";
        public const string About = "about";

        public static bool IsKnown(string? value) => value is Overview or Operations or Logs or About;

        public static string GetDefault(bool hasPluginUi) => hasPluginUi
            ? Operations
            : Overview;
    }

    internal static class SettingsSections
    {
        public const string Appearance = "appearance";
        public const string Plugins = "plugins";
        public const string Runtime = "runtime";
        public const string About = "about";
    }
}
