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
        if (args.Length is < 1 or > 2 || args[0] is not ("--allow-ui" or "--allow-ui-themes" or "--allow-ui-continuity"))
        { Console.Error.WriteLine("This tool opens a window. Run only with explicit UI-test authorization and --allow-ui."); return 2; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PersonalControlCenter;component/Themes/Controls.xaml") });
        var audio = new FakeAudio(); var power = new FakePower();
        var coordinator = new ControlCoordinator(audio, power);
        Action<Action> dispatch = action => app.Dispatcher.BeginInvoke(action);
        var config = new AppConfig { Proxy = new(TestUrl: "https://example.com/") };
        var awake = new FakeAwake();
        var desktopService = new FakeDesktop();
        var desktop = new DesktopViewModel(new FakeLauncher(), desktopService, scenes: new SceneRunner(coordinator, awake), audio: desktopService, brightness: desktopService);
        var session = new SessionViewModel(awake, new ProxyMonitor(new FakeProxy(), dispatch), new FakeLauncher(), config, dispatch);
        var model = new PanelViewModel(coordinator, new FakeLauncher(), config, dispatch, session, desktop)
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
                    if (args[0] == "--allow-ui-continuity") { await CheckContinuity(panel, model, coordinator, audio, desktopService, args[1]); return; }
                    if (args[0] == "--allow-ui-themes") { await CheckThemes(panel, config, args[1]); return; }
                    await Task.Delay(700);
                    panel.MotionEnabled = false;
                    var handle = new System.Windows.Interop.WindowInteropHelper(panel).Handle;
                    var caption = panel.PointToScreen(new Point(80, 30));
                    var edge = panel.PointToScreen(new Point(2, 100));
                    if (HitTest(handle, caption) != 2 || HitTest(handle, edge) != 10) throw new Exception("Native caption/resize hit tests failed");
                    var settingsButton = (FrameworkElement)panel.FindName("SettingsButton");
                    var buttonPoint = settingsButton.PointToScreen(new Point(settingsButton.ActualWidth / 2, settingsButton.ActualHeight / 2));
                    if (HitTest(handle, buttonPoint) != 1) throw new Exception("Settings button became draggable caption");
                    panel.Width = 460; panel.Height = 560; panel.Left += 20; panel.Top += 20; panel.UpdateLayout();
                    SendMessage(handle, 0x0232, 0, 0);
                    var position = panel.PointToScreen(new Point());
                    panel.Hide(); panel.Present(); panel.UpdateLayout();
                    var restored = panel.PointToScreen(new Point());
                    if (Math.Abs(panel.ActualWidth - 460) > 2 || Math.Abs(panel.ActualHeight - 560) > 2 || (restored - position).Length > 2) throw new Exception("Panel geometry lost on reopen");
                    Capture(panel, Path.Combine(args[1], "resized-panel.png"));
                    foreach (var theme in new[] { "light", "dark" })
                    {
                        ControlCenter.App.ThemeManager.Apply(theme);
                        panel.Width = 460; panel.Height = 720;
                        panel.UpdateLayout();
                        var powerCard = (FrameworkElement)panel.FindName("PowerCard");
                        var awakeCard = (FrameworkElement)panel.FindName("AwakeCard");
                        if (Math.Abs(powerCard.TranslatePoint(new Point(), panel).Y - awakeCard.TranslatePoint(new Point(), panel).Y) > 1) throw new Exception("Compact cards did not share a row");
                        Capture(panel, Path.Combine(args[1], theme + "-panel.png"));
                        panel.Width = 340; panel.Height = 360; panel.UpdateLayout();
                        if (awakeCard.TranslatePoint(new Point(), panel).Y < powerCard.TranslatePoint(new Point(), panel).Y + powerCard.ActualHeight) throw new Exception("Narrow cards overlap");
                        Capture(panel, Path.Combine(args[1], theme + "-panel-small.png"));
                        foreach (var expander in Descendants(panel).OfType<System.Windows.Controls.Expander>()) expander.IsExpanded = true;
                        panel.UpdateLayout();
                        var panelScroll = Descendants(panel).OfType<System.Windows.Controls.ScrollViewer>().First();
                        panelScroll.ScrollToEnd(); panel.UpdateLayout();
                        var shortcuts = (FrameworkElement)panel.FindName("ShortcutsCard");
                        if (shortcuts.TranslatePoint(new Point(), panelScroll).Y + shortcuts.ActualHeight > panelScroll.ActualHeight + 1) throw new Exception("Expanded narrow panel cannot reach last card");
                        Capture(panel, Path.Combine(args[1], theme + "-panel-expanded-small.png"));
                        foreach (var expander in Descendants(panel).OfType<System.Windows.Controls.Expander>()) expander.IsExpanded = false;
                        panelScroll.ScrollToTop(); panel.Width = 460; panel.Height = 720; panel.UpdateLayout();
                        var settings = new SettingsWindow(new MemoryConfig(config), config, false, new MemoryTrust());
                        var pin = (System.Windows.Controls.Button)panel.FindName("PinButton");
                        pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        if (!panel.IsPinned) throw new Exception("Pin did not activate");
                        panel.SuppressDismiss = false; panel.CanHide = true;
                        settings.Show(); await Task.Delay(150); settings.UpdateLayout();
                        if (!panel.IsVisible) throw new Exception("Pinned panel dismissed on deactivation");
                        panel.SuppressDismiss = true; panel.CanHide = false;
                        pin.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        if (panel.IsPinned) throw new Exception("Pin did not release");
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
                    model.SearchText = "不存在的入口";
                    if (model.FilteredShortcuts.Count != 0) throw new Exception("Shortcut search did not filter");
                    model.SearchText = "下载";
                    if (model.FilteredShortcuts.Count != 1) throw new Exception("Shortcut search did not find configured item");
                    model.SearchText = "";
                    await desktop.RefreshAsync();
                    await desktop.ToggleMicrophoneAsync();
                    if (desktop.Snapshot?.Microphone?.Muted != true) throw new Exception("Microphone state not read back");
                    await desktop.SetBrightnessAsync("display", 42);
                    if (desktop.Displays.Single().Percent != 42) throw new Exception("Brightness state not read back");
                    await desktop.MediaAsync("player", "toggle");
                    if (desktop.Media.Single().Playing) throw new Exception("Media operation not reflected");
                    await desktop.SwitchOutputAsync("fake");
                    if (desktopService.Output != "fake") throw new Exception("Output target lost");
                    var beforeScene = await audio.ReadLevelAsync("fake", default);
                    await desktop.Scenes!.ApplyAsync(new("演示场景", Volume: 25, Muted: false, AwakeMinutes: 30));
                    var sceneState = await audio.ReadLevelAsync("fake", default);
                    if (Math.Abs(sceneState.Volume - .25) > .01 || sceneState.Muted || awake.Current.Status != AwakeStatus.Active) throw new Exception("Scene actions incomplete");
                    await desktop.Scenes.RestoreAsync();
                    if (await audio.ReadLevelAsync("fake", default) != beforeScene || awake.Current.Status != AwakeStatus.Off) throw new Exception("Scene restore incomplete");
                    await desktop.Scenes.ApplyAsync(new("保留后续修改", Volume: 25));
                    await audio.SetVolumeAsync("fake", .8f, default);
                    await desktop.Scenes.RestoreAsync();
                    if (Math.Abs((await audio.ReadLevelAsync("fake", default)).Volume - .8) > .01) throw new Exception("Scene restore overwrote later manual adjustment");
                    ((System.Windows.Controls.Button)panel.FindName("AllTab")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    await Task.Delay(150); panel.UpdateLayout();
                    foreach (var theme in new[] { "light", "dark" })
                    {
                        ControlCenter.App.ThemeManager.Apply(theme); panel.UpdateLayout();
                        var scroll = (System.Windows.Controls.ScrollViewer)panel.FindName("PanelScroll");
                        scroll.ScrollToVerticalOffset(500); panel.UpdateLayout(); Capture(panel, Path.Combine(args[1], theme + "-all-features.png"));
                        scroll.ScrollToEnd(); panel.UpdateLayout(); Capture(panel, Path.Combine(args[1], theme + "-all-tools.png"));
                        var sceneWindow = new SceneWindow(desktop, model.PowerChoices); sceneWindow.Show(); await Task.Delay(100); sceneWindow.UpdateLayout();
                        Capture(sceneWindow, Path.Combine(args[1], theme + "-scenes.png")); sceneWindow.Close();
                        var updateWindow = new UpdateWindow(new ControlCenter.Windows.UpdatePackages(Path.Combine(args[1], "fake-versions")));
                        updateWindow.Show(); await Task.Delay(100); updateWindow.UpdateLayout(); Capture(updateWindow, Path.Combine(args[1], theme + "-updates.png")); updateWindow.Close();
                    }
                    if (model.HasMultiplePowerSchemes || model.HasMultipleOutputs) throw new Exception("Single-device capability incorrectly enabled");
                    if (!Descendants(panel).OfType<System.Windows.Controls.TextBlock>().Any(t => t.Text == "42%")) throw new Exception("Brightness percentage missing");
                    desktopService.Empty = true; await desktop.RefreshAsync(); panel.UpdateLayout();
                    if (((FrameworkElement)panel.FindName("MediaCard")).Visibility != Visibility.Collapsed) throw new Exception("Empty media card remained visible");
                    if (!desktop.BrightnessHint.Contains("未发现") || desktop.MuteMicrophone.CanExecute(null)) throw new Exception("Unsupported controls not represented");
                    Capture(panel, Path.Combine(args[1], "no-capabilities.png"));
                    File.WriteAllText(Path.Combine(args[1], "result.txt"), "PASS: search, microphone, brightness, media, output switch, scene execution/restore and preservation of later manual changes; favorites/all layout; pin/unpin and pinned deactivation; compact/narrow/expanded layout; native hit tests and retained geometry; settings footer, focus and backup dropdown; fake services only.");
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "result.txt"), "FAIL: " + ex); Environment.ExitCode = 1; }
                finally { panel.Exit(); app.Shutdown(); }
            });
        }
        app.Run();
        coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return Environment.ExitCode;
    }
    private static async Task CheckContinuity(ControlPanel panel, PanelViewModel model, ControlCoordinator coordinator, FakeAudio audio, FakeDesktop desktop, string output)
    {
        await Task.Delay(250); panel.MotionEnabled = true; panel.CanHide = true;
        panel.Dismiss(); await Task.Delay(20);
        var root = (FrameworkElement)panel.FindName("ContentRoot"); var opacity = root.Opacity;
        panel.Present();
        if (root.Opacity > opacity + .08) throw new Exception("Reopening jumped instead of reversing");
        await Task.Delay(220);
        if (!panel.IsVisible || panel.Phase != PanelPhase.Open) throw new Exception("Old close completion hid reopened panel");
        var volume = (System.Windows.Controls.Slider)panel.FindName("VolumeSlider");
        var value = (FrameworkElement)panel.FindName("VolumeValue");
        model.BeginVolumeGesture(); model.Volume = 6; panel.UpdateLayout(); var bounds = value.RenderSize;
        model.Volume = 100; panel.UpdateLayout(); if (value.RenderSize != bounds || volume.Value != 100) throw new Exception("Volume tracking or number width changed");
        model.EndVolumeGesture(); await Task.Delay(100);
        audio.Multiple = true; await coordinator.RefreshAsync();
        var trigger = (FrameworkElement)panel.FindName("DeviceButton"); var anchor = trigger.TranslatePoint(new Point(), panel);
        model.ToggleDevices.Execute(null); await Task.Delay(100); panel.UpdateLayout();
        if ((trigger.TranslatePoint(new Point(), panel) - anchor).Length > .1) throw new Exception($"Device expansion moved trigger: {anchor} -> {trigger.TranslatePoint(new Point(), panel)}");
        var picker = (System.Windows.Controls.ComboBox)panel.FindName("OutputPicker"); picker.SelectedItem = model.OutputDevices.Single(x => x.Id == "next");
        var selected = picker.SelectedItem; await coordinator.RefreshAsync(); panel.UpdateLayout();
        if (!ReferenceEquals(selected, picker.SelectedItem)) throw new Exception("Refresh reset output selection");
        var gate = new TaskCompletionSource<bool>(); desktop.SwitchHandler = async id => { if (!await gate.Task) throw new IOException("演示切换失败，原设备保留"); audio.DefaultId = id; };
        var request = model.SelectOutputAsync("next"); panel.UpdateLayout();
        if (!model.OutputBusy || !model.DevicesOpen || model.Device != "演示扬声器") throw new Exception("Output waiting state lost original device");
        await Task.Delay(60); panel.UpdateLayout();
        File.WriteAllText(Path.Combine(output, "visible-labels.txt"), string.Join("\n", Descendants(panel).OfType<System.Windows.Controls.TextBlock>().Where(t => t.Text == model.Device || t.Text == model.PowerSource).Select(t => $"{t.Text}: {t.Foreground} {t.Visibility} {t.ActualWidth}x{t.ActualHeight}")));
        Capture(panel, Path.Combine(output, "output-pending.png"));
        gate.SetResult(false); await request; panel.UpdateLayout();
        if (!model.DevicesOpen || model.OutputBusy || !model.OutputStatus.Contains("失败")) throw new Exception("Failure collapsed output list");
        Capture(panel, Path.Combine(output, "output-failed.png"));
        gate = new TaskCompletionSource<bool>(); request = model.SelectOutputAsync("next"); gate.SetResult(true); await request; panel.UpdateLayout();
        if (model.DevicesOpen || model.Device != "演示耳机") throw new Exception("Confirmed switch did not close list");
        var power = (FrameworkElement)panel.FindName("PowerCard"); var powerPosition = power.TranslatePoint(new Point(), panel);
        var host = (System.Windows.Controls.Panel)panel.FindName("ModuleHost"); var first = host.Children[0];
        model.Notice = new string('错', 150); await model.Desktop.RefreshAsync(); panel.UpdateLayout();
        if (power.TranslatePoint(new Point(), panel) != powerPosition || !ReferenceEquals(first, host.Children[0])) throw new Exception("Status refresh moved/replaced controls");
        Capture(panel, Path.Combine(output, "stable-feedback.png"));
        panel.SuppressDismiss = false;
        var outside = new Window { Title = "演示外部窗口 · 无系统操作", Width = 220, Height = 100, ShowInTaskbar = false };
        outside.Show(); outside.Activate(); await Task.Delay(250);
        if (panel.IsVisible) throw new Exception("Outside activation did not close panel");
        outside.Close();
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: reversible open/close, direct volume tracking and fixed number width, stable device trigger and refresh selection, pending/failure/success output switch, fixed feedback layout, outside dismissal. Fake services only.");
    }
    private static async Task CheckThemes(ControlPanel panel, AppConfig config, string output)
    {
        await Task.Delay(300); panel.MotionEnabled = false;
        foreach (var palette in AppearanceStyles.Palettes)
            foreach (string mode in new[] { "light", "dark" })
            {
                var appearance = new AppearanceConfig(Theme: mode, Palette: palette.Id);
                ControlCenter.App.ThemeManager.Apply(appearance); panel.UpdateLayout();
                var expected = AppearanceStyles.Resolve(appearance, mode == "dark")["background"];
                if (!SystemParameters.HighContrast && ((System.Windows.Media.SolidColorBrush)panel.Background).Color.ToString() != "#FF" + expected[1..]) throw new Exception("Panel palette did not update");
                Capture(panel, Path.Combine(output, palette.Id + "-" + mode + ".png"));
            }
        ControlCenter.App.ThemeManager.Apply(config.Appearance);
        var baseline = Application.Current.Resources["AccentBrush"];
        var repo = new MemoryConfig(config);
        var settings = new SettingsWindow(repo, config, false, new MemoryTrust());
        settings.Show(); await Task.Delay(150);
        ((System.Windows.Controls.ComboBox)settings.FindName("PalettePicker")).SelectedValue = "mint";
        ((System.Windows.Controls.RadioButton)settings.FindName("LightTheme")).IsChecked = true;
        var editor = (System.Windows.Controls.TextBox)settings.FindName("CssEditor");
        editor.Text = ":root { --accent: #7356BF; --card-radius: 24px; --card-padding: 20px; --control-radius: 12px; }";
        void Click(string label) => Descendants(settings).OfType<System.Windows.Controls.Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Click("预览配色"); settings.UpdateLayout();
        if (!ReferenceEquals(baseline, Application.Current.Resources["AccentBrush"])) throw new Exception("Preview leaked into application resources");
        if (((CornerRadius)settings.Resources["CardRadius"]).TopLeft != 24) throw new Exception("CSS radius not applied");
        var valid = settings.Resources["AccentBrush"];
        var css = editor.Text; editor.Text = ":root { --accent: invalid; }"; Click("预览配色");
        if (!ReferenceEquals(valid, settings.Resources["AccentBrush"])) throw new Exception("Invalid CSS changed preview");
        editor.Text = css; Click("预览配色");
        Descendants(settings).OfType<System.Windows.Controls.Expander>().Single(e => Equals(e.Header, "自定义 CSS 主题")).IsExpanded = true;
        settings.UpdateLayout(); Capture(settings, Path.Combine(output, "custom-css-settings.png"));
        settings.Width = 440; settings.Height = 420; settings.UpdateLayout();
        var save = (System.Windows.Controls.Button)settings.FindName("SaveButton");
        var position = save.TranslatePoint(new Point(), settings);
        if (position.Y < 0 || position.Y + save.ActualHeight > settings.ActualHeight) throw new Exception("Save footer clipped");
        Capture(settings, Path.Combine(output, "custom-css-small.png"));
        save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        for (int i = 0; i < 40 && settings.Saved is null; i++) await Task.Delay(50);
        if (settings.Saved?.Appearance.CustomCss != css) throw new Exception("Theme did not save");
        var loaded = (await repo.LoadAsync()).Config;
        ControlCenter.App.ThemeManager.Apply(loaded.Appearance); panel.UpdateLayout();
        var tile = (System.Windows.Controls.Border)panel.FindName("BrightnessCard");
        if (tile.CornerRadius.TopLeft != 24 || tile.Padding.Left != 20) throw new Exception("Saved CSS geometry did not reach panel");
        Capture(panel, Path.Combine(output, "custom-css-panel.png"));
        var reopened = new SettingsWindow(repo, loaded, false, new MemoryTrust());
        reopened.Show(); await Task.Delay(100);
        if (((System.Windows.Controls.TextBox)reopened.FindName("CssEditor")).Text != css || !Equals(((System.Windows.Controls.ComboBox)reopened.FindName("PalettePicker")).SelectedValue, "mint")) throw new Exception("Theme draft did not reload");
        reopened.PrepareForExit(); reopened.Close();
        File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: six palettes in light/dark, scoped CSS preview, invalid CSS rollback, CSS geometry, saved theme reload and small settings footer. Fake services only.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    private static long HitTest(nint hwnd, Point point) => SendMessage(hwnd, 0x0084, 0, (nint)((((int)point.Y & 0xffff) << 16) | ((int)point.X & 0xffff))).ToInt64();
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
        public bool Multiple;
        public string DefaultId = "fake";
        private AudioLevel level = new("fake", .6f, false);
        public event Action? Invalidated;
        public Task<AudioSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new AudioSnapshot(Multiple ? [new("fake", "演示扬声器"), new("next", "演示耳机")] : [new("fake", "演示扬声器")], DefaultId, null, level with { EndpointId = DefaultId }));
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
    private sealed class FakeDesktop : IDesktopFeatures, IAudioDevices, IBrightnessService
    {
        public Task<MicrophoneState?> ReadMicrophoneAsync(CancellationToken ct) => Task.FromResult<MicrophoneState?>(mic);
        Task<DisplayBrightness[]> IBrightnessService.ReadAsync(CancellationToken ct) => Task.FromResult(new[] { display });
        public bool Empty;
        private MicrophoneState mic = new("mic", "演示麦克风", false);
        private DisplayBrightness display = new("display", "演示显示屏", 65);
        private MediaSession media = new("player", "演示曲目", "演示播放器", true, true, true, true);
        public Func<string, Task>? SwitchHandler;
        public string? Output { get; private set; }
        public Task<DesktopSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new DesktopSnapshot("82% · 已接电源", "演示网络 · 链路已连接", "演示蓝牙 · 已开启", Empty ? null : mic, Empty ? [] : [display], Empty ? [] : [media], []));
        public Task SetMicrophoneMuteAsync(string id, bool muted, CancellationToken ct) { mic = mic with { Muted = muted }; return Task.CompletedTask; }
        public Task SetAsync(string id, int value, CancellationToken ct) { display = display with { Percent = value }; return Task.CompletedTask; }
        public Task MediaAsync(string id, string action, CancellationToken ct) { media = media with { Playing = !media.Playing }; return Task.CompletedTask; }
        public async Task SwitchOutputAsync(string id, CancellationToken ct) { Output = id; if (SwitchHandler is not null) await SwitchHandler(id); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
