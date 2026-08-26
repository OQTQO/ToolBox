using ToolBox.Core.Lifetime;
using ToolBox.Core.Plugins;
using ToolBox.Core.Resources;
using ToolBox.Core.Services;
using ToolBox.PluginSdk;
using Xunit;

namespace ToolBox.Core.Tests;

public sealed class ResourceServiceTests
{
    private static readonly string[] ExpectedSharedOwners = ["plugin-a", "plugin-b"];

    [Fact]
    public void SharedLeasesCanCoexistAndExclusiveConflictReportsCurrentHolders()
    {
        using var manager = new ResourceManager();
        using var first = manager.Acquire(
            "plugin-a",
            new ResourceKey("keyboard.globalHook"),
            ResourceAccessMode.Shared);
        using var second = manager.Acquire(
            "plugin-b",
            new ResourceKey("keyboard.globalHook"),
            ResourceAccessMode.Shared);

        var exception = Assert.Throws<ResourceConflictException>(
            () => manager.Acquire(
                "plugin-c",
                new ResourceKey("keyboard.globalHook"),
                ResourceAccessMode.Exclusive));

        Assert.Equal(new ResourceKey("keyboard.globalHook"), exception.ResourceKey);
        Assert.Equal(ResourceAccessMode.Exclusive, exception.RequestedAccessMode);
        Assert.Equal(ExpectedSharedOwners, exception.CurrentOwners);
        Assert.Contains("keyboard.globalHook", exception.Message, StringComparison.Ordinal);
        Assert.Contains("plugin-a", exception.Message, StringComparison.Ordinal);
        Assert.Contains("plugin-b", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourceLeaseRegisteredWithScopeIsReleasedDuringCleanup()
    {
        using var manager = new ResourceManager();
        using var scope = new PluginLifetimeScope();
        var resources = manager.Bind("plugin-scope", scope);

        var lease = resources.Acquire(
            new ResourceKey("adb.device:123456"),
            ResourceAccessMode.Exclusive);

        Assert.False(lease.IsReleased);
        Assert.Equal(1, manager.GetActiveLeaseCount(new ResourceKey("adb.device:123456")));

        scope.Cancel();
        await scope.CleanupAsync();

        Assert.True(lease.IsReleased);
        Assert.Equal(0, manager.GetActiveLeaseCount(new ResourceKey("adb.device:123456")));
        Assert.Throws<InvalidOperationException>(() => resources.Acquire(
            new ResourceKey("adb.device:123456"),
            ResourceAccessMode.Exclusive));
    }

    [Fact]
    public async Task ServiceBrokerLazilyStartsReusesAndStopsAfterLastLeaseIsIdle()
    {
        using var stopCompleted = new ManualResetEventSlim();
        var startCount = 0;
        var stopCount = 0;
        var service = new TestService();
        using var broker = new ServiceBroker();
        broker.Register<ITestService>(
            "test.service",
            _ =>
            {
                Interlocked.Increment(ref startCount);
                return ValueTask.FromResult<ITestService>(service);
            },
            (_, _) =>
            {
                Interlocked.Increment(ref stopCount);
                stopCompleted.Set();
                return ValueTask.CompletedTask;
            },
            idleTimeout: TimeSpan.FromMilliseconds(50));

        var services = broker.Bind("plugin-service");
        var first = await services.AcquireAsync<ITestService>("test.service");
        var second = await services.AcquireAsync<ITestService>("test.service");

        Assert.Same(first.Service, second.Service);
        Assert.Equal(1, startCount);
        Assert.Equal(2, broker.GetReferenceCount("test.service"));

        await first.DisposeAsync();
        Assert.Equal(1, broker.GetReferenceCount("test.service"));
        Assert.False(stopCompleted.IsSet);

        await second.DisposeAsync();
        Assert.True(stopCompleted.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, stopCount);
        Assert.False(broker.IsStarted("test.service"));

        stopCompleted.Reset();
        var third = await services.AcquireAsync<ITestService>("test.service");
        Assert.Equal(2, startCount);
        await third.DisposeAsync();
    }

    [Fact]
    public async Task ServiceLeaseRegisteredWithScopeIsReleasedAndIdleServiceStops()
    {
        using var stopCompleted = new ManualResetEventSlim();
        using var broker = new ServiceBroker();
        broker.Register<ITestService>(
            "scoped.service",
            _ => ValueTask.FromResult<ITestService>(new TestService()),
            (_, _) =>
            {
                stopCompleted.Set();
                return ValueTask.CompletedTask;
            },
            idleTimeout: TimeSpan.FromMilliseconds(25));

        using var scope = new PluginLifetimeScope();
        var services = broker.Bind("plugin-scoped", scope);
        var lease = await services.AcquireAsync<ITestService>("scoped.service");

        scope.Cancel();
        await scope.CleanupAsync();

        Assert.True(lease.IsReleased);
        Assert.True(stopCompleted.Wait(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, broker.GetReferenceCount("scoped.service"));
    }

    private interface ITestService
    {
    }

    private sealed class TestService : ITestService
    {
    }
}
