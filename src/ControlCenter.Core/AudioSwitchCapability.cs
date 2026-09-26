namespace ControlCenter.Core;
public sealed record AudioSwitchCapability(bool Available, string Explanation)
{
    // No validated environment is currently allowlisted. Configuration cannot override this gate.
    public static AudioSwitchCapability Current { get; } = new(false,
        "默认输出切换尚未通过此版本的兼容验证，请在系统声音设置中切换。");
}
