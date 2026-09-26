using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ControlCenter.Core;
namespace ControlCenter.App.Views;
public partial class ShortcutEditorWindow : Window
{
    private readonly string id;
    public ShortcutDefinition? Entry { get; private set; }
    public ShortcutEditorWindow(ShortcutDefinition? entry = null)
    {
        InitializeComponent(); FontFamily = ThemeManager.PreferredFont();
        id = entry?.Id ?? Guid.NewGuid().ToString("N");
        if (entry is null) return;
        LabelBox.Text = entry.Label; TargetBox.Text = entry.Target;
        KindBox.SelectedIndex = entry.Kind == "application" ? 1 : entry.Kind == "url" ? 2 : 0;
        ArgumentsBox.Text = string.Join("\n", entry.Arguments ?? []); WorkingBox.Text = entry.WorkingDirectory ?? "";
    }
    private string Kind => (string)((ComboBoxItem)KindBox.SelectedItem).Tag;
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Kind == "folder")
        {
            var dialog = new OpenFolderDialog { Title = "选择本机目录" };
            if (dialog.ShowDialog(this) == true) TargetBox.Text = dialog.FolderName;
        }
        else if (Kind == "application")
        {
            var dialog = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) TargetBox.Text = dialog.FileName;
        }
        else ErrorText.Text = "网页请直接填写 HTTP/HTTPS 地址。";
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var args = ArgumentsBox.Text.Length == 0 ? [] : ArgumentsBox.Text.Replace("\r\n", "\n").Split('\n');
        var entry = new ShortcutDefinition(id, LabelBox.Text.Trim(), Kind, TargetBox.Text.Trim(), args, string.IsNullOrWhiteSpace(WorkingBox.Text) ? null : WorkingBox.Text.Trim());
        try { ShortcutPolicy.Validate(entry); Entry = entry; DialogResult = true; }
        catch (InvalidDataException ex) { ErrorText.Text = ex.Message; }
    }
}

