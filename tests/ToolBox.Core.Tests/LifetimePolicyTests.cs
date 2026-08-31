using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class LifetimePolicyTests
{
    [Fact]
    public async Task LifetimeScopeCancelsTracksAndCleansResourcesInReverseOrder()
    {
        var cleanupOrder = new List<string>();
        var backgroundCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var scope = new PluginLifetimeScope();
        using var callbackRegistration = scope.Register(_ =>
        {
            cleanupOrder.Add("callback");
            backgroundCompleted.TrySetResult();
            return ValueTask.CompletedTask;
        });
        using var disposableRegistration = scope.Register(
            new RecordingDisposable(cleanupOrder, "disposable"));

        scope.Track(backgroundCompleted.Task);
        scope.Cancel();

        Assert.True(scope.IsStopping);
        Assert.True(scope.LifetimeToken.IsCancellationRequested);
        Assert.Throws<InvalidOperationException>(() => scope.Track(Task.CompletedTask));
        Assert.Throws<InvalidOperationException>(() => scope.Register(new RecordingDisposable(cleanupOrder, "late")));

        await scope.CleanupAsync();

        Assert.Collection(
            cleanupOrder,
            item => Assert.Equal("disposable", item),
            item => Assert.Equal("callback", item));
        Assert.True(backgroundCompleted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task LifetimeScopeTreatsTrackedTaskCancellationAsNormalShutdown()
    {
        using var scope = new PluginLifetimeScope();
        var backgroundTask = Task.Delay(Timeout.InfiniteTimeSpan, scope.LifetimeToken);

        scope.Track(backgroundTask);
        scope.Cancel();

        await scope.CleanupAsync();

        Assert.True(backgroundTask.IsCanceled);
    }

    [Fact]
    public async Task LifetimeScopeStillReportsTrackedTaskFaultsDuringShutdown()
    {
        using var scope = new PluginLifetimeScope();
        scope.Track(Task.FromException(new InvalidOperationException("background failed")));
        scope.Cancel();

        var exception = await Assert.ThrowsAsync<AggregateException>(
            async () => await scope.CleanupAsync());

        Assert.Contains(
            exception.Flatten().InnerExceptions,
            inner => inner is InvalidOperationException
                && inner.Message == "background failed");
    }

    [Fact]
    public async Task ShutdownDeadlineProvidesOneSharedRemainingBudget()
    {
        using var deadline = ShutdownDeadline.Start(
            new PluginShutdownOptions(TimeSpan.FromMilliseconds(150)));
        var initialRemaining = deadline.Remaining;

        await Task.Delay(40);

        Assert.True(deadline.Remaining < initialRemaining);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token));
        Assert.True(deadline.IsTimedOut);

        var remainingAfterCancellation = deadline.Remaining;
        if (remainingAfterCancellation > TimeSpan.Zero)
        {
            await Task.Delay(remainingAfterCancellation + TimeSpan.FromMilliseconds(20));
        }

        Assert.True(deadline.IsExpired);
        Assert.Equal(TimeSpan.Zero, deadline.Remaining);
    }

    [Fact]
    public void ShutdownDeadlineDoesNotClassifyExternalCancellationAsTimeout()
    {
        using var cancellation = new CancellationTokenSource();
        using var deadline = ShutdownDeadline.Start(
            new PluginShutdownOptions(TimeSpan.FromSeconds(5)),
            cancellation.Token);

        cancellation.Cancel();

        Assert.True(deadline.Token.IsCancellationRequested);
        Assert.True(deadline.IsExternallyCancelled);
        Assert.False(deadline.IsTimedOut);
    }

    [Fact]
    public void LifecycleFailureStatesRemainExplicitAndQuarantineIsTerminalUntilDisabled()
    {
        var manifest = new PluginManifest(
            FormatVersion: 2,
            Id: "com.toolbox.lifecycle-policy",
            Name: "Lifecycle Policy",
            Version: "0.1.0",
            PluginApiMajor: 1,
            Publisher: "toolbox.tests",
            Platform: new PluginPlatform("windows", "x64"),
            Runtime: new PluginRuntime(
                new[] { PluginExecutionMode.InProcess },
                PluginExecutionMode.InProcess,
                Background: false),
            Capabilities: [new PluginCapability(
                PluginCapabilityContract.BackgroundExecution,
                Required: true,
                "Exercises lifecycle state transitions in the test fixture.")],
            EntryPoint: "Lifecycle.Policy, Lifecycle");
        var state = PluginState.CreateInstalled(manifest)
            .TransitionTo(PluginLifecycleState.Disabled)
            .TransitionTo(PluginLifecycleState.Starting)
            .TransitionTo(PluginLifecycleState.Running)
            .TransitionTo(PluginLifecycleState.Stopping)
            .TransitionTo(
                PluginLifecycleState.DisableFailed,
                errorCode: "PLUGIN_STOP_FAILED",
                errorMessage: "stop failed");

        Assert.Equal(PluginLifecycleState.DisableFailed, state.LifecycleState);

        state = state.TransitionTo(
            PluginLifecycleState.RestartRequired,
            errorCode: "PLUGIN_RESTART_REQUIRED",
            errorMessage: "restart required");
        Assert.Equal(PluginLifecycleState.RestartRequired, state.LifecycleState);
        Assert.Equal("PLUGIN_RESTART_REQUIRED", state.LastErrorCode);

        var quarantined = PluginState.CreateInstalled(manifest)
            .TransitionTo(PluginLifecycleState.Disabled)
            .TransitionTo(PluginLifecycleState.Starting)
            .TransitionTo(
                PluginLifecycleState.Faulted,
                errorCode: "PLUGIN_START_FAILED",
                errorMessage: "crash")
            .TransitionTo(
                PluginLifecycleState.Quarantined,
                errorCode: "PLUGIN_QUARANTINED",
                errorMessage: "too many crashes");

        Assert.Equal(PluginLifecycleState.Quarantined, quarantined.LifecycleState);
        Assert.Equal("PLUGIN_QUARANTINED", quarantined.LastErrorCode);
        Assert.Equal(
            PluginLifecycleState.Disabled,
            quarantined.TransitionTo(PluginLifecycleState.Disabled).LifecycleState);
    }

    private sealed class RecordingDisposable : IDisposable
    {
        private readonly List<string> _cleanupOrder;
        private readonly string _name;

        public RecordingDisposable(List<string> cleanupOrder, string name)
        {
            _cleanupOrder = cleanupOrder;
            _name = name;
        }

        public void Dispose()
        {
            _cleanupOrder.Add(_name);
        }
    }
}
