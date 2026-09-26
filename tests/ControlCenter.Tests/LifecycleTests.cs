using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
public class LifecycleTests
{
    [Fact] public void ExplorerReregistrationReappliesVersionAndAllowsRecovery()
    {
        int adds = 0, versions = 0, removes = 0;
        var tray = new TrayRegistration(() => ++adds != 2, () => { versions++; return true; }, () => removes++);
        Assert.True(tray.Register()); Assert.True(tray.Version4);
        Assert.False(tray.Register()); Assert.False(tray.Available); Assert.False(tray.Version4);
        Assert.True(tray.Register()); Assert.Equal(2, versions);
        tray.Dispose(); tray.Dispose(); Assert.Equal(1, removes);
        Assert.False(tray.Register()); Assert.Equal(3, adds);
    }
    [Fact] public void LegacyCallbackFallbackStillHasUsableTray()
    {
        using var tray = new TrayRegistration(() => true, () => false, () => { });
        Assert.True(tray.Register()); Assert.True(tray.Available); Assert.False(tray.Version4);
    }
    [Fact] public async Task ShutdownContinuesAfterFailureAndTimeoutInDeclaredOrder()
    {
        var reports = new List<(ShutdownModule, ShutdownResult)>();
        await ShutdownSequence.RunAsync([
            (ShutdownModule.Awake, () => Task.FromException(new IOException())),
            (ShutdownModule.Coordinator, () => new TaskCompletionSource().Task),
            (ShutdownModule.Audio, () => Task.CompletedTask),
            (ShutdownModule.Power, () => Task.CompletedTask)
        ], (module, result) => reports.Add((module, result)), TimeSpan.FromMilliseconds(20));
        Assert.Equal(new[] { (ShutdownModule.Awake, ShutdownResult.Failed), (ShutdownModule.Coordinator, ShutdownResult.TimedOut),
            (ShutdownModule.Audio, ShutdownResult.Completed), (ShutdownModule.Power, ShutdownResult.Completed) }, reports);
    }
    [Fact] public void ShutdownLogRotatesAndContainsOnlyFixedFields()
    {
        string dir = Path.Combine(Path.GetTempPath(), "PCC-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "lifecycle.log");
            File.WriteAllBytes(path, new byte[2 * 1024 * 1024]);
            new ShutdownLog(dir).Write(ShutdownModule.Awake, ShutdownResult.Failed);
            Assert.True(File.Exists(path + ".1"));
            Assert.Contains("shutdown Awake Failed", File.ReadAllText(path));
            Assert.DoesNotContain(dir, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
