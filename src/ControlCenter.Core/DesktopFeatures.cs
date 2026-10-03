namespace ControlCenter.Core;

public sealed record MicrophoneState(string Id, string Name, bool Muted);
public sealed record DisplayBrightness(string Id, string Name, int Percent);
public sealed record MediaSession(string Id, string Title, string Artist, bool Playing, bool CanToggle, bool CanPrevious, bool CanNext)
{ public string StateLabel => Playing ? "正在播放" : "已暂停"; }
public sealed record DesktopSnapshot(string Battery, string Network, string Bluetooth, MicrophoneState? Microphone,
    DisplayBrightness[] Displays, MediaSession[] Media, string[] Warnings);
public interface IDesktopFeatures
{
    Task<DesktopSnapshot> ReadAsync(CancellationToken ct);
    Task MediaAsync(string id, string action, CancellationToken ct);
}
// Each interface is implemented directly by its Windows service; there is no forwarding facade.
public interface IAudioDevices
{
    Task<MicrophoneState?> ReadMicrophoneAsync(CancellationToken ct);
    Task SetMicrophoneMuteAsync(string id, bool muted, CancellationToken ct);
    Task SwitchOutputAsync(string id, CancellationToken ct);
}
public interface IBrightnessService : IAsyncDisposable
{
    Task<DisplayBrightness[]> ReadAsync(CancellationToken ct);
    Task SetAsync(string id, int percent, CancellationToken ct);
}
public static class SettingsPages
{
    public static readonly IReadOnlyDictionary<string, string> Uris = new Dictionary<string, string>
    {
        ["显示"] = "ms-settings:display", ["蓝牙"] = "ms-settings:bluetooth", ["网络"] = "ms-settings:network",
        ["混音器"] = "ms-settings:apps-volume", ["存储"] = "ms-settings:storagesense", ["麦克风权限"] = "ms-settings:privacy-microphone"
    };
}
public sealed record SceneDefinition(string Name, int? Volume = null, bool? Muted = null, int? AwakeMinutes = null, bool KeepDisplay = false, Guid? PowerScheme = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40 || Volume is < 0 or > 100 || AwakeMinutes is < 1 or > 480)
            throw new InvalidDataException("场景名称、音量或时长无效。");
        if (Volume is null && Muted is null && AwakeMinutes is null && PowerScheme is null) throw new InvalidDataException("场景至少需要一项操作。");
    }
    public string Preview => string.Join("；", new[] { Volume is { } v ? $"音量 {v}%" : null, Muted is { } m ? (m ? "静音" : "取消静音") : null,
        AwakeMinutes is { } a ? $"保持唤醒 {a} 分钟" + (KeepDisplay ? "并保持屏幕常亮" : "") : null, PowerScheme is { } p ? $"电源方案 {p}" : null }.Where(x => x is not null));
}
public sealed record PanelGeometry(double Left, double Top, double Width, double Height)
{
    public bool IsValid => new[] { Left, Top, Width, Height }.All(double.IsFinite) && Width is >= 340 and <= 4000 && Height is >= 360 and <= 4000
        && Math.Abs(Left) <= 100000 && Math.Abs(Top) <= 100000;
}
public sealed record FeaturePreferences
{
    public int SchemaVersion { get; init; } = 1;
    public PanelGeometry? Geometry { get; init; }
    public string[] Favorites { get; init; } = ["audio", "power", "awake", "proxy", "shortcuts"];
    public SceneDefinition[] Scenes { get; init; } = [];
    public void Validate()
    {
        string[] ids = ["audio", "power", "awake", "proxy", "shortcuts", "battery", "media", "microphone", "brightness", "connections"];
        if (SchemaVersion != 1 || Geometry is { IsValid: false } || Favorites is null || Favorites.Length > ids.Length || Favorites.Distinct().Count() != Favorites.Length || Favorites.Any(x => !ids.Contains(x))
            || Scenes is null || Scenes.Length > 20 || Scenes.Any(x => x is null) || Scenes.Select(x => x.Name).Distinct().Count() != Scenes.Length)
            throw new InvalidDataException("功能配置无效。");
        foreach (var scene in Scenes) scene.Validate();
    }
}
public interface IFeaturePreferencesStore
{
    FeaturePreferences Load(FeaturePreferences? defaults = null);
    void Save(FeaturePreferences value);
}
