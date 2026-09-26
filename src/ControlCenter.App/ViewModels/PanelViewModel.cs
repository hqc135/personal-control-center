using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ControlCenter.Core;
namespace ControlCenter.App.ViewModels;
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) { if (CanExecute(parameter)) execute(parameter); }
    public event EventHandler? CanExecuteChanged;
    public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed record PowerChoice(Guid Id, string Label);
public sealed class PanelViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ControlCoordinator coordinator;
    private readonly IShortcutLauncher launcher;
    private readonly ShortcutCoordinator shortcuts;
    private readonly Action<Action> dispatch;
    private AppConfig config;
    private string notice = "第五轮候选：控制操作需主动点击；不会自动保持唤醒或测试外网。";
    private double volume;
    private bool devicesOpen, gestureActive, disposed;
    private string? gestureEndpoint;
    private long volumeSequence;
    private int pendingVolume;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? ShortcutSettingsRequested;
    public string DataOriginLabel { get; init; } = "第五轮候选 · 真实系统状态";
    public SessionViewModel? Session { get; }
    public IReadOnlyList<string> Modules => config.Modules;
    public string AudioSwitchExplanation => AudioSwitchCapability.Current.Explanation;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public double Volume
    {
        get => volume;
        set
        {
            if (Math.Abs(volume - value) < 0.001 || !double.IsFinite(value)) return;
            volume = Math.Clamp(value, 0, 100); Changed(); Changed(nameof(VolumeLabel));
            _ = SubmitVolumeAsync();
        }
    }
    public string VolumeLabel => AudioAvailable ? $"{Math.Round(volume)}%" : "—";
    public string MuteLabel => coordinator.Audio.Current.Value?.Level?.Muted == true ? "取消静音" : "静音";
    public string Device => coordinator.Audio.Current.Value is { } audio
        ? audio.Devices.FirstOrDefault(x => x.Id == audio.DefaultEndpointId)?.Name ?? "未发现输出设备" : "读取中";
    public bool AudioAvailable => coordinator.Audio.Current.Value?.Level is not null && !(coordinator.Audio.Current.IsStale && coordinator.Audio.Current.Error is not null);
    public string AudioStatus => Status(coordinator.Audio.Current);
    public string PowerStatus => Status(coordinator.Power.Current);
    public bool PowerAvailable => coordinator.Power.Current.Value is not null && !(coordinator.Power.Current.IsStale && coordinator.Power.Current.Error is not null);
    public string PowerSource => coordinator.Power.Current.Value?.OnAcPower switch { true => "已接电源", false => "电池供电", _ => "电源状态未知" };
    public IReadOnlyList<string> Devices => coordinator.Audio.Current.Value?.Devices.Select(x =>
        x.Name + (x.Id == coordinator.Audio.Current.Value.DefaultEndpointId ? " · 媒体" : "")
            + (x.Id == coordinator.Audio.Current.Value.CommunicationsEndpointId ? " · 通话" : "")
            + (x.Id == coordinator.Audio.Current.Value.ConsoleEndpointId ? " · 控制台" : "")).ToArray() ?? [];
    public IReadOnlyList<PowerChoice> PowerChoices => coordinator.Power.Current.Value is { } power
        ? power.Schemes.Select(x => new PowerChoice(x.Id, (power.ActiveId == x.Id ? "✓  " : "") + x.Name)).ToArray() : [];
    public IReadOnlyList<ShortcutDefinition> Shortcuts => config.Shortcuts;
    public string Notice { get => notice; set { notice = value; Changed(); } }
    public bool DevicesOpen { get => devicesOpen; set { devicesOpen = value; Changed(); } }
    public RelayCommand Mute { get; }
    public RelayCommand ToggleDevices { get; }
    public RelayCommand SoundSettings { get; }
    public RelayCommand SelectPower { get; }
    public RelayCommand Launch { get; }
    public RelayCommand Refresh { get; }
    public RelayCommand EditShortcuts { get; }
    public PanelViewModel(ControlCoordinator coordinator, IShortcutLauncher launcher, AppConfig config, Action<Action> dispatch, SessionViewModel? session = null)
    {
        this.coordinator = coordinator; this.launcher = launcher; this.config = config; this.dispatch = dispatch;
        shortcuts = new(launcher); Session = session;
        coordinator.SetModules(config.Modules.Contains("audio"), config.Modules.Contains("power"));
        Mute = new(p => { _ = RunMuteAsync(); }, p => AudioAvailable);
        ToggleDevices = new(p => DevicesOpen = !DevicesOpen);
        SoundSettings = new(p => { _ = RunAsync(() => launcher.OpenSoundSettingsAsync(CancellationToken.None)); });
        SelectPower = new(p => { if (p is Guid id) _ = RunAsync(() => coordinator.SetPowerAsync(id)); }, p => PowerAvailable);
        Launch = new(p => { if (p is ShortcutDefinition entry) _ = RunAsync(() => shortcuts.LaunchAsync(entry)); });
        Refresh = new(p => { _ = coordinator.RefreshAsync(); });
        EditShortcuts = new(p => ShortcutSettingsRequested?.Invoke());
        coordinator.Audio.Changed += AudioChanged;
        coordinator.Power.Changed += PowerChanged;
    }
    private async Task RunMuteAsync()
    {
        if (coordinator.Audio.Current.Value?.Level is not { } level) return;
        await RunAsync(() => coordinator.SetMuteAsync(level.EndpointId, !level.Muted));
    }
    public void BeginVolumeGesture()
    {
        gestureActive = true;
        gestureEndpoint = coordinator.Audio.Current.Value?.Level?.EndpointId;
    }
    public void EndVolumeGesture()
    {
        if (!gestureActive) return;
        if (gestureEndpoint is not null) _ = SubmitVolumeAsync();
        gestureActive = false; gestureEndpoint = null;
        if (pendingVolume == 0) ApplyAudio();
    }
    private async Task SubmitVolumeAsync()
    {
        string? target = gestureActive ? gestureEndpoint : coordinator.Audio.Current.Value?.Level?.EndpointId;
        if (target is null || !AudioAvailable) return;
        long sequence = ++volumeSequence;
        pendingVolume++;
        try
        {
            var result = await coordinator.SetVolumeAsync(target, (float)(volume / 100));
            if (!disposed && sequence == volumeSequence && result.Outcome != CommandOutcome.Superseded && result.Message is not null) Notice = result.Message;
        }
        catch (Exception) { if (!disposed) Notice = "音量操作未完成，请刷新实际状态。"; }
        finally { pendingVolume--; if (!disposed && pendingVolume == 0) ApplyAudio(); }
    }
    private async Task RunAsync(Func<Task<CommandResult>> action)
    {
        try
        {
            var result = await action();
            if (!disposed && result.Outcome != CommandOutcome.Superseded) Notice = result.Message ?? (result.Outcome == CommandOutcome.Confirmed ? "操作已确认。" : "结果待确认，请刷新。");
        }
        catch (Exception) { if (!disposed) Notice = "操作未完成，请检查实际状态后重试。"; }
    }
    private void AudioChanged() => dispatch(() => { if (!disposed) ApplyAudio(); });
    private void ApplyAudio()
    {
        var audio = coordinator.Audio.Current.Value;
        if (gestureActive && gestureEndpoint is not null && audio is not null && !audio.Devices.Any(x => x.Id == gestureEndpoint))
        {
            gestureEndpoint = null; coordinator.CancelPendingVolume();
            Notice = "原输出设备已断开，本次拖动已取消。";
        }
        if (pendingVolume == 0 && !gestureActive && audio?.Level is { } level) volume = level.Volume * 100;
        foreach (var name in new[] { nameof(Volume), nameof(VolumeLabel), nameof(Device), nameof(MuteLabel), nameof(AudioAvailable), nameof(AudioStatus), nameof(Devices) }) Changed(name);
        Mute.Notify();
    }
    private void PowerChanged() => dispatch(() =>
    {
        if (disposed) return;
        Changed(nameof(PowerChoices)); Changed(nameof(PowerStatus)); Changed(nameof(PowerAvailable)); Changed(nameof(PowerSource)); SelectPower.Notify();
    });
    private static string Status<T>(ModuleState<T> state) where T : class
        => state.Operation == CommandOutcome.Pending ? "操作中，等待系统确认…" :
        state.Error ?? (state.IsRefreshing ? "正在刷新…" : state.IsStale ? "状态待刷新" : "已读取系统状态");
    public void UpdateConfig(AppConfig value)
    {
        config = value; Changed(nameof(Shortcuts)); Changed(nameof(Modules));
        coordinator.SetModules(config.Modules.Contains("audio"), config.Modules.Contains("power"));
        Session?.UpdateConfig(value);
    }
    public void SetVisible(bool visible) { coordinator.SetVisible(visible); Session?.SetVisible(visible); }
    public void Dispose()
    {
        disposed = true;
        Session?.Dispose();
        coordinator.Audio.Changed -= AudioChanged; coordinator.Power.Changed -= PowerChanged;
    }
}

