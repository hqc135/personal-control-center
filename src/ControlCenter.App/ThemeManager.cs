using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace ControlCenter.App;
public static class ThemeManager
{
    public static void Apply(string theme)
    {
        bool dark = theme == "dark" || (theme == "system" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0);
        var values = dark
            ? new[] { "#202226", "#2A2D32", "#F2F3F5", "#B6BDC7", "#9AB8FF", "#354566", "#515762", "#363B43" }
            : new[] { "#F4F5F7", "#FFFFFF", "#20242B", "#59616D", "#315FC3", "#EAF0FC", "#DCE0E7", "#E9ECF2" };
        string[] keys = ["BackgroundBrush", "SurfaceBrush", "TextBrush", "SecondaryBrush", "AccentBrush", "AccentSurfaceBrush", "BorderBrush", "HoverBrush"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
        if (SystemParameters.HighContrast)
        {
            Application.Current.Resources["BackgroundBrush"] = SystemColors.WindowBrush;
            Application.Current.Resources["SurfaceBrush"] = SystemColors.WindowBrush;
            Application.Current.Resources["TextBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["SecondaryBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["AccentBrush"] = SystemColors.HighlightTextBrush;
            Application.Current.Resources["AccentSurfaceBrush"] = SystemColors.HighlightBrush;
            Application.Current.Resources["BorderBrush"] = SystemColors.WindowTextBrush;
            Application.Current.Resources["HoverBrush"] = SystemColors.HighlightBrush;
        }
    }
    public static FontFamily PreferredFont()
    {
        // WPF resolves installed fonts in order; opening settings need not enumerate all system fonts.
        return new FontFamily("PingFang SC, Microsoft YaHei UI, Segoe UI");
    }
}

