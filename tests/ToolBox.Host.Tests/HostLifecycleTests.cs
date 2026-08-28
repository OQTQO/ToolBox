using System.Diagnostics;
using ToolBox.Host;
using Xunit;

namespace ToolBox.Host.Tests;

public sealed class HostLifecycleTests
{
    [Fact]
    public void ShutdownRequestIsSingleAndCannotBeUpgradedToRestart()
    {
        var state = new HostLifetimeState();

        Assert.True(state.TryRequestShutdown());
        Assert.True(state.IsShutdownRequested);
        Assert.False(state.TryRequestRestart("C:\\ToolBox\\ToolBox.exe"));
        Assert.False(state.TryRequestShutdown());
        Assert.True(state.TryBeginShutdown(out var plan));
        Assert.Equal(HostExitIntent.Shutdown, plan.Intent);
        Assert.Null(plan.RestartExecutablePath);
        Assert.False(state.TryBeginShutdown(out _));
    }

    [Fact]
    public void RestartRequestPreservesExecutableAndRunsOnlyOnce()
    {
        var state = new HostLifetimeState();

        Assert.True(state.TryRequestRestart("C:\\ToolBox\\ToolBox.exe"));
        Assert.False(state.TryRequestRestart("C:\\Other\\ToolBox.exe"));
        Assert.True(state.TryBeginShutdown(out var plan));
        Assert.Equal(HostExitIntent.Restart, plan.Intent);
        Assert.Equal("C:\\ToolBox\\ToolBox.exe", plan.RestartExecutablePath);
        Assert.False(state.TryBeginShutdown(out _));
    }

    [Fact]
    public void UnrequestedApplicationExitDefaultsToShutdown()
    {
        var state = new HostLifetimeState();

        Assert.True(state.TryBeginShutdown(out var plan));

        Assert.Equal(HostExitIntent.Shutdown, plan.Intent);
        Assert.Null(plan.RestartExecutablePath);
        Assert.True(state.IsShutdownRequested);
    }

    [Fact]
    public void ShutdownPipelineKeepsOrderIsolatesFailuresAndIsIdempotent()
    {
        var events = new List<string>();
        var reportedFailures = new List<HostShutdownFailure>();
        var coordinator = new HostShutdownCoordinator(
        [
            new("plugins", _ => events.Add("plugins")),
            new("tray", _ =>
            {
                events.Add("tray");
                throw new InvalidOperationException("simulated tray failure");
            }),
            new("logger", _ => events.Add("logger")),
            new("installer", _ => events.Add("installer")),
            new("restart", plan => events.Add(plan.Intent == HostExitIntent.Restart ? "restart" : "no-restart"))
        ],
        failure => reportedFailures.Add(failure));
        var plan = new HostExitPlan(HostExitIntent.Restart, "C:\\ToolBox\\ToolBox.exe");

        var first = coordinator.Run(plan);
        var second = coordinator.Run(plan);

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.Equal(["plugins", "tray", "logger", "installer", "restart"], events);
        var failure = Assert.Single(first.Failures);
        Assert.Equal("tray", failure.OperationName);
        Assert.Same(failure, Assert.Single(reportedFailures));
        Assert.Empty(second.Failures);
    }

    [Fact]
    public void FailureReporterCannotInterruptCleanup()
    {
        var events = new List<string>();
        var coordinator = new HostShutdownCoordinator(
        [
            new("first", _ => throw new InvalidOperationException("first failed")),
            new("second", _ => events.Add("second"))
        ],
        _ => throw new InvalidOperationException("reporting failed"));

        var result = coordinator.Run(new HostExitPlan(HostExitIntent.Shutdown, null));

        Assert.True(result.Started);
        Assert.Single(result.Failures);
        Assert.Equal(["second"], events);
    }

    [Fact]
    public void DefaultShutdownPipelineLocksResourceReleaseOrder()
    {
        var events = new List<string>();
        var coordinator = HostShutdownCoordinator.CreateDefault(
            new HostShutdownActions(
                () => events.Add("diagnostics-stopping"),
                () => events.Add("log-started"),
                () => events.Add("plugins"),
                () => events.Add("tray"),
                () => events.Add("diagnostics-stopped"),
                () => events.Add("log-completed"),
                () => events.Add("logger"),
                () => events.Add("installer"),
                _ => events.Add("restart")),
            _ => throw new InvalidOperationException("No failure was expected."));

        var result = coordinator.Run(new HostExitPlan(HostExitIntent.Shutdown, null));

        Assert.True(result.Started);
        Assert.Empty(result.Failures);
        Assert.Equal(
        [
            "diagnostics-stopping",
            "log-started",
            "plugins",
            "tray",
            "diagnostics-stopped",
            "log-completed",
            "logger",
            "installer"
        ],
        events);
    }

    [Fact]
    public void DefaultShutdownPipelineLaunchesReplacementOnlyForRestart()
    {
        var launchedPaths = new List<string>();
        var noOp = () => { };
        var actions = new HostShutdownActions(
            noOp,
            noOp,
            noOp,
            noOp,
            noOp,
            noOp,
            noOp,
            noOp,
            path => launchedPaths.Add(path));

        HostShutdownCoordinator.CreateDefault(actions, _ => { })
            .Run(new HostExitPlan(HostExitIntent.Shutdown, null));
        Assert.Empty(launchedPaths);

        HostShutdownCoordinator.CreateDefault(actions, _ => { })
            .Run(new HostExitPlan(HostExitIntent.Restart, "C:\\ToolBox\\ToolBox.exe"));
        Assert.Equal(["C:\\ToolBox\\ToolBox.exe"], launchedPaths);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("", false, false)]
    [InlineData("ToolBox.exe", true, false)]
    [InlineData("C:\\Program Files\\dotnet.exe", true, false)]
    [InlineData("C:\\ToolBox\\ToolBox.dll", true, false)]
    [InlineData("C:\\ToolBox\\ToolBox.exe", false, false)]
    [InlineData("C:\\ToolBox\\ToolBox.exe", true, true)]
    public void RestartExecutableResolutionRejectsUnsafeHosts(
        string? processPath,
        bool fileExists,
        bool expected)
    {
        var service = new HostRestartService(
            () => processPath,
            _ => fileExists,
            _ => throw new InvalidOperationException("Launch should not be called."));

        var actual = service.TryGetExecutablePath(out var executablePath);

        Assert.Equal(expected, actual);
        Assert.Equal(expected ? processPath : string.Empty, executablePath);
    }

    [Fact]
    public void RestartLaunchUsesExactExecutableAndWorkingDirectory()
    {
        ProcessStartInfo? captured = null;
        var service = new HostRestartService(
            () => "C:\\ToolBox\\ToolBox.exe",
            _ => true,
            startInfo => captured = startInfo);

        service.Launch("C:\\ToolBox\\ToolBox.exe");

        Assert.NotNull(captured);
        Assert.Equal("C:\\ToolBox\\ToolBox.exe", captured.FileName);
        Assert.Equal("C:\\ToolBox", captured.WorkingDirectory);
        Assert.True(captured.UseShellExecute);
    }
}
