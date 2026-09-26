using System.Runtime.InteropServices;
using Microsoft.Win32;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed class WindowsHotkeyBackend(nint window) : IHotkeyBackend
{
    public bool Register(int id, HotkeyDefinition definition) => RegisterHotKey(window, id, definition.Modifiers | 0x4000, definition.Key);
    public bool Unregister(int id) => UnregisterHotKey(window, id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
public interface IRunEntryStore
{
    object? Read();
    void Write(string command);
    void Delete();
}
public sealed class UserRunEntryStore : IRunEntryStore
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "PersonalControlCenter";
    public object? Read() { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames); }
    public void Write(string command) { using var key = Registry.CurrentUser.CreateSubKey(Key); key.SetValue(Name, command, RegistryValueKind.String); }
    public void Delete() { using var key = Registry.CurrentUser.OpenSubKey(Key, true); key?.DeleteValue(Name, false); }
}
public sealed record StartupSnapshot(object? Entry, bool CurrentLocation, string Message);
public sealed class UserStartupService(IRunEntryStore store, string executable)
{
    public string Command
    {
        get
        {
            ShortcutPolicy.Validate(new("startup", "开机启动", "application", executable));
            var command = "\"" + executable + "\" --tray";
            if (command.Length > 260) throw new InvalidDataException("程序路径过长，无法登记用户自启。");
            return command;
        }
    }
    public StartupSnapshot Read()
    {
        var entry = store.Read();
        bool own = entry is string text && text == Command;
        return new(entry, own, entry is null ? "未登记本用户自启。" : own ? "已登记当前位置；Windows 启动应用设置仍可能将其禁用。" : "存在其他内容的同名登记；请先核对，程序不会覆盖或删除它。");
    }
    // Only a direct UI button invokes this method. Loading/importing configuration cannot write Run keys.
    public CommandResult Set(bool enabled, StartupSnapshot observed)
    {
        try
        {
            var current = store.Read();
            if (!Equals(current, observed.Entry)) return new(CommandOutcome.Failed, FailureCode.VerificationFailed, "自启登记已被外部修改，请刷新后重试。");
            if (current is not null && !Equals(current, Command))
                return new(CommandOutcome.Failed, FailureCode.Untrusted, "同名登记不属于当前程序位置，未修改。");
            if (enabled) store.Write(Command); else if (current is not null) store.Delete();
            var actual = store.Read();
            if (enabled ? !Equals(actual, Command) : actual is not null)
                return new(CommandOutcome.UnknownOutcome, FailureCode.VerificationFailed, "自启写入后的回读不一致。");
            return new(CommandOutcome.Confirmed, Message: enabled ? "已登记当前用户自启（托盘启动）。" : "已移除本程序的用户自启登记。");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(CommandOutcome.Failed, FailureCode.PermissionDenied, "无法修改用户自启登记；未申请提权。"); }
    }
}
