using System.IO;
using System.Windows;
using ControlCenter.App.Views;
using ControlCenter.App.ViewModels;
using ControlCenter.Core;
// Explicitly interactive, excluded from ordinary build/test scripts. Requires explicit UI authorization.
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || args[0] != "--allow-ui")
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
        if (args.Length == 2)
        {
            app.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    Directory.CreateDirectory(args[1]);
                    await Task.Delay(700);
                    foreach (var theme in new[] { "light", "dark" })
                    {
                        ControlCenter.App.ThemeManager.Apply(theme);
                        panel.UpdateLayout();
                        Capture(panel, Path.Combine(args[1], theme + "-panel.png"));
                        var settings = new SettingsWindow(new MemoryConfig(config), config, false, new MemoryTrust());
                        settings.Show(); await Task.Delay(150); settings.UpdateLayout();
                        Capture(settings, Path.Combine(args[1], theme + "-settings-top.png"));
                        var combo = (System.Windows.Controls.ComboBox)settings.FindName("BackupList");
                        combo.ItemsSource = new[] { new ConfigBackup("config-202610020001.json", DateTime.UtcNow), new ConfigBackup("config-202610020002.json", DateTime.UtcNow) };
                        combo.SelectedIndex = 0;
                        combo.IsDropDownOpen = true; await Task.Delay(100);
                        var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
                        if (!popup.IsOpen || popup.Child is not FrameworkElement { ActualHeight: > 0 }) throw new Exception("Backup dropdown did not open");
                        combo.SelectedIndex = 1; combo.IsDropDownOpen = false;
                        settings.UpdateLayout();
                        var label = (System.Windows.Controls.TextBlock)combo.Template.FindName("SelectionLabel", combo);
                        if (label.Text != "config-202610020002.json") throw new Exception("Backup selection label incorrect");
                        var save = (System.Windows.Controls.Button)settings.FindName("SaveButton");
                        var before = save.TranslatePoint(new Point(), settings);
                        foreach (var scroll in Descendants(settings).OfType<System.Windows.Controls.ScrollViewer>()) scroll.ScrollToEnd();
                        settings.UpdateLayout();
                        var after = save.TranslatePoint(new Point(), settings);
                        if (before != after || after.Y + save.ActualHeight > settings.ActualHeight) throw new Exception("Save footer moved or clipped");
                        Capture(settings, Path.Combine(args[1], theme + "-settings.png"));
                        settings.Width = 440; settings.Height = 420; settings.UpdateLayout();
                        var smallPosition = save.TranslatePoint(new Point(), settings);
                        if (smallPosition.Y < 0 || smallPosition.Y + save.ActualHeight > settings.ActualHeight - 20) throw new Exception("Small window footer clipped");
                        var size = save.DesiredSize; save.Focus(); settings.UpdateLayout();
                        if (save.DesiredSize != size) throw new Exception("Keyboard focus changed button size");
                        Capture(settings, Path.Combine(args[1], theme + "-settings-small.png"));
                        settings.PrepareForExit(); settings.Close();
                    }
                    model.Mute.Execute(null); await Task.Delay(100);
                    if (!(await audio.ReadLevelAsync("fake", CancellationToken.None)).Muted) throw new Exception("Mute did not reach fake audio");
                    session.Start.Execute("30"); await Task.Delay(100);
                    session.Stop.Execute(null); await Task.Delay(100);
                    File.WriteAllText(Path.Combine(args[1], "result.txt"), "PASS: light/dark rendered; save footer stationary during scrolling and visible at 440x420; keyboard focus preserves button size; backup popup opens and selection label updates; mute reached fake audio; awake start/stop commands exercised; fake services only.");
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "result.txt"), "FAIL: " + ex); Environment.ExitCode = 1; }
                finally { panel.Exit(); app.Shutdown(); }
            });
        }
        app.Run();
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return Environment.ExitCode;
    }
    private static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private sealed class MemoryConfig(AppConfig config) : IConfigRepository
    {
        public Task<ConfigLoadResult> LoadAsync(CancellationToken ct = default) => Task.FromResult(new ConfigLoadResult(config));
        public Task SaveAsync(AppConfig value, CancellationToken ct = default) { config = value; return Task.CompletedTask; }
    }
    private sealed class MemoryTrust : IShortcutTrustStore
    {
        public bool IsTrusted(ShortcutDefinition shortcut) => false;
        public Task ConfirmAsync(ShortcutDefinition shortcut, CancellationToken ct) => Task.CompletedTask;
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

