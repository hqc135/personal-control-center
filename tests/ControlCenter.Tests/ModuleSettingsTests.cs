using ControlCenter.Core;
using ControlCenter.App.ViewModels;
using ControlCenter.Windows;
using ControlCenter.Windows.Launch;
using Xunit;
namespace ControlCenter.Tests;
public class ModuleSettingsTests
{
    [Fact] public async Task DisabledModulesDoNotReadWhenRefreshed()
    {
        int audioReads = 0, powerReads = 0;
        var audio = new FakeAudio(); var power = new FakePower();
        audio.ReadOverride = _ => { audioReads++; return Task.FromResult(audio.Snapshot); };
        power.ReadOverride = _ => { powerReads++; return Task.FromResult(power.Snapshot); };
        await using var coordinator = new ControlCoordinator(audio, power);
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new() { Modules = ["shortcuts"] }, a => a());
        await coordinator.RefreshAsync(); Assert.Equal(0, audioReads + powerReads);
        model.UpdateConfig(new() { Modules = ["power", "audio"] });
        Assert.Equal(new[] { "power", "audio" }, model.Modules);
        await coordinator.RefreshAsync(); Assert.Equal(1, audioReads); Assert.Equal(1, powerReads);
    }
    [Fact] public async Task HiddenProxyModuleDoesNotProbeAndHidingAwakeDoesNotCancelRequest()
    {
        var backend = new FakeAwakeBackend(); await using var awake = new AwakeService(backend, new TestClock());
        var probe = new FakeProbe(); using var monitor = new ProxyMonitor(probe, a => a());
        using var model = new SessionViewModel(awake, monitor, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new() { Modules = [] }, a => a());
        model.SetVisible(true); Assert.Equal(0, probe.LocalCalls);
        await awake.StartAsync(30, false);
        model.UpdateConfig(new() { Modules = ["shortcuts"] });
        Assert.Equal(AwakeStatus.Active, awake.Current.Status); Assert.Equal(0, probe.LocalCalls);
    }
    [Fact] public async Task AudioRolesAreDisplayedSeparately()
    {
        var audio = new FakeAudio { Snapshot = new([new("a", "Speaker"), new("b", "Headset")], "a", "b", new("a", .6f, false), "a") };
        await using var coordinator = new ControlCoordinator(audio, new FakePower());
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), a => a());
        await coordinator.RefreshAudioAsync();
        Assert.Contains("Speaker · 媒体 · 控制台", model.Devices); Assert.Contains("Headset · 通话", model.Devices);
    }
}
