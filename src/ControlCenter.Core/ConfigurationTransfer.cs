namespace ControlCenter.Core;
public sealed record ImportPreview(AppConfig Config, int RemovedLocalEntries);
public static class ConfigurationTransfer
{
    // Portable files deliberately omit all extension data: unknown fields may contain machine-local secrets.
    public static AppConfig Portable(AppConfig source)
    {
        ConfigCodec.Validate(source);
        return new()
        {
            Appearance = new(source.Appearance.Theme, source.Appearance.FontFamily, source.Appearance.Motion, source.Appearance.Material, source.Appearance.Palette, source.Appearance.CustomCss),
            Modules = [.. source.Modules],
            Proxy = new(source.Proxy.Host, source.Proxy.Port),
            Shortcuts = source.Shortcuts.Where(x => x.Kind == "knownFolder").Select(x => new ShortcutDefinition(x.Id, x.Label, x.Kind, x.Target)).ToArray(),
            Hotkey = null
        };
    }
    public static ImportPreview Preview(byte[] data)
    {
        var decoded = ConfigCodec.Decode(data);
        // Rebinding is deliberate: no executable/path/web target is imported as an actionable entry.
        var portable = Portable(decoded);
        return new(portable, decoded.Shortcuts.Length - portable.Shortcuts.Length);
    }
}
