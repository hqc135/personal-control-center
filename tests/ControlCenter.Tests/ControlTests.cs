using System.Collections.Immutable;
using ControlCenter.Core;
using Xunit;
namespace ControlCenter.Tests;

internal sealed class FakeAudio : IAudioService
{
    public AudioSnapshot Snapshot = new([new("a", "Speaker"), new("b", "Headset")], "a", "b", new("a", .6f, false));
    public List<(string Id, float Value)> Writes = [];
    public Func<CancellationToken, Task<AudioSnapshot>>? ReadOverride;
    public Func<string, float, CancellationToken, Task>? WriteOverride;
    public Func<string, CancellationToken, Task<AudioLevel>>? LevelOverride;
    public event Action? Invalidated;
    public void Signal() => Invalidated?.Invoke();
    public Task<AudioSnapshot> ReadAsync(CancellationToken ct) => ReadOverride?.Invoke(ct) ?? Task.FromResult(Snapshot);
    public Task<AudioLevel> ReadLevelAsync(string id, CancellationToken ct) => LevelOverride?.Invoke(id, ct) ?? Task.FromResult(Snapshot.Level! with { EndpointId = id });
    public async Task SetVolumeAsync(string id, float value, CancellationToken ct)
    {
        Writes.Add((id, value));
        if (WriteOverride is not null) await WriteOverride(id, value, ct);
        else Snapshot = Snapshot with { Level = new(id, value, Snapshot.Level?.Muted ?? false) };
    }
    public Task SetMuteAsync(string id, bool muted, CancellationToken ct) { Snapshot = Snapshot with { Level = new(id, Snapshot.Level?.Volume ?? .6f, muted) }; return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class FakePower : IPowerService
{
    public static readonly Guid A = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid B = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public PowerSnapshot Snapshot = new([new(A, "Balanced"), new(B, "Performance")], A, true);
    public Func<CancellationToken, Task<PowerSnapshot>>? ReadOverride;
    public Func<Guid, CancellationToken, Task>? ActivateOverride;
    public readonly List<Guid> Writes = [];
    public event Action? Invalidated;
    public void Signal() => Invalidated?.Invoke();
    public Task<PowerSnapshot> ReadAsync(CancellationToken ct) => ReadOverride?.Invoke(ct) ?? Task.FromResult(Snapshot);
    public async Task ActivateAsync(Guid id, CancellationToken ct)
    {
        Writes.Add(id);
        if (ActivateOverride is not null) await ActivateOverride(id, ct);
        else Snapshot = Snapshot with { ActiveId = id };
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public class ControlTests
{
    [Fact] public async Task PowerReadFailureDoesNotBlockAudio()
    {
        var power = new FakePower { ReadOverride = _ => throw new ServiceException(FailureCode.NativeFailure, "Power unavailable") };
        await using var controller = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        await controller.RefreshAsync();
        Assert.Null(controller.Power.Current.Value); Assert.NotNull(controller.Audio.Current.Value);
    }
    [Fact] public void OlderReadCannotOverwriteAWriteOrNewerRead()
    {
        var store = new ModuleStore<AudioSnapshot>();
        var audio = new FakeAudio();
        var old = store.BeginRead(); store.BeginWrite(); store.CompleteRead(old, audio.Snapshot);
        Assert.Null(store.Current.Value); Assert.Equal(CommandOutcome.Pending, store.Current.Operation);
        var first = store.BeginRead(); var second = store.BeginRead();
        store.CompleteRead(second, audio.Snapshot with { Level = new("a", .9f, false) });
        store.CompleteRead(first, audio.Snapshot);
        Assert.Equal(.9f, store.Current.Value!.Level!.Volume);
        store.FailRead(first, "old error"); Assert.Null(store.Current.Error);
    }
    [Fact] public async Task ModuleFailuresAreIndependentAndNoFakeStateIsInserted()
    {
        var audio = new FakeAudio { ReadOverride = _ => throw new ServiceException(FailureCode.Unavailable, "No device") };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        await controller.RefreshAsync();
        Assert.Null(controller.Audio.Current.Value);
        Assert.True(controller.Audio.Current.IsStale);
        Assert.Equal(FakePower.A, controller.Power.Current.Value!.ActiveId);
        Assert.False(controller.Power.Current.IsStale);
    }
    [Fact] public async Task VolumeLatestWinsAndFinalValueIsCommitted()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new FakeAudio();
        audio.WriteOverride = async (id, value, ct) =>
        {
            if (audio.Writes.Count == 1) { started.SetResult(); await release.Task; }
            audio.Snapshot = audio.Snapshot with { Level = new(id, value, false) };
        };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var first = controller.SetVolumeAsync("a", .1f);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var middle = controller.SetVolumeAsync("a", .2f);
        var last = controller.SetVolumeAsync("a", .9f);
        Assert.Equal(CommandOutcome.Superseded, (await middle).Outcome);
        release.SetResult();
        Assert.Equal(CommandOutcome.Confirmed, (await first).Outcome);
        Assert.Equal(CommandOutcome.Confirmed, (await last).Outcome);
        Assert.Equal(new[] { .1f, .9f }, audio.Writes.Select(x => x.Value));
        Assert.Equal(.9f, controller.Audio.Current.Value!.Level!.Volume);
    }
    [Fact] public async Task DeviceRemovalNeverRedirectsPendingVolumeToNewDefault()
    {
        var audio = new FakeAudio { Snapshot = new([new("b", "Headset")], "b", "b", new("b", .5f, false)) };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = await controller.SetVolumeAsync("a", .8f);
        Assert.Equal(FailureCode.DeviceGone, result.Code); Assert.Empty(audio.Writes);
        Assert.Equal("b", controller.Audio.Current.Value!.DefaultEndpointId);
    }
    [Fact] public async Task ChangingDefaultDoesNotChangeTheExplicitWriteTarget()
    {
        var audio = new FakeAudio();
        audio.Snapshot = audio.Snapshot with { DefaultEndpointId = "b" };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        await controller.SetVolumeAsync("a", .8f);
        Assert.Equal("a", Assert.Single(audio.Writes).Id);
    }
    [Fact] public async Task MuteIsConfirmedByReadback()
    {
        var audio = new FakeAudio();
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = await controller.SetMuteAsync("a", true);
        Assert.Equal(CommandOutcome.Confirmed, result.Outcome); Assert.True(controller.Audio.Current.Value!.Level!.Muted);
    }
    [Fact] public async Task VerificationFailureIsNeverReportedAsSuccess()
    {
        var audio = new FakeAudio { WriteOverride = (_, _, _) => Task.CompletedTask };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = await controller.SetVolumeAsync("a", .9f);
        Assert.Equal(FailureCode.VerificationFailed, result.Code); Assert.NotEqual(CommandOutcome.Confirmed, result.Outcome);
        Assert.Equal(.6f, controller.Audio.Current.Value!.Level!.Volume);
    }
    [Fact] public async Task WriteSucceededButReadbackFailedIsUnknownOutcome()
    {
        var audio = new FakeAudio { LevelOverride = (_, _) => throw new ServiceException(FailureCode.DeviceGone, "Disconnected") };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = await controller.SetVolumeAsync("a", .9f);
        Assert.Equal(CommandOutcome.UnknownOutcome, result.Outcome); Assert.Single(audio.Writes);
    }
    [Fact] public async Task MatchingValueFromAnotherEndpointCannotConfirmAWrite()
    {
        var audio = new FakeAudio { LevelOverride = (_, _) => Task.FromResult(new AudioLevel("b", .9f, false)) };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = await controller.SetVolumeAsync("a", .9f);
        Assert.Equal(FailureCode.VerificationFailed, result.Code);
    }
    [Fact] public async Task CancellationAfterNativeEntryStillReadsBackTheEffect()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var audio = new FakeAudio();
        audio.WriteOverride = async (id, value, ct) =>
        {
            using var registration = ct.Register(() => cancelled.TrySetResult());
            entered.SetResult(); await cancelled.Task;
            audio.Snapshot = audio.Snapshot with { Level = new(id, value, false) };
        };
        var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = controller.SetVolumeAsync("a", .4f);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await controller.DisposeAsync();
        Assert.Equal(CommandOutcome.Confirmed, (await result).Outcome);
        Assert.Equal(.4f, controller.Audio.Current.Value!.Level!.Volume);
    }
    [Fact] public async Task PowerCommandsAreSerialAndPendingChoiceIsCoalesced()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var power = new FakePower(); int inFlight = 0, max = 0;
        power.ActivateOverride = async (id, _) =>
        {
            max = Math.Max(max, ++inFlight);
            if (power.Writes.Count == 1) { entered.SetResult(); await release.Task; }
            power.Snapshot = power.Snapshot with { ActiveId = id }; inFlight--;
        };
        await using var controller = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        var first = controller.SetPowerAsync(FakePower.B);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(CommandOutcome.Pending, controller.Power.Current.Operation);
        var middle = controller.SetPowerAsync(FakePower.A);
        var last = controller.SetPowerAsync(FakePower.B);
        Assert.Equal(CommandOutcome.Superseded, (await middle).Outcome); release.SetResult();
        await Task.WhenAll(first, last);
        Assert.Equal(1, max); Assert.Equal(2, power.Writes.Count);
        Assert.Equal(FakePower.B, controller.Power.Current.Value!.ActiveId);
    }
    [Fact] public async Task DeletedPowerSchemeIsNotActivated()
    {
        var power = new FakePower();
        await using var controller = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        var result = await controller.SetPowerAsync(Guid.NewGuid());
        Assert.Equal(FailureCode.Unavailable, result.Code); Assert.Empty(power.Writes);
    }
    [Fact] public async Task RefusedPowerWriteKeepsObservedSelection()
    {
        var power = new FakePower { ActivateOverride = (_, _) => throw new ServiceException(FailureCode.PermissionDenied, "Denied") };
        await using var controller = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        var result = await controller.SetPowerAsync(FakePower.B);
        Assert.Equal(FailureCode.PermissionDenied, result.Code);
        Assert.Equal(FakePower.A, controller.Power.Current.Value!.ActiveId);
    }
    [Fact] public async Task InvalidationWhileHiddenOnlyMarksSnapshotStale()
    {
        int reads = 0;
        var audio = new FakeAudio();
        audio.ReadOverride = _ => { reads++; return Task.FromResult(audio.Snapshot); };
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        await controller.RefreshAsync();
        audio.Signal();
        Assert.True(controller.Audio.Current.IsStale); Assert.Equal(1, reads);
    }
    [Theory] [InlineData(float.NaN)] [InlineData(-.1f)] [InlineData(1.1f)]
    public async Task BadVolumeNeverReachesAdapter(float value)
    {
        var audio = new FakeAudio();
        await using var controller = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        var result = await controller.SetVolumeAsync("a", value);
        Assert.Equal(FailureCode.InvalidConfiguration, result.Code); Assert.Empty(audio.Writes);
    }
}

