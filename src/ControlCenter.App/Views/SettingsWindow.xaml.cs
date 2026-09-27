using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using ControlCenter.Core;
using ControlCenter.Windows;
using Microsoft.Win32;
namespace ControlCenter.App.Views;
public partial class SettingsWindow : Window
{
    private readonly IConfigRepository repository;
    private readonly IShortcutTrustStore trust;
    private AppConfig original;
    private readonly ObservableCollection<ShortcutDefinition> shortcuts;
    private bool readOnly;
    private bool ready, dirty, accepted;
    private string? proxyShortcutId;
    private readonly ObservableCollection<ModuleOption> modules = [];
    private readonly UserStartupService? startup;
    private StartupSnapshot? startupSnapshot;
    private bool saving;
    private readonly OperationLifetime fileOperation = new();
    private bool closing;
    public AppConfig? Saved { get; private set; }
    public string? SavedWarning { get; private set; }
    public SettingsWindow(IConfigRepository repository, AppConfig config, bool readOnly, IShortcutTrustStore trust, UserStartupService? startup = null, string? hotkeyStatus = null, string? configWarning = null)
    {
        this.repository = repository; original = config; this.trust = trust; this.readOnly = readOnly;
        InitializeComponent();
        this.startup = startup;
        shortcuts = new(config.Shortcuts); ShortcutList.ItemsSource = shortcuts;
        ModuleList.ItemsSource = modules;
        FontFamily = ThemeManager.PreferredFont();
        LoadDraft(config);
        HotkeyStatus.Text = hotkeyStatus ?? "本会话未注册快捷键。";
        Loaded += async (_, _) => await LoadLocalOptionsAsync();
        SaveButton.IsEnabled = !readOnly;
        StartupButtons.IsEnabled = !readOnly && startup is not null;
        if (configWarning is not null) Status.Text = configWarning;
        else if (readOnly) Status.Text = "当前配置受只读保护，请修复后重新载入。";
        ready = true;
        Closed += (_, _) => { closing = true; fileOperation.Dispose(); };
    }
    private void LoadDraft(AppConfig config)
    {
        ready = false; original = config;
        shortcuts.Clear(); foreach (var item in config.Shortcuts) shortcuts.Add(item);
        modules.Clear();
        foreach (var id in config.Modules.Concat(new[] { "audio", "power", "awake", "proxy", "shortcuts" }.Except(config.Modules)))
            modules.Add(new(id, config.Modules.Contains(id)));
        HotkeyText.Text = config.Hotkey?.Label ?? "";
        SystemTheme.IsChecked = config.Appearance.Theme == "system";
        LightTheme.IsChecked = config.Appearance.Theme == "light";
        DarkTheme.IsChecked = config.Appearance.Theme == "dark";
        Motion.IsChecked = config.Appearance.Motion != "off";
        ProxyHost.Text = config.Proxy.Host; ProxyPort.Text = config.Proxy.Port.ToString();
        ProxyTarget.Text = config.Proxy.TestUrl ?? ""; proxyShortcutId = config.Proxy.ShortcutId;
        UpdateProxyLabel();
        ready = true;
    }
    private AppConfig ReadDraft()
    {
        if (!int.TryParse(ProxyPort.Text, out int port)) throw new InvalidDataException("端口须为 1–65535。");
        var theme = DarkTheme.IsChecked == true ? "dark" : LightTheme.IsChecked == true ? "light" : "system";
        var config = original with {
            Appearance = original.Appearance with { Theme = theme, Motion = Motion.IsChecked == true ? "subtle" : "off" },
            Shortcuts = shortcuts.ToArray(), Modules = modules.Where(x => x.Enabled).Select(x => x.Id).ToArray(), Hotkey = HotkeyPolicy.Parse(HotkeyText.Text),
            Proxy = original.Proxy with { Host = ProxyHost.Text.Trim(), Port = port, TestUrl = string.IsNullOrWhiteSpace(ProxyTarget.Text) ? null : ProxyTarget.Text.Trim(), ShortcutId = proxyShortcutId }
        };
        ConfigCodec.Validate(config); return config;
    }
    private void Edited(object sender, RoutedEventArgs e) { if (ready) dirty = true; }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly || saving) return;
        saving = true; IsEnabled = false;
        try { var config = ReadDraft(); await Task.Run(() => repository.SaveAsync(config)); if (closing) return; Saved = config; SavedWarning = (repository as IConfigSaveStatus)?.LastSaveWarning; accepted = true; Close(); }
        catch (ConfigConflictException ex) { if (closing) return; Status.Text = ex.Message; SaveButton.IsEnabled = true; }
        catch (InvalidDataException ex) { if (closing) return; Status.Text = ex.Message; SaveButton.IsEnabled = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (closing) return; Status.Text = "保存失败，旧配置或备份保留；请检查目录权限与输入。"; SaveButton.IsEnabled = true; }
        finally { saving = false; if (!closing) IsEnabled = true; }
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly) return;
        if (shortcuts.Count >= 20) { Status.Text = "最多保存 20 个入口。"; return; }
        var editor = new ShortcutEditorWindow { Owner = this };
        if (editor.ShowDialog() == true && editor.Entry is { } entry) { shortcuts.Add(entry); dirty = true; }
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly || ShortcutList.SelectedItem is not ShortcutDefinition selected) return;
        if (selected.Kind == "knownFolder") { Status.Text = "系统下载目录由 Windows 解析；可移除后另加自定义目录。"; return; }
        var editor = new ShortcutEditorWindow(selected) { Owner = this };
        if (editor.ShowDialog() == true && editor.Entry is { } entry) { shortcuts[shortcuts.IndexOf(selected)] = entry; dirty = true; }
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!readOnly && ShortcutList.SelectedItem is ShortcutDefinition selected)
        { shortcuts.Remove(selected); if (proxyShortcutId == selected.Id) proxyShortcutId = null; UpdateProxyLabel(); dirty = true; }
    }
    private void UpdateProxyLabel() => ProxyEntryLabel.Text = shortcuts.FirstOrDefault(x => x.Id == proxyShortcutId)?.Label ?? "未绑定代理程序";
    private void BindProxy_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly) return;
        if (ShortcutList.SelectedItem is not ShortcutDefinition { Kind: "application" } selected) { Status.Text = "请先选择一个程序入口。"; return; }
        proxyShortcutId = selected.Id; dirty = true; UpdateProxyLabel();
    }
    private void ClearProxy_Click(object sender, RoutedEventArgs e) { if (!readOnly) { proxyShortcutId = null; dirty = true; UpdateProxyLabel(); } }
    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly || ShortcutList.SelectedItem is not ShortcutDefinition { Kind: "application" } selected) { Status.Text = "请先选择一个程序入口。"; return; }
        var preview = "目标：" + selected.Target + "\n工作目录：" + (selected.WorkingDirectory ?? "程序所在目录") +
            "\n参数（每行一个）：" + "\n" + string.Join("\n", selected.Arguments ?? []) + "\n\n只确认此入口，不会立即启动。是否继续？";
        if (MessageBox.Show(this, preview, "核对程序入口", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await trust.ConfirmAsync(selected, CancellationToken.None); if (closing) return; Status.Text = "本机已确认此目标与参数；入口配置仍需保存。"; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { if (!closing) Status.Text = "本机确认记录保存失败，程序仍不可启动。"; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void ModuleUp_Click(object sender, RoutedEventArgs e) => MoveModule(-1);
    private void ModuleDown_Click(object sender, RoutedEventArgs e) => MoveModule(1);
    private void MoveModule(int offset)
    {
        if (readOnly || ModuleList.SelectedItem is not ModuleOption selected) return;
        int index = modules.IndexOf(selected), next = index + offset;
        if (next < 0 || next >= modules.Count) return;
        modules.Move(index, next); ModuleList.SelectedItem = selected; dirty = true;
    }
    private async Task LoadLocalOptionsAsync()
    {
        try
        {
            if (repository is IConfigRecovery recovery)
            {
                var backups = await Task.Run(() => recovery.ListBackupsAsync());
                if (closing) return;
                BackupList.ItemsSource = backups;
            }
            if (startup is not null) { var snapshot = await Task.Run(startup.Read); if (closing) return; startupSnapshot = snapshot; StartupStatus.Text = snapshot.Message; }
            else StartupStatus.Text = "本模式不提供真实自启操作。";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException) { if (!closing) StartupStatus.Text = "本机选项读取失败；未修改任何登记。"; }
    }
    private static string ConfigError(Exception ex) => ex is System.Text.Json.JsonException json
        ? $"JSON 格式错误，行 {json.LineNumber + 1}，位置 {json.BytePositionInLine + 1}。原配置未修改。"
        : ex is FutureConfigException ? "配置来自更高版本，不能导入覆盖。"
        : ex is InvalidDataException ? ex.Message : "配置操作失败，当前已保存配置未修改。";
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly) return;
        var dialog = new OpenFileDialog { Filter = "JSON 配置|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunFileOperationAsync(async token =>
        {
            var preview = ConfigurationTransfer.Preview(await ConfigFiles.ReadAsync(dialog.FileName, token));
            if (!fileOperation.CanApply(token)) return;
            LoadDraft(preview.Config); dirty = true;
            Status.Text = $"已载入导入草稿，移除 {preview.RemovedLocalEntries} 个需重新配置的入口；快捷键和测试目标已清空。尚未保存。";
        });
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON 配置|*.json", FileName = "PersonalControlCenter.portable.json", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        await RunFileOperationAsync(async token =>
        {
            await ConfigFiles.ExportAsync(dialog.FileName, ReadDraft(), token);
            if (fileOperation.CanApply(token)) Status.Text = "便携配置已导出；本机路径、网页、快捷键和信任未导出。";
        });
    }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (readOnly || repository is not IConfigRecovery recovery || BackupList.SelectedItem is not ConfigBackup backup) return;
        await RunFileOperationAsync(async token =>
        {
            var draft = await recovery.ReadBackupAsync(backup.Id, token);
            if (!fileOperation.CanApply(token)) return;
            LoadDraft(draft); dirty = true; Status.Text = "本机备份已读入草稿，检查后保存才生效；尚未执行系统操作。";
        });
    }
    private async Task RunFileOperationAsync(Func<CancellationToken, Task> action)
    {
        var token = fileOperation.TryBegin();
        if (token is null) return;
        IsEnabled = false;
        try { await action(token.Value); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        { if (fileOperation.CanApply(token.Value)) Status.Text = ConfigError(ex); }
        finally { fileOperation.Complete(token.Value); if (!closing) IsEnabled = true; }
    }
    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (dirty && MessageBox.Show(this, "重新载入会放弃未保存的草稿，继续？", "重新载入配置", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunFileOperationAsync(async token =>
        {
            var loaded = await Task.Run(() => repository.LoadAsync(token), token);
            if (!fileOperation.CanApply(token)) return;
            readOnly = loaded.ReadOnly; LoadDraft(loaded.Config); dirty = false;
            SaveButton.IsEnabled = !readOnly; StartupButtons.IsEnabled = !readOnly && startup is not null;
            Status.Text = loaded.Warning ?? "已读取当前文件；保存后才应用到面板。";
        });
    }
    private async void EnableStartup_Click(object sender, RoutedEventArgs e) => await SetStartupAsync(true);
    private async void DisableStartup_Click(object sender, RoutedEventArgs e) => await SetStartupAsync(false);
    private async Task SetStartupAsync(bool enabled)
    {
        if (readOnly || startup is null || startupSnapshot is null) return;
        StartupButtons.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => startup.Set(enabled, startupSnapshot));
            startupSnapshot = await Task.Run(startup.Read);
            if (closing) return;
            StartupStatus.Text = (result.Message ?? "操作结果待确认。") + " " + startupSnapshot.Message;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException) { if (!closing) StartupStatus.Text = "自启操作未确认，请刷新登记状态。"; }
        finally { if (!closing) StartupButtons.IsEnabled = !readOnly; }
    }
    public void PrepareForExit() { accepted = true; closing = true; fileOperation.Dispose(); IsEnabled = false; }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (saving && !accepted) { e.Cancel = true; return; }
        if (dirty && !accepted)
        {
            var result = MessageBox.Show(this, "有未保存的设置。放弃这些修改？", "未保存的设置", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) e.Cancel = true;
        }
        base.OnClosing(e);
    }
}
public sealed class ModuleOption(string id, bool enabled)
{
    public string Id { get; } = id;
    public bool Enabled { get; set; } = enabled;
    public string Label => Id switch { "audio" => "声音", "power" => "电源", "awake" => "保持唤醒", "proxy" => "代理", _ => "常用入口" };
}

