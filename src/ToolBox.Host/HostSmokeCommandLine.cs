using System.IO;
using System.Text.Json;

namespace ToolBox.Host;

internal sealed record HostSmokeCommand(
    IReadOnlyList<string> PackagePaths,
    string WorkerPath,
    string WorkingRoot,
    string ResultPath);

internal sealed record HostSmokePackageResult(
    string PackagePath,
    string PluginId,
    string Version,
    bool Installed,
    bool Enabled,
    bool Disabled,
    bool Uninstalled,
    string? Error);

internal sealed record HostSmokeResult(
    bool Success,
    string HostVersion,
    string WorkerPath,
    IReadOnlyList<HostSmokePackageResult> Packages,
    string? Error);

internal static class HostSmokeCommandLine
{
    private const string PackageOption = "--smoke-test-package";
    private const string WorkerOption = "--smoke-test-worker";
    private const string RootOption = "--smoke-test-root";
    private const string ResultOption = "--smoke-test-result";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool IsRequested(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return arguments.Any(argument => string.Equals(
            argument,
            PackageOption,
            StringComparison.Ordinal));
    }

    public static int Execute(IReadOnlyList<string> arguments)
    {
        HostSmokeCommand? command = null;
        HostSmokeResult result;

        try
        {
            command = Parse(arguments);
            result = HostPackageSmokeRunner.RunAsync(command).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            result = new HostSmokeResult(
                false,
                GetHostVersion(),
                command?.WorkerPath ?? string.Empty,
                [],
                exception.Message);
        }

        var resultPath = command?.ResultPath ?? TryReadOption(arguments, ResultOption);
        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            try
            {
                WriteResult(resultPath, result);
            }
            catch
            {
                return 3;
            }
        }

        return result.Success ? 0 : 2;
    }

    internal static HostSmokeCommand Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var packages = new List<string>();
        string? workerPath = null;
        string? workingRoot = null;
        string? resultPath = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (index + 1 >= arguments.Count)
            {
                throw new ArgumentException($"Smoke-test option '{option}' is missing its value.");
            }

            var value = arguments[++index];
            switch (option)
            {
                case PackageOption:
                    packages.Add(Path.GetFullPath(value));
                    break;
                case WorkerOption:
                    workerPath = Path.GetFullPath(value);
                    break;
                case RootOption:
                    workingRoot = Path.GetFullPath(value);
                    break;
                case ResultOption:
                    resultPath = Path.GetFullPath(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown smoke-test option '{option}'.");
            }
        }

        if (packages.Count == 0)
        {
            throw new ArgumentException("At least one --smoke-test-package value is required.");
        }

        workerPath ??= Path.Combine(AppContext.BaseDirectory, "ToolBox.PluginWorker.exe");
        workingRoot ??= Path.Combine(
            Path.GetTempPath(),
            "ToolBox.Host.Smoke",
            Guid.NewGuid().ToString("N"));
        resultPath ??= Path.Combine(workingRoot, "result.json");

        return new HostSmokeCommand(packages, workerPath, workingRoot, resultPath);
    }

    internal static string GetHostVersion()
    {
        return typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static string? TryReadOption(IReadOnlyList<string> arguments, string option)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.Ordinal))
            {
                return Path.GetFullPath(arguments[index + 1]);
            }
        }

        return null;
    }

    private static void WriteResult(string path, HostSmokeResult result)
    {
        var fullPath = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The smoke-test result path has no parent directory.");
        Directory.CreateDirectory(parent);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(result, JsonOptions));
    }
}
