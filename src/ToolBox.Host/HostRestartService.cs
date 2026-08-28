using System.Diagnostics;
using System.IO;

namespace ToolBox.Host;

internal sealed class HostRestartService
{
    private readonly Func<string?> _getProcessPath;
    private readonly Func<string, bool> _fileExists;
    private readonly Action<ProcessStartInfo> _startProcess;

    public HostRestartService()
        : this(
            () => Environment.ProcessPath,
            File.Exists,
            StartProcess)
    {
    }

    internal HostRestartService(
        Func<string?> getProcessPath,
        Func<string, bool> fileExists,
        Action<ProcessStartInfo> startProcess)
    {
        _getProcessPath = getProcessPath ?? throw new ArgumentNullException(nameof(getProcessPath));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _startProcess = startProcess ?? throw new ArgumentNullException(nameof(startProcess));
    }

    public bool TryGetExecutablePath(out string executablePath)
    {
        executablePath = string.Empty;
        var processPath = _getProcessPath();
        if (string.IsNullOrWhiteSpace(processPath)
            || !Path.IsPathFullyQualified(processPath)
            || !_fileExists(processPath)
            || !processPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        executablePath = processPath;
        return true;
    }

    public void Launch(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _startProcess(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        });
    }

    private static void StartProcess(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The replacement ToolBox process did not start.");
    }
}
