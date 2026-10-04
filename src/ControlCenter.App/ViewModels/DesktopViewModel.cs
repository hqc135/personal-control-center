using System.ComponentModel;
using ControlCenter.Core;
namespace ControlCenter.App.ViewModels;
public sealed class DesktopViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDesktopFeatures? service;
    private readonly IAudioDevices? audio;
    private readonly IBrightnessService? brightness;
    private readonly IFeaturePreferencesStore? store;
    private readonly IShortcutLauncher launcher;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy, disposed, preferencesReadOnly;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? OperationCompleted;
    public FeaturePreferences Preferences { get; private set; } = new();
    public SceneRunner? Scenes { get; }
    public DesktopSnapshot? Snapshot { get; private set; }
    public string Status { get; private set; } = "打开全部功能或刷新以读取设备状态";
    public string Battery => Snapshot?.Battery ?? "";
    public string Connections => Snapshot is { } s ? s.Network + "\n蓝牙：" + s.Bluetooth : "尚未读取";
    public string MicrophoneName => Snapshot?.Microphone?.Name ?? "未发现可访问的麦克风";
    public string MicrophoneSummary => Snapshot is null ? "尚未读取" : Snapshot.Microphone is null ? "不可用" : Snapshot.Microphone.Muted ? "已静音" : "未静音";
    public string NetworkSummary => Snapshot?.Network.Split(" · 链路")[0] ?? "尚未读取";
    public string BluetoothSummary => Snapshot?.Bluetooth ?? "尚未读取";
    public string BrightnessHint => Snapshot is null ? "尚未读取屏幕能力，请刷新。"
        : Displays.Length > 0 ? "调整后点应用；百分比为待应用值。"
        : Snapshot.Warnings.Any(x => x.Contains("亮度")) ? "亮度读取失败，可刷新重试或打开显示设置。"
        : "未发现支持 WMI / DDC 的可控屏幕，可打开显示设置。";
    public string Microphone => Snapshot?.Microphone is { } m ? m.Name + (m.Muted ? " · 已静音" : " · 未静音") : "未发现可访问的麦克风";
    public string MicrophoneAction => Snapshot?.Microphone?.Muted == true ? "取消麦克风静音" : "麦克风静音";
    public DisplayBrightness[] Displays => Snapshot?.Displays ?? [];
    public MediaSession[] Media => Snapshot?.Media ?? [];
    public RelayCommand Refresh { get; }
    public RelayCommand MuteMicrophone { get; }
    public RelayCommand OpenSettings { get; }
    public bool Busy => busy;
    public DesktopViewModel(IShortcutLauncher launcher, IDesktopFeatures? service = null, IFeaturePreferencesStore? store = null, SceneRunner? scenes = null, string[]? defaultFavorites = null, IAudioDevices? audio = null, IBrightnessService? brightness = null)
    {
        this.launcher = launcher; this.service = service; this.audio = audio; this.brightness = brightness; this.store = store; Scenes = scenes;
        try { var defaults = new FeaturePreferences { Favorites = defaultFavorites ?? Preferences.Favorites }; Preferences = store?.Load(defaults) ?? defaults; }
        catch { preferencesReadOnly = true; Status = "功能配置读取失败，原文件保留；本次不保存布局或场景。"; }
        Refresh = new(_ => { _ = RefreshAsync(); }, _ => !busy && !disposed);
        MuteMicrophone = new(_ => { _ = ToggleMicrophoneAsync(); }, _ => !busy && !disposed && audio is not null && Snapshot?.Microphone is not null);
        OpenSettings = new(p => { if (p is string key) _ = OpenSettingsAsync(key); });
    }
    public async Task RefreshAsync()
    {
        if (busy || disposed || service is null) return;
        busy = true; Notify();
        try
        {
            Snapshot = await service.ReadAsync(lifetime.Token);
            Status = Snapshot.Warnings.Length > 0 ? string.Join("；", Snapshot.Warnings) : $"更新于 {DateTime.Now:HH:mm:ss} · 仅在打开或手动刷新时读取";
        }
        catch (OperationCanceledException) { }
        catch { Snapshot = null; Status = "设备状态读取失败，请刷新；旧数据未作为当前状态显示。"; }
        finally { busy = false; if (!disposed) Notify(); }
    }
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || disposed || service is null) return;
        busy = true; Status = "操作中…"; Notify();
        string? error = null;
        try { await action(lifetime.Token); }
        catch (Exception ex) { error = ex is ServiceException or IOException ? ex.Message : "操作未完成，请刷新确认。"; }
        finally { busy = false; }
        await RefreshAsync();
        if (error is not null) Status = error;
        if (!disposed) OperationCompleted?.Invoke(error ?? "操作已提交，已刷新设备状态。");
        if (!disposed) Notify();
    }
    public Task ToggleMicrophoneAsync() => Snapshot?.Microphone is { } m && audio is not null
        ? RunAsync(ct => audio.SetMicrophoneMuteAsync(m.Id, !m.Muted, ct)) : Task.CompletedTask;
    public Task SetBrightnessAsync(string id, int value) => RunAsync(ct => brightness!.SetAsync(id, value, ct));
    public Task MediaAsync(string id, string action) => RunAsync(ct => service!.MediaAsync(id, action, ct));
    public Task SwitchOutputAsync(string id) => RunAsync(ct => audio!.SwitchOutputAsync(id, ct));
    private async Task OpenSettingsAsync(string key)
    {
        try { var result = await launcher.OpenSettingsPageAsync(key, lifetime.Token); Status = result.Message ?? (result.Outcome == CommandOutcome.Confirmed ? "已打开系统设置" : "系统设置未打开"); }
        catch { Status = "系统设置未打开"; }
        if (!disposed) Notify();
    }
    public void Save(FeaturePreferences value)
    {
        if (preferencesReadOnly) { Status = "原功能配置不可读，未覆盖。"; Notify(); return; }
        try { value.Validate(); store?.Save(value); Preferences = value; }
        catch (Exception ex) { Status = "功能配置未保存：" + ex.Message; }
        Notify();
    }
    private void Notify() { PropertyChanged?.Invoke(this, new(null)); Refresh.Notify(); MuteMicrophone.Notify(); }
    public void Dispose() { if (disposed) return; disposed = true; lifetime.Cancel(); }
}
