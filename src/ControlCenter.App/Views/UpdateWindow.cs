using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ControlCenter.Windows;
namespace ControlCenter.App.Views;
public sealed class UpdateWindow : Window
{
    private readonly UpdatePackages packages;
    private readonly CancellationTokenSource lifetime = new();
    private readonly PasswordBox token = new();
    private readonly TextBox hash = new();
    private readonly ComboBox versions = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Button download = new() { Content = "下载、校验并解压", IsEnabled = false };
    private readonly List<Button> actions = [];
    private UpdateRelease? release;
    private bool busy;
    public UpdateWindow(UpdatePackages? packages = null)
    {
        this.packages = packages ?? new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PersonalControlCenter", "versions"));
        Title = "更新与回退"; Width = 500; Height = 680; MinWidth = 380; MinHeight = 400;
        SetResourceReference(BackgroundProperty, "BackgroundBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        token.MinHeight = 32; token.Padding = new Thickness(8, 5, 8, 5);
        token.SetResourceReference(BackgroundProperty, "SurfaceBrush"); token.SetResourceReference(ForegroundProperty, "TextBrush"); token.SetResourceReference(BorderBrushProperty, "BorderBrush");
        var body = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "更新与回退", FontSize = 22, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = $"当前 {typeof(UpdateWindow).Assembly.GetName().Version?.ToString(3)} · 手动检查，包含预发布版", Margin = new Thickness(0, 8, 0, 0) });
        Add("GitHub token（私有仓库需要；只在本窗口内使用，不保存）", token);
        var check = Button("检查 GitHub 更新", async () =>
        {
            release = await this.packages.CheckAsync(token.Password, lifetime.Token);
            status.Text = release is null ? "没有可校验的已发布运行包。" : $"最新可用：{release.Version} · {release.Size / 1048576d:F1} MB\n当前版本：{typeof(UpdateWindow).Assembly.GetName().Version?.ToString(3)}";
        });
        actions.Add(download); body.Children.Add(download);
        download.Click += async (_, _) => await Run(async () =>
        {
            if (release is null) return;
            var folder = await this.packages.DownloadAsync(release, token.Password, lifetime.Token);
            status.Text = "已校验并解压到：\n" + folder + "\n请从托盘退出旧版，再运行此目录内的程序。";
            Reload();
        });
        Button("浏览器查看发布页", () => { Process.Start(new ProcessStartInfo("https://github.com/hqc135/personal-control-center/releases") { UseShellExecute = true }); return Task.CompletedTask; });
        Add("本地 ZIP 的发布 SHA256", hash);
        Button("选择 ZIP 并校验解压", async () =>
        {
            var dialog = new OpenFileDialog { Filter = "运行包 (*.zip)|*.zip" };
            if (dialog.ShowDialog(this) != true) return;
            string expected = hash.Text.Trim();
            var folder = await this.packages.StageAsync(dialog.FileName, expected, lifetime.Token);
            status.Text = "已校验并解压到：\n" + folder; Reload();
        });
        Add("已校验版本（选择旧版即可回退）", versions);
        Button("打开所选版本目录", () =>
        {
            if (versions.SelectedItem is string path && this.packages.Versions().Contains(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        Button("打开当前运行目录", () => { Process.Start(new ProcessStartInfo(AppContext.BaseDirectory) { UseShellExecute = true }); return Task.CompletedTask; });
        body.Children.Add(new TextBlock { Text = "各版本并排保留，不覆盖正在运行的程序。升级/回退：退出当前版本，打开对应目录运行 EXE。配置仍在用户目录，旧版本遇到不支持的配置时请使用配置备份。校验哈希用于确认文件完整性，不代表数字签名。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        var cancel = new Button { Content = "取消传输", Margin = new Thickness(0, 10, 0, 0) }; cancel.Click += (_, _) => { lifetime.Cancel(); status.Text = "已请求取消；重新打开窗口可重试。"; }; body.Children.Add(cancel);
        body.Children.Add(status); Closed += (_, _) => { lifetime.Cancel(); token.Clear(); }; Reload();
        void Add(string label, FrameworkElement control) { body.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 5) }); body.Children.Add(control); }
        Button Button(string label, Func<Task> action)
        {
            var b = new Button { Content = label, Margin = new Thickness(0, 8, 0, 0) }; b.Click += async (_, _) => await Run(action); actions.Add(b); body.Children.Add(b); return b;
        }
    }
    private void Reload() { versions.ItemsSource = packages.Versions(); if (versions.Items.Count > 0) versions.SelectedIndex = 0; }
    private async Task Run(Func<Task> action)
    {
        if (busy || lifetime.IsCancellationRequested) return;
        busy = true; foreach (var button in actions) button.IsEnabled = false; status.Text = "处理中…";
        try { await action(); }
        catch (OperationCanceledException) { status.Text = "操作已取消。"; }
        catch (Exception ex) { status.Text = "未完成：" + (ex is IOException or InvalidDataException ? ex.Message : "网络或文件操作失败，请重试或手动下载。"); }
        finally { busy = false; foreach (var button in actions) button.IsEnabled = !lifetime.IsCancellationRequested; download.IsEnabled = release is not null && !lifetime.IsCancellationRequested; }
    }
}
