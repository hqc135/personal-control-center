using System.Windows;
using ControlCenter.App.Views;
using ControlCenter.App.ViewModels;
using ControlCenter.Core;
// Explicitly interactive, excluded from ordinary build/test scripts. Never run while the user is gaming.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--allow-ui")
        { Console.Error.WriteLine("This tool opens a window. Run only with explicit UI-test authorization and --allow-ui."); return 2; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PersonalControlCenter;component/Themes/Controls.xaml") });
        var audio = new FakeAudio(); var power = new FakePower();
        var coordinator = new ControlCoordinator(audio, power);
        Action<Action> dispatch = action => app.Dispatcher.BeginInvoke(action);
        var config = new AppConfig { Proxy = new(TestUrl: "https://example.com/") };
        var session = new SessionViewModel(new FakeAwake(), new ProxyMonitor(new FakeProxy(), dispatch), new FakeLauncher(), config, dispatch);
        var model = new PanelViewModel(coordinator, new FakeLauncher(), config, dispatch, session)
        { DataOriginLabel = "演示数据 · UI 验收工具", Notice = "所有服务是假实现，不会改动系统。" };
        var panel = new ControlPanel(model) { Title = "演示数据 · UI 验收工具", CanHide = false, SuppressDismiss = true };
        panel.SettingsRequested += () =>
        {
            var menu = new System.Windows.Controls.ContextMenu();
            var exit = new System.Windows.Controls.MenuItem { Header = "退出 UI 工具" };
            exit.Click += (_, _) => { panel.Exit(); app.Shutdown(); };
            menu.Items.Add(exit); menu.IsOpen = true;
        };
        panel.ExitRequested += () => { panel.Exit(); app.Shutdown(); };
        panel.Present();
        app.Run();
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return 0;
    }
    private sealed class FakeAudio : IAudioService
    {
        private AudioLevel level = new("fake", .6f, false);
        public event Action? Invalidated;
        public Task<AudioSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new AudioSnapshot([new("fake", "演示扬声器")], "fake", null, level));
        public Task<AudioLevel> ReadLevelAsync(string id, CancellationToken ct) => Task.FromResult(level);
        public Task SetVolumeAsync(string id, float value, CancellationToken ct) { level = level with { Volume = value }; Invalidated?.Invoke(); return Task.CompletedTask; }
        public Task SetMuteAsync(string id, bool value, CancellationToken ct) { level = level with { Muted = value }; Invalidated?.Invoke(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakePower : IPowerService
    {
        private static readonly Guid id = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");
        public event Action? Invalidated;
        public Task<PowerSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new PowerSnapshot([new(id, "演示方案")], id, true));
        public Task ActivateAsync(Guid value, CancellationToken ct) { Invalidated?.Invoke(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeLauncher : IShortcutLauncher
    {
        public Task<CommandResult> LaunchAsync(ShortcutDefinition value, CancellationToken ct) => Task.FromResult(new CommandResult(CommandOutcome.Confirmed, Message: "演示入口：未打开外部程序。"));
        public Task<CommandResult> OpenSoundSettingsAsync(CancellationToken ct) => Task.FromResult(new CommandResult(CommandOutcome.Confirmed, Message: "演示入口：未打开系统设置。"));
    }
    private sealed class FakeAwake : IAwakeService
    {
        public AwakeSnapshot Current { get; private set; } = new(AwakeStatus.Off);
        public event Action? Changed;
        public Task<CommandResult> StartAsync(int minutes, bool display)
        { Current = new(AwakeStatus.Active, DateTimeOffset.UtcNow.AddMinutes(minutes), display, Remaining: TimeSpan.FromMinutes(minutes)); Changed?.Invoke(); return Task.FromResult(CommandResult.Confirmed); }
        public Task<CommandResult> StopAsync()
        { Current = new(AwakeStatus.Off); Changed?.Invoke(); return Task.FromResult(CommandResult.Confirmed); }
        public Task CheckAsync(bool resumed = false) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeProxy : IProxyProbe
    {
        public Task<ProxyResult> LocalAsync(ProxyConfig config, CancellationToken ct) => Task.FromResult(new ProxyResult(ProxyResultKind.LocalReachable));
        public Task<ProxyResult> RemoteAsync(ProxyConfig config, CancellationToken ct) => Task.FromResult(new ProxyResult(ProxyResultKind.HttpResponse, 403));
    }
}

