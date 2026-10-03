using ControlCenter.Core;
using Xunit;
namespace ControlCenter.Tests;
public class AppearanceStylesTests
{
    [Fact] public void OldConfigurationKeepsDefaultPalette()
    {
        var config = ConfigCodec.Decode("{\"schemaVersion\":1}"u8.ToArray());
        Assert.Equal("graphite", config.Appearance.Palette);
        Assert.Equal("", config.Appearance.CustomCss);
    }
    [Fact] public void AllPalettesHaveLightAndDarkColors()
    {
        foreach (var palette in AppearanceStyles.Palettes)
        {
            var config = new AppearanceConfig(Palette: palette.Id);
            var light = AppearanceStyles.Resolve(config, false); var dark = AppearanceStyles.Resolve(config, true);
            Assert.NotEqual(light["background"], dark["background"]);
            Assert.All(AppearanceStyles.ColorNames, name => Assert.Matches("^#[0-9A-Fa-f]{6}$", light[name]));
            Assert.All(AppearanceStyles.ColorNames, name => Assert.Matches("^#[0-9A-Fa-f]{6}$", dark[name]));
        }
    }
    [Fact] public void CssCascadeAndModeOverridesWork()
    {
        const string css = "/* theme */ :root { --accent: #123; --card-radius: 0px; } [data-theme=\"dark\"] { --accent: #abcdef; }";
        Assert.Equal("#112233", AppearanceStyles.ParseCss(css, false)["accent"]);
        Assert.Equal("#abcdef", AppearanceStyles.ParseCss(css, true)["accent"]);
        Assert.Equal("0px", AppearanceStyles.ParseCss(css, true)["card-radius"]);
        Assert.Equal("#456789", AppearanceStyles.ParseCss(":root { --accent: #123; --accent: #456789; }", false)["accent"]);
    }
    [Theory]
    [InlineData(":root { --unknown: #fff; }")]
    [InlineData(":root { --accent: url(https://example.com); }")]
    [InlineData(":root { --accent: #1234; }")]
    [InlineData(":root { --card-padding: 0px; }")]
    [InlineData(":root { --card-radius: 100px; }")]
    [InlineData(":root { --control-radius: -1px; }")]
    [InlineData("button { color: red; }")]
    [InlineData("@import 'theme.css';")]
    [InlineData(":root { --accent: #fff; } garbage")]
    [InlineData("[data-theme=\"dark\"] { --accent: invalid; }")]
    [InlineData("/* unfinished")]
    public void InvalidCssRejectedIncludingInactiveBlocks(string css) => Assert.Throws<InvalidDataException>(() => AppearanceStyles.ParseCss(css, false));
    [Fact] public void CssSizeIsBounded() => Assert.Throws<InvalidDataException>(() => AppearanceStyles.ParseCss(new string(' ', AppearanceStyles.MaxCssLength + 1), false));
    [Fact] public void ConfigurationAndPortableTransferRetainTheme()
    {
        var original = new AppConfig { Appearance = new(Palette: "mint", CustomCss: AppearanceStyles.Example) };
        var decoded = ConfigCodec.Decode(ConfigCodec.Encode(original));
        var portable = ConfigurationTransfer.Portable(decoded);
        Assert.Equal(original.Appearance.Palette, portable.Appearance.Palette);
        Assert.Equal(original.Appearance.CustomCss, portable.Appearance.CustomCss);
        Assert.Throws<InvalidDataException>(() => ConfigCodec.Validate(original with { Appearance = new(Palette: "missing") }));
    }
}
