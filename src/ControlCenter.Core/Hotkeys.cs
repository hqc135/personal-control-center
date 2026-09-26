namespace ControlCenter.Core;
public sealed record HotkeyDefinition(uint Modifiers, uint Key)
{
    public string Label => ((Modifiers & 2) != 0 ? "Ctrl+" : "") + ((Modifiers & 1) != 0 ? "Alt+" : "")
        + ((Modifiers & 4) != 0 ? "Shift+" : "") + (char)Key;
}
public static class HotkeyPolicy
{
    public static void Validate(HotkeyDefinition? value)
    {
        if (value is null) return;
        if ((value.Modifiers & ~7u) != 0 || (value.Modifiers & 3u) == 0 || !(value.Key is >= 65 and <= 90 or >= 48 and <= 57))
            throw new InvalidDataException("快捷键需包含 Ctrl 或 Alt，搭配字母/数字；不支持 Windows 键、F12 或 PrintScreen。");
    }
    public static HotkeyDefinition? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (var part in parts[..^1])
        {
            uint bit = part.ToUpperInvariant() switch { "CTRL" => 2, "ALT" => 1, "SHIFT" => 4, _ => 0 };
            if (bit == 0 || (modifiers & bit) != 0) throw new InvalidDataException("快捷键格式无效。例：Ctrl+Alt+P。");
            modifiers |= bit;
        }
        if (parts[^1].Length != 1) throw new InvalidDataException("快捷键末尾须为字母或数字。");
        var result = new HotkeyDefinition(modifiers, char.ToUpperInvariant(parts[^1][0]));
        Validate(result); return result;
    }
}
public interface IHotkeyBackend
{
    bool Register(int id, HotkeyDefinition definition);
    bool Unregister(int id);
}
// Owned by the window's UI thread. A new binding is reserved before the previous one is released.
public sealed class HotkeyController(IHotkeyBackend backend) : IDisposable
{
    private int activeId;
    private bool disposed;
    private readonly HashSet<int> held = [];
    public HotkeyDefinition? Current { get; private set; }
    public bool Matches(int id) => !disposed && activeId != 0 && activeId == id;
    public CommandResult Apply(HotkeyDefinition? desired)
    {
        if (disposed) throw new ObjectDisposedException(nameof(HotkeyController));
        HotkeyPolicy.Validate(desired);
        if (desired == Current && held.Count == (Current is null ? 0 : 1)) return CommandResult.Confirmed;
        if (desired is null)
        {
            foreach (int id in held.ToArray()) if (backend.Unregister(id)) held.Remove(id);
            if (!held.Contains(activeId)) { activeId = 0; Current = null; }
            return held.Count == 0 ? CommandResult.Confirmed
                : new(CommandOutcome.UnknownOutcome, FailureCode.NativeFailure, "快捷键释放未确认，请退出程序后重试。");
        }
        int next = activeId == 0x5101 ? 0x5102 : 0x5101;
        if (held.Contains(next)) return new(CommandOutcome.UnknownOutcome, FailureCode.NativeFailure, "旧快捷键资源未释放，请退出后重试。");
        if (!backend.Register(next, desired))
            return new(CommandOutcome.Failed, FailureCode.Unavailable, "快捷键冲突或注册失败；原快捷键与托盘入口保留。");
        held.Add(next);
        if (activeId != 0 && !backend.Unregister(activeId))
        {
            if (backend.Unregister(next)) held.Remove(next);
            return new(CommandOutcome.UnknownOutcome, FailureCode.NativeFailure, "旧快捷键释放未确认，未切换到新快捷键。");
        }
        held.Remove(activeId); activeId = next; Current = desired;
        return CommandResult.Confirmed;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (int id in held) backend.Unregister(id);
        held.Clear(); activeId = 0; Current = null;
    }
}
