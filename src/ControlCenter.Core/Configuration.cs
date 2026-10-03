using System.Text.Json;
using System.Text.Json.Serialization;
namespace ControlCenter.Core;

public sealed record AppearanceConfig(string Theme = "system", string FontFamily = "PingFang SC", string Motion = "subtle", string Material = "solid", string Palette = "graphite", string CustomCss = "")
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Additional { get; init; }
}
public sealed record AppConfig
{
    public int SchemaVersion { get; init; } = 1;
    public AppearanceConfig Appearance { get; init; } = new();
    public ProxyConfig Proxy { get; init; } = new();
    public HotkeyDefinition? Hotkey { get; init; }
    public string[] Modules { get; init; } = ["audio", "power", "awake", "proxy", "shortcuts"];
    public ShortcutDefinition[] Shortcuts { get; init; } = [new("downloads", "下载", "knownFolder", "Downloads")];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Additional { get; init; }
}
public static class ConfigCodec
{
    public const int MaxBytes = 256 * 1024;
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    public static AppConfig Decode(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("配置超过 256 KiB。");
        using var doc = JsonDocument.Parse(bytes);
        if (doc.RootElement.TryGetProperty("schemaVersion", out var version) && version.GetInt32() > 1)
            throw new FutureConfigException();
        var config = JsonSerializer.Deserialize<AppConfig>(bytes, Options) ?? throw new InvalidDataException("配置为空。");
        Validate(config);
        return config;
    }
    public static void Validate(AppConfig config)
    {
        if (config.SchemaVersion != 1) throw new InvalidDataException("不支持的配置版本。");
        if (config.Appearance is null || config.Appearance.Theme is not ("system" or "light" or "dark")
            || config.Appearance.Motion is not ("subtle" or "off") || config.Appearance.Material != "solid")
            throw new InvalidDataException("外观配置无效。");
        _ = AppearanceStyles.Resolve(config.Appearance, false);
        string[] allowed = ["audio", "power", "awake", "proxy", "shortcuts"];
        if (config.Modules is null || config.Modules.Distinct().Count() != config.Modules.Length || config.Modules.Any(x => !allowed.Contains(x)))
            throw new InvalidDataException("模块列表无效。");
        if (config.Shortcuts is null || config.Shortcuts.Length > 20 || config.Shortcuts.Any(x => x is null)
            || config.Shortcuts.Select(x => x.Id).Distinct().Count() != config.Shortcuts.Length)
            throw new InvalidDataException("入口列表无效或超过 20 项。");
        foreach (var shortcut in config.Shortcuts) ShortcutPolicy.Validate(shortcut);
        ProxyPolicy.Validate(config.Proxy);
        HotkeyPolicy.Validate(config.Hotkey);
        if (config.Proxy.ShortcutId is not null && !config.Shortcuts.Any(x => x.Id == config.Proxy.ShortcutId && x.Kind == "application"))
            throw new InvalidDataException("代理入口须指向已配置的程序入口。");
    }
    public static byte[] Encode(AppConfig config)
    {
        Validate(config);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(config, Options);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("配置超过 256 KiB。");
        return bytes;
    }
}
public sealed class FutureConfigException() : IOException("配置来自较新版本，已进入只读模式，不会覆盖原文件。");
public sealed class ConfigConflictException() : IOException("配置已被其他操作修改；未覆盖新文件。请重新载入后再编辑。");
public interface IConfigSaveStatus { string? LastSaveWarning { get; } }
public sealed record ConfigLoadResult(AppConfig Config, string? Warning = null, bool ReadOnly = false);
public interface IConfigRepository
{
    Task<ConfigLoadResult> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppConfig config, CancellationToken ct = default);
}
public sealed record ConfigBackup(string Id, DateTime LastWriteUtc);
public interface IConfigRecovery
{
    Task<IReadOnlyList<ConfigBackup>> ListBackupsAsync(CancellationToken ct = default);
    Task<AppConfig> ReadBackupAsync(string id, CancellationToken ct = default);
}
