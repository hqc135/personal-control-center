using ControlCenter.App.ViewModels;
using ControlCenter.Core;
using ControlCenter.Windows.Launch;
using Xunit;
namespace ControlCenter.Tests;
public class InteractionContinuityTests
{
    private static IShortcutLauncher Launcher() => new ShortcutLauncher(new FakeTrust(), new FakePlatform());
    [Fact] public async Task RefreshKeepsUnchangedOptionInstances()
    {
        await using var coordinator = new ControlCoordinator(new FakeAudio(), new FakePower(), TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, Launcher(), new(), a => a());
        await coordinator.RefreshAsync(); var devices = model.OutputDevices; var choices = model.PowerChoices;
        await coordinator.RefreshAsync();
        Assert.Same(devices, model.OutputDevices); Assert.Same(choices, model.PowerChoices);
    }
    [Fact] public async Task RefreshRetainsLastValuesAndDisablesStaleControls()
    {
        var source = new DesktopSource(); using var model = new DesktopViewModel(Launcher(), source);
        await model.RefreshAsync(); var displays = model.Displays;
        await model.RefreshAsync(); Assert.Same(displays, model.Displays);
        source.Failure = true; await model.RefreshAsync();
        Assert.Same(displays, model.Displays); Assert.True(model.IsStale); Assert.False(model.CanOperate); Assert.Contains("上次", model.Status);
        source.Failure = false; source.Partial = true; await model.RefreshAsync();
        Assert.Same(displays, model.Displays); Assert.True(model.IsStale); Assert.False(model.CanOperate);
        source.Partial = false; await model.RefreshAsync(); Assert.False(model.IsStale); Assert.True(model.CanOperate);
    }
    [Fact] public async Task PowerSelectionChangesOnlyAfterConfirmation()
    {
        var power = new FakePower(); var entered = new TaskCompletionSource(); var finish = new TaskCompletionSource();
        power.ActivateOverride = async (id, ct) => { entered.SetResult(); await finish.Task; throw new IOException("rejected"); };
        await using var coordinator = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, Launcher(), new(), a => a());
        await coordinator.RefreshAsync();
        var request = coordinator.SetPowerAsync(FakePower.B); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(FakePower.A, model.PowerChoices.Single(x => x.IsSelected).Id);
        Assert.False(model.SelectPower.CanExecute(FakePower.B));
        finish.SetResult(); await request;
        Assert.Equal(FakePower.A, model.PowerChoices.Single(x => x.IsSelected).Id);
        power.ActivateOverride = null; await coordinator.SetPowerAsync(FakePower.B);
        Assert.Equal(FakePower.B, model.PowerChoices.Single(x => x.IsSelected).Id);
    }
    private sealed class DesktopSource : IDesktopFeatures
    {
        public bool Failure, Partial;
        public Task<DesktopSnapshot> ReadAsync(CancellationToken ct)
        {
            if (Failure) throw new IOException("offline");
            return Task.FromResult(new DesktopSnapshot("", "", "", null,
                Partial ? [] : [new("screen", "Screen", 55)], [], Partial ? ["亮度读取失败"] : []));
        }
        public Task MediaAsync(string id, string action, CancellationToken ct) => Task.CompletedTask;
    }
}
