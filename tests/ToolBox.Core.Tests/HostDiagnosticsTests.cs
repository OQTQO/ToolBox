using ToolBox.Core.Diagnostics;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class HostDiagnosticsTests
{
    [Fact]
    public void NewSessionStartsAtCreated()
    {
        var diagnostics = new HostDiagnostics("launch-1", "session-1", "0.1.0");

        var snapshot = diagnostics.Snapshot();

        Assert.Equal("launch-1", snapshot.LaunchAttemptId);
        Assert.Equal("session-1", snapshot.SessionId);
        Assert.Equal(StartupStage.Created, snapshot.Stage);
    }

    [Fact]
    public void FailureIsVisibleInSnapshot()
    {
        var diagnostics = new HostDiagnostics("launch-1", "session-1", "0.1.0");

        diagnostics.RecordFailure("HOST_TEST_FAILURE", new InvalidOperationException("test failure"));

        var snapshot = diagnostics.Snapshot();

        Assert.Equal(StartupStage.Faulted, snapshot.Stage);
        Assert.Equal("HOST_TEST_FAILURE", snapshot.LastErrorCode);
        Assert.Equal("test failure", snapshot.LastErrorMessage);
    }

    [Fact]
    public void StageChangeNotifiesSubscribers()
    {
        var diagnostics = new HostDiagnostics("launch-1", "session-1", "0.1.0");
        HostDiagnosticsSnapshot? changed = null;
        diagnostics.Changed += snapshot => changed = snapshot;

        diagnostics.TransitionTo(StartupStage.Healthy);

        Assert.NotNull(changed);
        Assert.Equal(StartupStage.Healthy, changed!.Stage);
    }
}
