using ControlCenter.Core;
using ControlCenter.App.ViewModels;
using ControlCenter.Windows.Launch;
using Xunit;
namespace ControlCenter.Tests;
public class ViewModelTests
{
    [Fact] public async Task ReadingSystemStateNeverWritesVolume()
    {
        var audio = new FakeAudio();
        await using var coordinator = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), action => action());
        await coordinator.RefreshAsync();
        Assert.True(model.AudioAvailable); Assert.InRange(model.Volume, 59.99, 60.01); Assert.Empty(audio.Writes);
        Assert.Equal("Speaker", model.Device);
    }
    [Fact] public async Task RemovedGestureTargetCannotRetargetToNewDefaultThroughBinding()
    {
        var audio = new FakeAudio();
        await using var coordinator = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), action => action());
        await coordinator.RefreshAsync();
        model.BeginVolumeGesture();
        audio.Snapshot = new([new("b", "Headset")], "b", "b", new("b", .3f, false));
        await coordinator.RefreshAudioAsync();
        model.Volume = 85; model.EndVolumeGesture();
        Assert.Empty(audio.Writes); Assert.InRange(model.Volume, 29.99, 30.01);
        Assert.Contains("已断开", model.Notice);
    }
    [Fact] public async Task MissingDevicesDisableControlsAndShowNoFakePercentage()
    {
        var audio = new FakeAudio { Snapshot = new([], null, null, null) };
        await using var coordinator = new ControlCoordinator(audio, new FakePower(), TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), action => action());
        await coordinator.RefreshAsync();
        Assert.False(model.AudioAvailable); Assert.False(model.Mute.CanExecute(null));
        Assert.Equal("—", model.VolumeLabel); Assert.Equal("未发现输出设备", model.Device);
    }
    [Fact] public async Task PowerChoicesComeFromObservedGuidsNotHardcodedNames()
    {
        var id = Guid.NewGuid();
        var power = new FakePower { Snapshot = new([new(id, "Custom")], id, null) };
        await using var coordinator = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), action => action());
        await coordinator.RefreshAsync();
        var choice = Assert.Single(model.PowerChoices);
        Assert.Equal(id, choice.Id); Assert.Equal("✓  Custom", choice.Label);
    }
    [Fact] public async Task CurrentAndUnknownPowerSchemesCannotBeActivatedFromPanel()
    {
        var active = Guid.NewGuid(); var other = Guid.NewGuid();
        var power = new FakePower { Snapshot = new([new(active, "Balanced")], active, true) };
        await using var coordinator = new ControlCoordinator(new FakeAudio(), power, TimeSpan.Zero);
        using var model = new PanelViewModel(coordinator, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), action => action());
        await coordinator.RefreshAsync();
        Assert.Contains("仅提供一个", model.PowerHint);
        Assert.False(model.SelectPower.CanExecute(active)); Assert.False(model.SelectPower.CanExecute(other));
        power.Snapshot = new([new(active, "Balanced"), new(other, "Custom")], active, true);
        await coordinator.RefreshAsync();
        Assert.True(model.SelectPower.CanExecute(other)); Assert.False(model.SelectPower.CanExecute(active));
    }
}
