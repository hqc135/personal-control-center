using ControlCenter.App.ViewModels;
using ControlCenter.Core;
using ControlCenter.Windows.Launch;
using Xunit;
namespace ControlCenter.Tests;
public class DesktopRoutingTests
{
    [Fact] public async Task DirectDeviceWritesKeepTargetsAndRefreshState()
    {
        var audio = new AudioDevices(); var brightness = new Brightness(); var status = new Status(audio, brightness);
        using var model = new DesktopViewModel(new ShortcutLauncher(new FakeTrust(), new FakePlatform()), status, audio: audio, brightness: brightness);
        await model.RefreshAsync();
        await model.ToggleMicrophoneAsync();
        await model.SetBrightnessAsync("screen-original", 42);
        await model.SwitchOutputAsync("speaker-selected");
        Assert.Equal("mic-original", audio.MicrophoneTarget);
        Assert.True(model.Snapshot!.Microphone!.Muted);
        Assert.Equal(("screen-original", 42), brightness.LastWrite);
        Assert.Equal(42, model.Displays.Single().Percent);
        Assert.Equal("speaker-selected", audio.OutputTarget);
        Assert.Equal(4, status.Reads);
    }
    [Fact] public async Task NativeFailurePreservesFeedbackAndReadbackWithoutRetry()
    {
        var audio = new AudioDevices(); var brightness = new Brightness { Fail = true }; var status = new Status(audio, brightness);
        using var model = new DesktopViewModel(new ShortcutLauncher(new FakeTrust(), new FakePlatform()), status, audio: audio, brightness: brightness);
        await model.RefreshAsync();
        await model.SetBrightnessAsync("screen-original", 42);
        Assert.Equal("显示屏已断开", model.Status);
        Assert.False(model.Busy);
        Assert.Equal(65, model.Displays.Single().Percent);
        Assert.Equal(1, brightness.Attempts);
        Assert.Equal(2, status.Reads);
        model.Dispose();
        await model.SetBrightnessAsync("screen-original", 30);
        Assert.Equal(1, brightness.Attempts);
    }
    private sealed class AudioDevices : IAudioDevices
    {
        public bool Muted; public string? MicrophoneTarget, OutputTarget;
        public Task<MicrophoneState?> ReadMicrophoneAsync(CancellationToken ct) => Task.FromResult<MicrophoneState?>(new("mic-original", "Mic", Muted));
        public Task SetMicrophoneMuteAsync(string id, bool muted, CancellationToken ct) { MicrophoneTarget = id; Muted = muted; return Task.CompletedTask; }
        public Task SwitchOutputAsync(string id, CancellationToken ct) { OutputTarget = id; return Task.CompletedTask; }
    }
    private sealed class Brightness : IBrightnessService
    {
        public int Percent = 65, Attempts; public bool Fail; public (string, int)? LastWrite;
        public Task<DisplayBrightness[]> ReadAsync(CancellationToken ct) => Task.FromResult(new[] { new DisplayBrightness("screen-original", "Screen", Percent) });
        public Task SetAsync(string id, int value, CancellationToken ct)
        {
            Attempts++;
            if (Fail) throw new ServiceException(FailureCode.DeviceGone, "显示屏已断开");
            LastWrite = (id, value); Percent = value; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Status(AudioDevices audio, Brightness brightness) : IDesktopFeatures
    {
        public int Reads;
        public async Task<DesktopSnapshot> ReadAsync(CancellationToken ct) { Reads++; return new("", "", "", await audio.ReadMicrophoneAsync(ct), await brightness.ReadAsync(ct), [], []); }
        public Task MediaAsync(string id, string action, CancellationToken ct) => Task.CompletedTask;
    }
}
