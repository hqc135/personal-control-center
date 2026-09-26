using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Utc = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    public long Stamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Stamp;
    public override DateTimeOffset GetUtcNow() => Utc;
    public void Advance(TimeSpan utc, TimeSpan elapsed) { Utc += utc; Stamp += elapsed.Ticks; }
}
internal sealed class FakeAwakeBackend : IAwakeBackend
{
    public bool Success = true;
    public List<(bool Active, bool Display, int Thread)> Calls { get; } = [];
    public bool Apply(bool active, bool keepDisplay) { Calls.Add((active, keepDisplay, Environment.CurrentManagedThreadId)); return Success; }
}
public class AwakeTests
{
    [Fact] public async Task ThrowingObserverCannotPreventRequestRelease()
    {
        var backend = new FakeAwakeBackend(); var service = new AwakeService(backend, new TestClock());
        service.Changed += () => throw new InvalidOperationException();
        await service.StartAsync(1, false); await service.DisposeAsync();
        Assert.False(backend.Calls.Last().Active);
    }
    [Fact] public void ExpiryReleaseFailureRetainsUnconfirmedState()
    {
        var clock = new TestClock(); var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, clock);
        session.Start(1, false); backend.Success = false;
        clock.Advance(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)); session.Check(false);
        Assert.Equal(AwakeStatus.ReleaseUnconfirmed, session.Current.Status);
    }
    [Fact] public void ConstructionDoesNotAcquire()
    {
        var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, new TestClock());
        Assert.Equal(AwakeStatus.Off, session.Current.Status); Assert.Empty(backend.Calls);
        session.Stop(); Assert.Empty(backend.Calls);
    }
    [Theory] [InlineData(0)] [InlineData(481)] [InlineData(-1)]
    public void InvalidDurationNeverReachesBackend(int minutes)
    {
        var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, new TestClock());
        Assert.Equal(CommandOutcome.Failed, session.Start(minutes, false).Outcome); Assert.Empty(backend.Calls);
    }
    [Fact] public void FailedAcquireDoesNotStartCountdown()
    {
        var backend = new FakeAwakeBackend { Success = false }; var session = new AwakeSession(backend, new TestClock());
        Assert.Equal(CommandOutcome.Failed, session.Start(30, true).Outcome);
        Assert.Equal(AwakeStatus.Off, session.Current.Status); Assert.Null(session.Current.Deadline);
    }
    [Fact] public void RenewalReplacesBudgetAndDisplayOption()
    {
        var clock = new TestClock(); var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, clock);
        session.Start(30, false); clock.Advance(TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(29)); session.Start(60, true);
        clock.Advance(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)); session.Check(false);
        Assert.Equal(AwakeStatus.Active, session.Current.Status); Assert.True(session.Current.KeepDisplay);
        Assert.Equal(TimeSpan.FromMinutes(58), session.Current.Remaining);
    }
    [Fact] public void FailedRenewalPreservesOriginalDeadline()
    {
        var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, new TestClock());
        session.Start(30, false); var original = session.Current; backend.Success = false;
        session.Start(60, true); Assert.Equal(original, session.Current);
    }
    [Theory] [InlineData(31, 1)] [InlineData(-60, 30)] [InlineData(30, 0)]
    public void EitherClockDeadlineEndsRequest(int utcMinutes, int elapsedMinutes)
    {
        var backend = new FakeAwakeBackend(); var clock = new TestClock(); var session = new AwakeSession(backend, clock);
        session.Start(30, false); clock.Advance(TimeSpan.FromMinutes(utcMinutes), TimeSpan.FromMinutes(elapsedMinutes)); session.Check(true);
        Assert.Equal(AwakeStatus.Off, session.Current.Status); Assert.False(backend.Calls.Last().Active);
        Assert.Equal(2, backend.Calls.Count); // Expired resume must never reacquire.
    }
    [Fact] public void ResumeWithinBudgetReassertsSameDisplayPolicy()
    {
        var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, new TestClock());
        session.Start(60, true); session.Check(true);
        Assert.True(backend.Calls.Last().Display); Assert.Equal(AwakeStatus.Active, session.Current.Status);
    }
    [Fact] public void FailedResumeIsUnconfirmedNotOff()
    {
        var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, new TestClock());
        session.Start(60, true); backend.Success = false; session.Check(true);
        Assert.Equal(AwakeStatus.ReleaseUnconfirmed, session.Current.Status);
    }
    [Fact] public void FailedReleaseRequiresExplicitRetryAndBlocksNewRequest()
    {
        var backend = new FakeAwakeBackend(); var session = new AwakeSession(backend, new TestClock());
        session.Start(30, false); backend.Success = false;
        Assert.Equal(CommandOutcome.UnknownOutcome, session.Stop().Outcome);
        Assert.Equal(AwakeStatus.ReleaseUnconfirmed, session.Current.Status);
        int calls = backend.Calls.Count; session.Check(false); session.Start(10, false);
        Assert.Equal(calls, backend.Calls.Count);
        backend.Success = true; session.Stop();
        Assert.Equal(AwakeStatus.Off, session.Current.Status); Assert.Contains("本应用", session.Current.Message);
    }
    [Fact] public async Task AcquireResumeReleaseAndDisposeUseSameDedicatedThread()
    {
        var backend = new FakeAwakeBackend(); var caller = Environment.CurrentManagedThreadId;
        var service = new AwakeService(backend, new TestClock());
        await service.StartAsync(30, true); await service.CheckAsync(true); await service.StopAsync();
        await service.StartAsync(60, false); await service.DisposeAsync();
        Assert.Equal(5, backend.Calls.Count);
        Assert.Single(backend.Calls.Select(x => x.Thread).Distinct());
        Assert.NotEqual(caller, backend.Calls[0].Thread);
        Assert.False(backend.Calls.Last().Active);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(30, false));
    }
    [Fact] public async Task ShutdownReportsUnconfirmedRelease()
    {
        var backend = new FakeAwakeBackend(); var service = new AwakeService(backend, new TestClock());
        await service.StartAsync(1, false); backend.Success = false;
        await Assert.ThrowsAsync<ServiceException>(() => service.DisposeAsync().AsTask());
        Assert.Equal(AwakeStatus.ReleaseUnconfirmed, service.Current.Status);
    }
}
