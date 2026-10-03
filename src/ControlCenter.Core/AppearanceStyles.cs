using System.Globalization;
using System.Text.RegularExpressions;
namespace ControlCenter.Core;

public sealed record PaletteOption(string Id, string Name);
public static class AppearanceStyles
{
    public const int MaxCssLength = 16384;
    public static readonly PaletteOption[] Palettes = [new("graphite", "石墨"), new("ocean", "海蓝"), new("mint", "薄荷"), new("lavender", "薰衣草"), new("rose", "玫瑰"), new("sand", "暖沙")];
    public static readonly string[] ColorNames = ["background", "surface", "text", "secondary", "accent", "accent-surface", "border", "hover"];
    public const string Example = "/* 留空的变量沿用所选配色；颜色使用 #RGB 或 #RRGGBB。 */\n:root {\n  --accent: #7356BF;\n  --card-radius: 18px;\n  --card-padding: 16px;\n  --control-radius: 10px;\n}\n[data-theme=\"dark\"] {\n  --accent: #C5B4FF;\n}\n";
    // A bounded CSS theme-variable dialect, not a browser stylesheet or executable XAML.
    public static Dictionary<string, string> ParseCss(string? css, bool dark)
    {
        if (css is null || css.Length > MaxCssLength) throw new InvalidDataException("CSS 不能为空值或超过 16,384 字符。");
        css = Regex.Replace(css, @"/\*[\s\S]*?\*/", "");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (!string.IsNullOrWhiteSpace(css))
        {
            var block = Regex.Match(css, "^\\s*(:root|\\[data-theme=\"(?:light|dark)\"\\])\\s*\\{([^{}]*)\\}");
            if (!block.Success) throw new InvalidDataException("CSS 仅支持 :root、[data-theme=\"light\"] 和 [data-theme=\"dark\"] 变量块。");
            var selector = block.Groups[1].Value;
            bool applies = selector == ":root" || selector == (dark ? "[data-theme=\"dark\"]" : "[data-theme=\"light\"]");
            foreach (var entry in block.Groups[2].Value.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                var declaration = Regex.Match(entry, @"^\s*--([a-z-]+)\s*:\s*(.*?)\s*$");
                if (!declaration.Success) throw new InvalidDataException("CSS 变量格式应为 --accent: #7356BF;");
                string name = declaration.Groups[1].Value, value = declaration.Groups[2].Value;
                if (ColorNames.Contains(name))
                {
                    if (!Regex.IsMatch(value, @"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")) throw new InvalidDataException($"--{name} 须使用 #RGB 或 #RRGGBB 不透明颜色。");
                    if (value.Length == 4) value = "#" + string.Concat(value.Skip(1).Select(c => new string(c, 2)));
                }
                else if (name is "card-radius" or "card-padding" or "control-radius")
                {
                    int min = name == "card-padding" ? 8 : 0;
                    int max = name == "control-radius" ? 20 : 32;
                    if (!Regex.IsMatch(value, @"^\d{1,2}px$") || !int.TryParse(value[..^2], out int number) || number < min || number > max)
                        throw new InvalidDataException($"--{name} 须为 {min}–{max}px 整数。");
                }
                else throw new InvalidDataException($"不支持 CSS 变量 --{name}。");
                if (applies) result[name] = value;
            }
            css = css[block.Length..];
        }
        return result;
    }
    public static Dictionary<string, string> Resolve(AppearanceConfig config, bool dark)
    {
        if (!Palettes.Any(x => x.Id == config.Palette)) throw new InvalidDataException("未知配色方案。");
        string[] colors = (config.Palette, dark) switch
        {
            ("ocean", false) => ["#EFF6FA", "#FFFFFF", "#163244", "#4C6879", "#006A9C", "#DCEFFA", "#CEDFE9", "#E2EDF4"],
            ("ocean", true) => ["#16232D", "#20333F", "#ECF6FC", "#B0C9D8", "#7FCFFF", "#244B64", "#415D70", "#2B414F"],
            ("mint", false) => ["#F0F6F2", "#FFFFFF", "#19392E", "#506B60", "#267451", "#E0F1E7", "#CEDFD5", "#E5EEE8"],
            ("mint", true) => ["#192823", "#243930", "#EDF8F1", "#B2CDBE", "#8CD9AD", "#305640", "#496958", "#30493D"],
            ("lavender", false) => ["#F5F2FA", "#FFFFFF", "#332945", "#695D7B", "#7250AF", "#EEE5FB", "#DDD4E9", "#EDE7F4"],
            ("lavender", true) => ["#26212F", "#342D40", "#F6F0FF", "#C5B9D5", "#C5A9FF", "#4D3B68", "#625471", "#433A50"],
            ("rose", false) => ["#FAF2F4", "#FFFFFF", "#432B33", "#7D5966", "#A43B63", "#F9E2EB", "#E8D3DB", "#F3E6EB"],
            ("rose", true) => ["#2C2026", "#3C2C34", "#FFF0F5", "#D2B5C2", "#FFA5C6", "#633C50", "#735162", "#4D3943"],
            ("sand", false) => ["#F7F3EB", "#FFFEFA", "#403324", "#75634F", "#89591F", "#F2E6CE", "#E2D8C6", "#EFE8DA"],
            ("sand", true) => ["#29251E", "#393229", "#FFF5E5", "#CCBEA7", "#E5BF7F", "#59472C", "#6E604C", "#494033"],
            (_, true) => ["#202226", "#2A2D32", "#F2F3F5", "#B6BDC7", "#9AB8FF", "#354566", "#515762", "#363B43"],
            _ => ["#F4F5F7", "#FFFFFF", "#20242B", "#59616D", "#315FC3", "#EAF0FC", "#DCE0E7", "#E9ECF2"]
        };
        var values = ColorNames.Zip(colors).ToDictionary(x => x.First, x => x.Second);
        values["card-radius"] = "18px"; values["card-padding"] = "16px"; values["control-radius"] = "10px";
        foreach (var entry in ParseCss(config.CustomCss, dark)) values[entry.Key] = entry.Value;
        return values;
    }
    public static double Pixels(string value) => double.Parse(value[..^2], CultureInfo.InvariantCulture);
}
