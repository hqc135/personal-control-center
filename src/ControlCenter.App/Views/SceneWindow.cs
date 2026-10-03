using System.Windows;
using System.Windows.Controls;
using ControlCenter.Core;
using ControlCenter.App.ViewModels;
namespace ControlCenter.App.Views;
public sealed class SceneWindow : Window
{
    private readonly DesktopViewModel model;
    private readonly TextBox name = new() { Text = "工作" }, volume = new(), minutes = new();
    private readonly CheckBox keepDisplay = new() { Content = "保持屏幕常亮" };
    private readonly ComboBox mute = new() { ItemsSource = new[] { "不改静音", "静音", "取消静音" }, SelectedIndex = 0 };
    private readonly ComboBox power = new(), saved = new();
    private readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Button apply = new() { Content = "执行以上操作", IsEnabled = false }, restore = new() { Content = "恢复上次场景" };
    private SceneDefinition? prepared;
    private bool busy;
    public SceneWindow(DesktopViewModel model, IReadOnlyList<PowerChoice> choices)
    {
        this.model = model; Title = "自定义场景"; Width = 480; Height = 680; MinWidth = 380; MinHeight = 400;
        SetResourceReference(BackgroundProperty, "BackgroundBrush"); SetResourceReference(ForegroundProperty, "TextBrush");
        var body = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = "场景", FontSize = 22, FontWeight = FontWeights.SemiBold });
        body.Children.Add(new TextBlock { Text = "只执行你选择的操作。先预览，再执行；不会自动切换网络或启动程序。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        Add("已保存场景", saved); Add("名称", name); Add("音量百分比（留空不改）", volume); Add("静音", mute);
        Add("保持唤醒分钟数（留空不改，1–480）", minutes); body.Children.Add(keepDisplay);
        power.ItemsSource = new[] { new PowerChoice(Guid.Empty, "不改电源") }.Concat(choices).ToArray(); power.DisplayMemberPath = "Label"; power.SelectedIndex = 0;
        Add("电源方案", power);
        var edit = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; body.Children.Add(edit);
        Button ActionButton(string title, RoutedEventHandler action) { var b = new Button { Content = title, Margin = new Thickness(0, 0, 6, 6) }; b.Click += action; edit.Children.Add(b); return b; }
        ActionButton("保存场景", (_, _) =>
        {
            try { var scene = Draft(); model.Save(model.Preferences with { Scenes = model.Preferences.Scenes.Where(x => x.Name != scene.Name).Append(scene).ToArray() }); Reload(); result.Text = model.Preferences.Scenes.Contains(scene) ? "场景已保存" : model.Status; }
            catch (Exception ex) { result.Text = ex.Message; }
        });
        ActionButton("删除所选", (_, _) => { if (saved.SelectedItem is SceneDefinition s) { model.Save(model.Preferences with { Scenes = model.Preferences.Scenes.Where(x => x.Name != s.Name).ToArray() }); Reload(); } });
        ActionButton("预览操作", (_, _) => { try { prepared = Draft(); preview.Text = prepared.Name + "\n" + prepared.Preview; apply.IsEnabled = model.Scenes is not null && !busy; } catch (Exception ex) { result.Text = ex.Message; } });
        body.Children.Add(preview); body.Children.Add(apply); restore.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(restore); body.Children.Add(result);
        apply.Click += async (_, _) =>
        {
            if (prepared is null || model.Scenes is null || busy) return;
            busy = true; apply.IsEnabled = restore.IsEnabled = false;
            try { result.Text = await model.Scenes.ApplyAsync(prepared); }
            catch { result.Text = "场景未完成，请检查实际状态。"; }
            finally { busy = false; apply.IsEnabled = true; restore.IsEnabled = model.Scenes.CanRestore; }
        };
        restore.Click += async (_, _) =>
        {
            if (model.Scenes is null || busy) return;
            busy = true; apply.IsEnabled = restore.IsEnabled = false;
            try { result.Text = await model.Scenes.RestoreAsync(); }
            catch { result.Text = "恢复未完成，请检查实际状态。"; }
            finally { busy = false; apply.IsEnabled = prepared is not null; restore.IsEnabled = model.Scenes.CanRestore; }
        };
        restore.IsEnabled = model.Scenes?.CanRestore == true;
        saved.DisplayMemberPath = "Name";
        saved.SelectionChanged += (_, _) =>
        {
            if (saved.SelectedItem is not SceneDefinition s) return;
            name.Text = s.Name; volume.Text = s.Volume?.ToString() ?? ""; minutes.Text = s.AwakeMinutes?.ToString() ?? "";
            mute.SelectedIndex = s.Muted switch { true => 1, false => 2, _ => 0 }; keepDisplay.IsChecked = s.KeepDisplay;
            power.SelectedItem = ((IEnumerable<PowerChoice>)power.ItemsSource).FirstOrDefault(x => x.Id == (s.PowerScheme ?? Guid.Empty));
        };
        foreach (var box in new[] { name, volume, minutes }) box.TextChanged += (_, _) => InvalidatePreview();
        mute.SelectionChanged += (_, _) => InvalidatePreview(); power.SelectionChanged += (_, _) => InvalidatePreview();
        keepDisplay.Checked += (_, _) => InvalidatePreview(); keepDisplay.Unchecked += (_, _) => InvalidatePreview();
        Closing += (_, e) => { if (busy) { e.Cancel = true; result.Text = "请等待当前操作完成后关闭。"; } };
        Reload();
        void Add(string label, FrameworkElement control) { body.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) }); body.Children.Add(control); }
    }
    private void Reload() => saved.ItemsSource = model.Preferences.Scenes;
    private void InvalidatePreview() { prepared = null; preview.Text = ""; apply.IsEnabled = false; }
    private SceneDefinition Draft()
    {
        int? Number(string value) => string.IsNullOrWhiteSpace(value) ? null : int.TryParse(value, out var n) ? n : throw new InvalidDataException("请输入整数。");
        Guid? scheme = power.SelectedItem is PowerChoice p && p.Id != Guid.Empty ? p.Id : null;
        var scene = new SceneDefinition(name.Text.Trim(), Number(volume.Text), mute.SelectedIndex switch { 1 => true, 2 => false, _ => null }, Number(minutes.Text), keepDisplay.IsChecked == true, scheme);
        scene.Validate(); return scene;
    }
}
