using System.ComponentModel;
using ControlCenter.Core;
namespace ControlCenter.App.ViewModels;
public sealed class SessionViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAwakeService awake;
    private readonly ProxyMonitor proxy;
    private readonly ShortcutCoordinator launcher;
    private readonly Action<Action> dispatch;
    private AppConfig config;
    private volatile bool disposed, visible;
    private bool busy, launchBusy;
    private string message = "";
    private string launchMessage = "";
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Minutes { get; set; } = "30";
    public bool KeepDisplay { get; set; }
    public string AwakeText => awake.Current.Status switch
    {
        AwakeStatus.Active => $"剩余 {Math.Ceiling(awake.Current.Remaining.TotalMinutes)} 分钟 · " + (awake.Current.KeepDisplay ? "屏幕常亮" : "允许屏幕熄灭"),
        AwakeStatus.ReleaseUnconfirmed => awake.Current.Message ?? "释放未确认",
        _ => awake.Current.Message ?? "未开启"
    };
    public string AwakeMessage => message;
    public string LocalText => proxy.Local?.Message ?? "尚未检测本机端口。";
    public string TargetText => config.Proxy.TestUrl is null ? "请在设置中填写手动测试目标。" : "手动测试目标：" + config.Proxy.TestUrl;
    public string RemoteText => proxy.RemoteBusy ? "正在测试指定目标…" : proxy.Remote?.Message ?? "尚未测试指定目标。";
    public string ProxyLaunchMessage => launchMessage;
    public RelayCommand Start { get; }
    public RelayCommand Stop { get; }
    public RelayCommand TestProxy { get; }
    public RelayCommand OpenProxy { get; }
    public SessionViewModel(IAwakeService awake, ProxyMonitor proxy, IShortcutLauncher launcher, AppConfig config, Action<Action> dispatch)
    {
        this.awake = awake; this.proxy = proxy; this.launcher = new(launcher); this.config = config; this.dispatch = dispatch;
        proxy.Configure(config.Proxy);
        Start = new(p => { _ = ChangeAwakeAsync(p?.ToString() ?? Minutes); }, p => !busy);
        Stop = new(p => { _ = ChangeAwakeAsync(null); }, p => !busy && awake.Current.Status != AwakeStatus.Off);
        TestProxy = new(p => { _ = proxy.TestRemoteAsync(); }, p => config.Proxy.TestUrl is not null && !proxy.RemoteBusy);
        OpenProxy = new(p => { _ = LaunchProxyAsync(); }, p => config.Proxy.ShortcutId is not null && !launchBusy);
        awake.Changed += Notify; proxy.Changed += Notify;
    }
    private async Task ChangeAwakeAsync(string? duration)
    {
        if (busy || disposed) return;
        if (duration is not null && (!int.TryParse(duration, out int parsed) || parsed is < 1 or > 480))
        { message = "请输入 1–480 分钟。"; Notify(); return; }
        busy = true; Notify();
        try
        {
            var result = duration is null ? await awake.StopAsync() : await awake.StartAsync(int.Parse(duration), KeepDisplay);
            message = result.Message ?? (result.Outcome == CommandOutcome.Confirmed ? "请求已确认。" : "请求未确认。");
        }
        catch (Exception) { message = "请求未完成，请检查状态或退出程序。"; }
        finally { busy = false; Notify(); }
    }
    private async Task LaunchProxyAsync()
    {
        var entry = config.Shortcuts.FirstOrDefault(x => x.Id == config.Proxy.ShortcutId && x.Kind == "application");
        if (entry is null || launchBusy || disposed) return;
        launchBusy = true; Notify();
        try
        {
            var result = await launcher.LaunchAsync(entry);
            launchMessage = result.Message ?? (result.Outcome == CommandOutcome.Confirmed
                ? "启动请求已提交；请稍后查看本机端口状态。" : "启动请求未确认。");
        }
        catch (Exception) { launchMessage = "代理程序未能启动。"; }
        finally { launchBusy = false; }
        Notify();
    }
    private void Notify()
    {
        if (disposed || !visible) return;
        dispatch(() =>
        {
            if (disposed || !visible) return;
            PropertyChanged?.Invoke(this, new(null));
            Start.Notify(); Stop.Notify(); TestProxy.Notify(); OpenProxy.Notify();
        });
    }
    public void UpdateConfig(AppConfig value)
    {
        // Hide first so a newly imported/hidden proxy module cannot schedule a probe while applying settings.
        proxy.SetVisible(false); config = value; proxy.Configure(value.Proxy);
        proxy.SetVisible(visible && config.Modules.Contains("proxy")); Notify();
    }
    public void SetVisible(bool visible)
    {
        this.visible = visible;
        proxy.SetVisible(visible && config.Modules.Contains("proxy"));
        if (visible) Notify();
    }
    public void Dispose()
    {
        if (disposed) return;
        SetVisible(false); disposed = true;
        awake.Changed -= Notify; proxy.Changed -= Notify; proxy.Dispose();
    }
}
