namespace ToolBox.Core.Diagnostics;

public sealed class LoggerOptions
{
    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;

    public string DirectoryPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolBox",
        "Logs");

    public long MaxFileBytes { get; init; } = 2 * 1024 * 1024;

    public int MaxFiles { get; init; } = 10;

    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(7);
}
