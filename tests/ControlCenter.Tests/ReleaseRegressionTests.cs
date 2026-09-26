using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public class ReleaseRegressionTests
{
    private sealed class FailingClock : TimeProvider
    {
        private int reads;
        public override DateTimeOffset GetUtcNow()
            => Interlocked.Increment(ref reads) > 1 ? throw new IOException("clock failure") : DateTimeOffset.UtcNow;
    }
    [Fact] public async Task WorkerFailureRejectsQueuedAndFutureCommandsInsteadOfHanging()
    {
        var backend = new FakeAwakeBackend();
        var service = new AwakeService(backend, new FailingClock());
        var queued = new TaskCompletionSource<Task<CommandResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Changed += () =>
        {
            if (service.Current.Status == AwakeStatus.Active) queued.TrySetResult(service.StopAsync());
        };
        await service.StartAsync(30, false).WaitAsync(TimeSpan.FromSeconds(2));
        var pending = await queued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<IOException>(() => service.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(1, false));
        Assert.False(backend.Calls.Last().Active);
    }
    [Fact] public async Task RepeatedAwakeDisposalDoesNotAcquireOrLeakRequest()
    {
        var backend = new FakeAwakeBackend(); var service = new AwakeService(backend);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.DisposeAsync().AsTask()));
        Assert.Empty(backend.Calls);
    }
    [Fact] public async Task BrokenDiagnosticSinkCannotInterruptShutdown()
    {
        int stopped = 0;
        await ShutdownSequence.RunAsync([
            (ShutdownModule.Awake, () => { stopped++; return Task.CompletedTask; }),
            (ShutdownModule.Power, () => { stopped++; return Task.CompletedTask; })
        ], (ShutdownEntry _) => throw new IOException("log unavailable"));
        Assert.Equal(2, stopped);
    }
    [Fact] public async Task ShutdownEntriesShareCorrelationAndContainDurationAndErrorCode()
    {
        var entries = new List<ShutdownEntry>();
        await ShutdownSequence.RunAsync([
            (ShutdownModule.Awake, () => Task.FromException(new IOException("private text"))),
            (ShutdownModule.Power, () => Task.CompletedTask)
        ], entries.Add);
        Assert.Equal(2, entries.Count);
        Assert.Single(entries.Select(x => x.CorrelationId).Distinct());
        Assert.All(entries, x => Assert.True(x.Elapsed >= TimeSpan.Zero));
        Assert.NotNull(entries[0].HResult);
    }
    [Fact] public void FiveHundredVisibilityCyclesNeverTriggerRemoteProbe()
    {
        var probe = new FakeProbe(); var clock = new TestClock();
        using var monitor = new ProxyMonitor(probe, a => a(), clock);
        monitor.Configure(new(TestUrl: "https://example.com"));
        for (int i = 0; i < 500; i++) { monitor.SetVisible(true); monitor.SetVisible(false); }
        Assert.Equal(1, probe.LocalCalls); Assert.Equal(0, probe.RemoteCalls); Assert.False(monitor.RemoteBusy);
    }
    [Fact] public void FiveHundredTransitionReversalsRejectStaleCompletion()
    {
        var transition = new PanelTransition();
        for (int i = 0; i < 500; i++)
        {
            var open = transition.Request(true);
            var closed = transition.Request(false);
            Assert.False(transition.Complete(open));
            Assert.True(transition.Complete(closed));
            Assert.Equal(PanelPhase.Closed, transition.Phase);
        }
    }
    [Theory] [InlineData(-2560, -1440, 1.0)] [InlineData(0, 0, 1.35)] [InlineData(2560, 0, 2.0)]
    public void PlacementStaysInsideNegativeAndScaledWorkAreas(double left, double top, double scale)
    {
        double width = 360 * scale, height = 800 * scale, margin = 12 * scale;
        var result = PanelPlacement.Clamp(99999, 99999, width, height, new(left, top, 2560, 2160), margin);
        Assert.InRange(result.X, left + margin, left + 2560 - width - margin);
        Assert.InRange(result.Y, top + margin, top + 2160 - height - margin);
    }
}
