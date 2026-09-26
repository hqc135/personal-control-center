using ControlCenter.App.ViewModels;
using ControlCenter.Core;
using ControlCenter.Windows;
using ControlCenter.Windows.Launch;
using Xunit;
namespace ControlCenter.Tests;
public class SessionViewModelTests
{
    [Fact] public async Task ConstructionAndShowingNeverAcquireOrTestRemote()
    {
        var backend = new FakeAwakeBackend(); await using var awake = new AwakeService(backend, new TestClock());
        var probe = new FakeProbe(); using var monitor = new ProxyMonitor(probe, a => a());
        using var model = new SessionViewModel(awake, monitor, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), a => a());
        model.SetVisible(true);
        Assert.Empty(backend.Calls); Assert.Equal(0, probe.RemoteCalls);
        Assert.False(model.TestProxy.CanExecute(null)); Assert.False(model.Stop.CanExecute(null));
    }
    [Fact] public async Task HiddenAwakeChangesDoNotScheduleUiUpdates()
    {
        var backend = new FakeAwakeBackend(); await using var awake = new AwakeService(backend, new TestClock());
        using var monitor = new ProxyMonitor(new FakeProbe(), a => a());
        int dispatches = 0;
        using var model = new SessionViewModel(awake, monitor, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), a => { dispatches++; a(); });
        await awake.StartAsync(30, false); Assert.Equal(0, dispatches);
        model.SetVisible(true); Assert.True(dispatches > 0);
        model.SetVisible(false); dispatches = 0;
        await awake.StopAsync(); Assert.Equal(0, dispatches);
    }
    [Fact] public async Task InvalidCustomDurationDoesNotReachService()
    {
        var backend = new FakeAwakeBackend(); await using var awake = new AwakeService(backend, new TestClock());
        using var monitor = new ProxyMonitor(new FakeProbe(), a => a());
        using var model = new SessionViewModel(awake, monitor, new ShortcutLauncher(new FakeTrust(), new FakePlatform()), new(), a => a());
        model.Minutes = "0"; model.Start.Execute(null);
        Assert.Empty(backend.Calls); Assert.Contains("1–480", model.AwakeMessage);
    }
}
