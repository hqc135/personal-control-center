using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
internal sealed class FakeHotkeyBackend : IHotkeyBackend
{
    public List<(string Action, int Id)> Calls = [];
    public bool AllowRegister = true, AllowUnregister = true;
    public bool Register(int id, HotkeyDefinition value) { Calls.Add(("add", id)); return AllowRegister; }
    public bool Unregister(int id) { Calls.Add(("remove", id)); return AllowUnregister; }
}
internal sealed class FakeRunStore : IRunEntryStore
{
    public object? Entry;
    public int Writes, Deletes;
    public bool Denied, IgnoreWrite;
    public object? Read() => Entry;
    public void Write(string command) { if (Denied) throw new UnauthorizedAccessException(); Writes++; if (!IgnoreWrite) Entry = command; }
    public void Delete() { if (Denied) throw new UnauthorizedAccessException(); Deletes++; Entry = null; }
}
public class IntegrationOptionsTests
{
    [Theory] [InlineData("Ctrl+Alt+P", 3, 80)] [InlineData("ctrl+shift+1", 6, 49)]
    public void HotkeyParsesExplicitSafeCombinations(string text, uint modifiers, uint key)
    {
        Assert.Equal(new HotkeyDefinition(modifiers, key), HotkeyPolicy.Parse(text));
    }
    [Theory] [InlineData("P")] [InlineData("Win+P")] [InlineData("Ctrl+F12")] [InlineData("Alt+PrintScreen")]
    [InlineData("Shift+P")] [InlineData("Ctrl+Ctrl+P")] [InlineData("Ctrl++P")]
    public void InvalidOrReservedHotkeyRejected(string text) => Assert.Throws<InvalidDataException>(() => HotkeyPolicy.Parse(text));
    [Fact] public void HotkeyDefaultsOffAndConflictPreservesCurrent()
    {
        var backend = new FakeHotkeyBackend(); using var hotkey = new HotkeyController(backend);
        hotkey.Apply(null); Assert.Empty(backend.Calls);
        hotkey.Apply(new(3, 80)); backend.AllowRegister = false;
        Assert.Equal(CommandOutcome.Failed, hotkey.Apply(new(3, 81)).Outcome);
        Assert.Equal(new HotkeyDefinition(3, 80), hotkey.Current); Assert.True(hotkey.Matches(0x5101));
        Assert.DoesNotContain(backend.Calls, c => c.Action == "remove");
    }
    [Fact] public void NewBindingAcquiredBeforeOldReleasedAndBothReleasedOnExit()
    {
        var backend = new FakeHotkeyBackend(); var controller = new HotkeyController(backend);
        controller.Apply(new(3, 80)); controller.Apply(new(3, 81));
        Assert.Equal(new[] { ("add", 0x5101), ("add", 0x5102), ("remove", 0x5101) }, backend.Calls);
        Assert.False(controller.Matches(0x5101)); Assert.True(controller.Matches(0x5102));
        controller.Dispose(); controller.Dispose();
        Assert.Equal(("remove", 0x5102), backend.Calls.Last()); Assert.Equal(4, backend.Calls.Count);
    }
    [Fact] public void FailedUnregisterDoesNotClaimNewBindingOrDisabled()
    {
        var backend = new FakeHotkeyBackend(); using var controller = new HotkeyController(backend);
        controller.Apply(new(3, 80)); backend.AllowUnregister = false;
        Assert.Equal(CommandOutcome.UnknownOutcome, controller.Apply(null).Outcome);
        Assert.Equal(CommandOutcome.UnknownOutcome, controller.Apply(new(3, 81)).Outcome);
        Assert.Equal(new HotkeyDefinition(3, 80), controller.Current);
    }
    [Fact] public void DisableAlsoReleasesBindingLeftByFailedRollback()
    {
        var backend = new FakeHotkeyBackend(); using var controller = new HotkeyController(backend);
        controller.Apply(new(3, 80)); backend.AllowUnregister = false; controller.Apply(new(3, 81));
        backend.AllowUnregister = true;
        Assert.Equal(CommandOutcome.Confirmed, controller.Apply(null).Outcome);
        Assert.Null(controller.Current);
        Assert.Contains(("remove", 0x5101), backend.Calls.TakeLast(2));
        Assert.Contains(("remove", 0x5102), backend.Calls.TakeLast(2));
    }
    [Fact] public void StartupReadDoesNotWriteAndEnableUsesQuotedCurrentExecutable()
    {
        var store = new FakeRunStore(); var startup = new UserStartupService(store, @"C:\Apps With Space\PersonalControlCenter.exe");
        var observed = startup.Read(); Assert.Equal(0, store.Writes); Assert.Null(observed.Entry);
        Assert.Equal(CommandOutcome.Confirmed, startup.Set(true, observed).Outcome);
        Assert.Equal("\"C:\\Apps With Space\\PersonalControlCenter.exe\" --tray", store.Entry);
        Assert.True(startup.Read().CurrentLocation);
        Assert.Equal(CommandOutcome.Confirmed, startup.Set(false, startup.Read()).Outcome);
        Assert.Null(store.Entry); Assert.Equal(1, store.Deletes);
    }
    [Fact] public void StartupDoesNotOverwriteOrDeleteDifferentRegistration()
    {
        var store = new FakeRunStore { Entry = "another app" }; var startup = new UserStartupService(store, @"C:\Apps\PCC.exe");
        Assert.Equal(CommandOutcome.Failed, startup.Set(true, startup.Read()).Outcome);
        Assert.Equal(CommandOutcome.Failed, startup.Set(false, startup.Read()).Outcome);
        Assert.Equal("another app", store.Entry); Assert.Equal(0, store.Deletes + store.Writes);
    }
    [Fact] public void StartupRejectsChangedObservedState()
    {
        var store = new FakeRunStore(); var startup = new UserStartupService(store, @"C:\Apps\PCC.exe");
        var observed = startup.Read(); store.Entry = "external change";
        Assert.Equal(FailureCode.VerificationFailed, startup.Set(true, observed).Code); Assert.Equal(0, store.Writes);
    }
    [Fact] public void StartupPermissionAndReadbackFailureNeverClaimSuccess()
    {
        var store = new FakeRunStore { Denied = true }; var startup = new UserStartupService(store, @"C:\Apps\PCC.exe");
        Assert.Equal(FailureCode.PermissionDenied, startup.Set(true, startup.Read()).Code);
        store.Denied = false; store.IgnoreWrite = true;
        Assert.Equal(CommandOutcome.UnknownOutcome, startup.Set(true, startup.Read()).Outcome);
    }
    [Fact] public void AudioSwitchGateCannotBeEnabledByImportedExtension()
    {
        var config = ConfigCodec.Decode(System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"enableNativeAudioSwitch":true}"""));
        Assert.False(AudioSwitchCapability.Current.Available);
        Assert.Contains("系统声音设置", AudioSwitchCapability.Current.Explanation);
        Assert.NotNull(config.Additional);
    }
}
