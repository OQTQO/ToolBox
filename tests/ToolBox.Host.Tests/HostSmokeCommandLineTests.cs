using System.Text.Json;
using ToolBox.Host;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class HostSmokeCommandLineTests
{
    [Fact]
    public void NormalHostLaunchDoesNotEnterSmokeMode()
    {
        Assert.False(HostSmokeCommandLine.IsRequested([]));
        Assert.False(HostSmokeCommandLine.IsRequested(["--unrelated"]));
    }

    [Fact]
    public void ParserAcceptsMultiplePackagesAndNormalizesPaths()
    {
        var command = HostSmokeCommandLine.Parse([
            "--smoke-test-package", "first.tpk",
            "--smoke-test-worker", "worker.exe",
            "--smoke-test-package", "second.tpk",
            "--smoke-test-root", "smoke-root",
            "--smoke-test-result", "result.json"
        ]);

        Assert.Equal(
            [Path.GetFullPath("first.tpk"), Path.GetFullPath("second.tpk")],
            command.PackagePaths);
        Assert.Equal(Path.GetFullPath("worker.exe"), command.WorkerPath);
        Assert.Equal(Path.GetFullPath("smoke-root"), command.WorkingRoot);
        Assert.Equal(Path.GetFullPath("result.json"), command.ResultPath);
    }

    [Fact]
    public void InvalidSmokeCommandWritesMachineReadableFailure()
    {
        var resultPath = Path.Combine(
            Path.GetTempPath(),
            "ToolBox.Host.Smoke.Tests",
            Guid.NewGuid().ToString("N"),
            "result.json");

        try
        {
            var exitCode = HostSmokeCommandLine.Execute([
                "--smoke-test-package", "plugin.tpk",
                "--smoke-test-result", resultPath,
                "--unknown", "value"
            ]);

            Assert.Equal(2, exitCode);
            using var document = JsonDocument.Parse(File.ReadAllText(resultPath));
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains(
                "Unknown smoke-test option",
                document.RootElement.GetProperty("error").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            var parent = Path.GetDirectoryName(resultPath);
            if (parent is not null && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }
}
