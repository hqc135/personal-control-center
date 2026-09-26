using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Microsoft.Win32;
using ControlCenter.Core;
using ControlCenter.Windows;
using ControlCenter.Windows.Audio;
using ControlCenter.Windows.Power;
using ControlCenter.Windows.Launch;
using ControlCenter.App.ViewModels;
using ControlCenter.App.Views;
namespace ControlCenter.App;
public partial class App : Application
{
    private SingleInstanceHost? instance;
    private HwndSource? messageWindow;
    private NativeTray? tray;
    private ControlPanel? panel;
    private SettingsWindow? settings;
    private IConfigRepository? repository;
    private ShortcutTrustStore? trust;
    private CoreAudioService? audio;
    private PowerSchemeService? power;
    private ControlCoordinator? coordinator;
    private AwakeService? awake;
    private ShutdownLog? shutdownLog;
    private HotkeyController? hotkeys;
    private UserStartupService? startup;
    private string hotkeyStatus = "本会话未注册快捷键。";
    private AppConfig config = new();
    private bool readOnly, exiting;
    private Task? stopServices;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        { MessageBox.Show("此版本需要 Windows 11，系统内部版本 22621 或更高。", "个人控制中心"); Shutdown(2); return; }
        if (e.Args.Any(x => x is not ("--show" or "--tray")) || e.Args.Length > 1)
        { MessageBox.Show("支持的参数：--show 或 --tray。", "个人控制中心"); Shutdown(2); return; }
        try
        {
            instance = new SingleInstanceHost("PCC");
            if (!instance.IsPrimary)
            {
                try { await instance.NotifyAsync(); } catch (Exception ex) when (ex is IOException or TimeoutException) { MessageBox.Show("已有实例暂未响应，请稍后重试。"); }
                Shutdown(); return;
            }
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalControlCenter");
            repository = new JsonConfigRepository(directory);
            shutdownLog = new(directory);
            var loaded = await repository.LoadAsync(); config = loaded.Config; readOnly = loaded.ReadOnly;
            trust = new ShortcutTrustStore(directory); await trust.LoadAsync();
            ThemeManager.Apply(config.Appearance.Theme);
            audio = new CoreAudioService(); power = new PowerSchemeService();
            coordinator = new ControlCoordinator(audio, power);
            var launcher = new ShortcutLauncher(trust, new WindowsLaunchPlatform());
            Action<Action> dispatch = action =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action);
            };
            awake = new(new ExecutionStateBackend());
            var session = new SessionViewModel(awake, new ProxyMonitor(new ProxyProbe(new ProxyTransport()), dispatch), launcher, config, dispatch);
            var model = new PanelViewModel(coordinator, launcher, config, dispatch, session);
            panel = new(model) { FontFamily = ThemeManager.PreferredFont(), MotionEnabled = config.Appearance.Motion != "off" };
            MainWindow = panel;
            if (loaded.Warning is not null) model.Notice = loaded.Warning;
            panel.SettingsRequested += ShowSettings; model.ShortcutSettingsRequested += ShowSettings;
            panel.ExitRequested += RequestExit;
            messageWindow = new HwndSource(new HwndSourceParameters("PCC.Messages") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) });
            messageWindow.AddHook(MessageHook);
            hotkeys = new(new WindowsHotkeyBackend(messageWindow.Handle));
            startup = new(new UserRunEntryStore(), Path.Combine(AppContext.BaseDirectory, "PersonalControlCenter.exe"));
            try { power.AttachNotifications(messageWindow.Handle); }
            catch (ServiceException ex) { model.Notice = ex.Message; }
            try
            {
                tray = new(messageWindow.Handle, Path.Combine(AppContext.BaseDirectory, "Assets", "pcc.ico"), "个人控制中心 · 第四轮候选");
                tray.Toggle += () => panel.Toggle(tray); tray.Menu += ShowMenu;
            }
            catch (IOException) { model.Notice = "托盘不可用，请使用窗口菜单退出。"; }
            panel.SetTrayAvailable(tray?.IsAvailable == true);
            ApplyHotkey();
            if (!panel.CanHide) { panel.ShowInTaskbar = true; panel.ContextMenu = CreateMenu(); }
            instance.Listen(() => { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { if (!exiting) panel.Present(tray); }); });
            SystemEvents.UserPreferenceChanged += PreferenceChanged;
            SystemParameters.StaticPropertyChanged += SystemSettingChanged;
            if (!e.Args.Contains("--tray") || !panel.CanHide) panel.Present(tray);
        }
        catch (Exception ex)
        {
            MessageBox.Show("启动失败：" + ex.GetType().Name + "。请保留此信息并反馈。", "个人控制中心");
            RequestExit();
        }
    }
    private nint MessageHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (exiting) return 0;
        if (msg == 0x312 && hotkeys?.Matches((int)wParam) == true) { panel?.Toggle(tray); handled = true; return 0; }
        power?.HandleMessage(msg, wParam);
        if (!exiting && msg == 0x218 && (wParam == 0x12 || wParam == 0x7)) _ = CheckResumeAsync();
        tray?.Handle(msg, lParam);
        if (NativeTray.TaskbarCreated != 0 && (uint)msg == NativeTray.TaskbarCreated && panel is not null)
        {
            panel.SetTrayAvailable(tray?.IsAvailable == true);
            if (!panel.CanHide) { panel.ShowInTaskbar = true; panel.ContextMenu = CreateMenu(); panel.Present(tray); }
        }
        return 0;
    }
    private async Task CheckResumeAsync()
    {
        try { if (awake is not null) await awake.CheckAsync(true); }
        catch (Exception) { if (panel is not null) panel.Model.Notice = "恢复后的唤醒状态未确认，请检查或结束请求。"; }
    }
    private ContextMenu CreateMenu()
    {
        var menu = new ContextMenu();
        foreach (var (label, action) in new (string, Action)[] { ("打开", () => panel?.Present(tray)), ("设置", ShowSettings), ("退出", RequestExit) })
        { var item = new MenuItem { Header = label }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        return menu;
    }
    private void ShowMenu()
    {
        if (panel is null || messageWindow is null || exiting) return;
        panel.SuppressDismiss = true;
        var menu = CreateMenu();
        NativeWindow.Foreground(messageWindow.Handle); menu.Placement = PlacementMode.MousePoint;
        menu.Closed += (_, _) => { panel.SuppressDismiss = settings is not null; if (!panel.IsActive && settings is null) panel.Dismiss(); };
        menu.IsOpen = true;
    }
    private void ShowSettings()
    {
        if (panel is null || repository is null || trust is null || exiting) return;
        if (settings is not null) { settings.Activate(); return; }
        panel.SuppressDismiss = true;
        settings = new(repository, config, readOnly, trust, startup, hotkeyStatus);
        settings.Closed += (_, _) =>
        {
            if (exiting) return;
            if (settings?.Saved is { } saved)
            {
                config = saved; ThemeManager.Apply(config.Appearance.Theme);
                panel.MotionEnabled = config.Appearance.Motion != "off"; panel.Model.UpdateConfig(config);
                ApplyHotkey();
            }
            settings = null; panel.SuppressDismiss = false; panel.Present(tray);
        };
        settings.Show(); settings.Activate();
    }
    private void ApplyHotkey()
    {
        if (hotkeys is null) return;
        var result = hotkeys.Apply(config.Hotkey);
        hotkeyStatus = result.Outcome == CommandOutcome.Confirmed
            ? (hotkeys.Current is null ? "本会话快捷键已关闭。" : "本会话快捷键：" + hotkeys.Current.Label)
            : (result.Message ?? "快捷键注册未确认。") + " 当前：" + (hotkeys.Current?.Label ?? "未注册");
        if (result.Outcome != CommandOutcome.Confirmed && panel is not null) panel.Model.Notice = hotkeyStatus;
    }
    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(() => ThemeManager.Apply(config.Appearance.Theme));
    private void SystemSettingChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(() => ThemeManager.Apply(config.Appearance.Theme));
    private async void RequestExit()
    {
        if (exiting) return;
        exiting = true; panel?.Model.Dispose();
        settings?.PrepareForExit();
        if (panel is not null) panel.IsEnabled = false;
        await StopServicesAsync(); Shutdown();
    }
    private Task StopServicesAsync() => stopServices ??= Task.Run(async () =>
    {
        await ShutdownSequence.RunAsync([
            (ShutdownModule.Awake, () => awake?.DisposeAsync().AsTask() ?? Task.CompletedTask),
            (ShutdownModule.Coordinator, () => coordinator?.DisposeAsync().AsTask() ?? Task.CompletedTask),
            (ShutdownModule.Audio, () => audio?.DisposeAsync().AsTask() ?? Task.CompletedTask),
            (ShutdownModule.Power, () => power?.DisposeAsync().AsTask() ?? Task.CompletedTask)
        ], (module, result) => shutdownLog?.Write(module, result));
    });
    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true; panel?.Model.Dispose();
        SystemEvents.UserPreferenceChanged -= PreferenceChanged; SystemParameters.StaticPropertyChanged -= SystemSettingChanged;
        // Normal exit awaits cleanup asynchronously. Session shutdown uses a bounded last chance.
        try { StopServicesAsync().Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        hotkeys?.Dispose(); tray?.Dispose(); messageWindow?.Dispose(); instance?.Dispose(); panel?.Exit();
        base.OnExit(e);
    }
}

