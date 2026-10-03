using ControlCenter.Core;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
namespace ControlCenter.App;
public static class ThemeManager
{
    public static void Apply(string theme) => Apply(new AppearanceConfig(Theme: theme));
    public static void Apply(AppearanceConfig appearance, ResourceDictionary? target = null)
    {
        var resources = target ?? Application.Current.Resources;
        bool dark = appearance.Theme == "dark" || (appearance.Theme == "system" && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0);
        var values = AppearanceStyles.Resolve(appearance, dark);
        string[] keys = ["BackgroundBrush", "SurfaceBrush", "TextBrush", "SecondaryBrush", "AccentBrush", "AccentSurfaceBrush", "BorderBrush", "HoverBrush"];
        for (int i = 0; i < keys.Length; i++) resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[AppearanceStyles.ColorNames[i]]));
        resources["CardRadius"] = new CornerRadius(AppearanceStyles.Pixels(values["card-radius"]));
        resources["CardPadding"] = new Thickness(AppearanceStyles.Pixels(values["card-padding"]));
        resources["ControlRadius"] = new CornerRadius(AppearanceStyles.Pixels(values["control-radius"]));
        if (SystemParameters.HighContrast)
        {
            resources["BackgroundBrush"] = SystemColors.WindowBrush;
            resources["SurfaceBrush"] = SystemColors.WindowBrush;
            resources["TextBrush"] = SystemColors.WindowTextBrush;
            resources["SecondaryBrush"] = SystemColors.WindowTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightTextBrush;
            resources["AccentSurfaceBrush"] = SystemColors.HighlightBrush;
            resources["BorderBrush"] = SystemColors.WindowTextBrush;
            resources["HoverBrush"] = SystemColors.HighlightBrush;
        }
    }
    public static FontFamily PreferredFont()
    {
        // WPF resolves installed fonts in order; opening settings need not enumerate all system fonts.
        return new FontFamily("PingFang SC, Microsoft YaHei UI, Segoe UI");
    }
}
