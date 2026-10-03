namespace ControlCenter.Core;
public sealed record AudioSwitchCapability(bool Available, string Explanation)
{
    // Implementation availability, not a guarantee of compatibility with a particular device/OS build.
    public static AudioSwitchCapability Current { get; } = new(true,
        "直接切换使用 Windows 兼容接口；未完成真机兼容验收，失败时请使用系统声音设置。");
}
