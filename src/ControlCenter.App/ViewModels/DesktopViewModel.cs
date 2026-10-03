using System.ComponentModel;
using ControlCenter.Core;
namespace ControlCenter.App.ViewModels;
public sealed class DesktopViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDesktopFeatures? service;
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
    public string Microphone => Snapshot?.Microphone is { } m ? m.Name + (m.Muted ? " · 已静音" : " · 未静音") : "未发现可访问的麦克风";
    public string MicrophoneAction => Snapshot?.Microphone?.Muted == true ? "取消麦克风静音" : "麦克风静音";
    public DisplayBrightness[] Displays => Snapshot?.Displays ?? [];
    public MediaSession[] Media => Snapshot?.Media ?? [];
    public RelayCommand Refresh { get; }
    public RelayCommand MuteMicrophone { get; }
    public RelayCommand OpenSettings { get; }
    public bool Busy => busy;
    public DesktopViewModel(IShortcutLauncher launcher, IDesktopFeatures? service = null, IFeaturePreferencesStore? store = null, SceneRunner? scenes = null, string[]? defaultFavorites = null)
    {
        this.launcher = launcher; this.service = service; this.store = store; Scenes = scenes;
        try { var defaults = new FeaturePreferences { Favorites = defaultFavorites ?? Preferences.Favorites }; Preferences = store?.Load(defaults) ?? defaults; }
        catch { preferencesReadOnly = true; Status = "功能配置读取失败，原文件保留；本次不保存布局或场景。"; }
        Refresh = new(_ => { _ = RefreshAsync(); }, _ => !busy && !disposed);
        MuteMicrophone = new(_ => { if (Snapshot?.Microphone is { } m) _ = RunAsync(ct => service!.SetMicrophoneMuteAsync(m.Id, !m.Muted, ct)); }, _ => !busy && !disposed && service is not null && Snapshot?.Microphone is not null);
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
    public async Task RunAsync(Func<CancellationToken, Task> action)
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
    public Task SetBrightnessAsync(string id, int value) => RunAsync(ct => service!.SetBrightnessAsync(id, value, ct));
    public Task MediaAsync(string id, string action) => RunAsync(ct => service!.MediaAsync(id, action, ct));
    public Task SwitchOutputAsync(string id) => RunAsync(ct => service!.SwitchOutputAsync(id, ct));
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
