using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ControlCenter.Core;
namespace ControlCenter.Windows.Launch;
public interface ILaunchPlatform
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string DownloadsPath();
    void Start(ProcessStartInfo startInfo);
}
public sealed class WindowsLaunchPlatform : ILaunchPlatform
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string DownloadsPath()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        int hr = SHGetKnownFolderPath(ref id, 0, 0, out var value);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        try { return Marshal.PtrToStringUni(value) ?? throw new IOException(); }
        finally { Marshal.FreeCoTaskMem(value); }
    }
    public void Start(ProcessStartInfo startInfo) => Process.Start(startInfo)?.Dispose();
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, nint token, out nint path);
}
public sealed class ShortcutLauncher(IShortcutTrustStore trust, ILaunchPlatform platform) : IShortcutLauncher
{
    public Task<CommandResult> LaunchAsync(ShortcutDefinition shortcut, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            ShortcutPolicy.Validate(shortcut);
            ProcessStartInfo start;
            switch (shortcut.Kind)
            {
                case "application":
                    if (!trust.IsTrusted(shortcut)) return new CommandResult(CommandOutcome.Failed, FailureCode.Untrusted, "程序入口待确认，请在设置中核对路径与参数。");
                    if (!platform.FileExists(shortcut.Target)) return Missing();
                    if (shortcut.WorkingDirectory is { } cwd && !platform.DirectoryExists(cwd)) return Missing();
                    start = new(shortcut.Target) { UseShellExecute = false, WorkingDirectory = shortcut.WorkingDirectory ?? Path.GetDirectoryName(shortcut.Target)! };
                    foreach (var argument in shortcut.Arguments ?? []) start.ArgumentList.Add(argument);
                    break;
                case "folder":
                case "knownFolder":
                    var target = shortcut.Kind == "knownFolder" ? platform.DownloadsPath() : shortcut.Target;
                    if (!platform.DirectoryExists(target)) return Missing();
                    start = new(target) { UseShellExecute = true };
                    break;
                case "url":
                    start = new(shortcut.Target) { UseShellExecute = true };
                    break;
                default: return new CommandResult(CommandOutcome.Failed, FailureCode.InvalidConfiguration, "入口类型无效。");
            }
            ct.ThrowIfCancellationRequested();
            platform.Start(start);
            return CommandResult.Confirmed;
        }
        catch (OperationCanceledException) { return new(CommandOutcome.Failed, FailureCode.Cancelled, "已取消打开入口。"); }
        catch (InvalidDataException) { return new(CommandOutcome.Failed, FailureCode.InvalidConfiguration, "入口配置无效，请在设置中修改。"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or COMException or ArgumentException)
        { return new(CommandOutcome.Failed, FailureCode.NativeFailure, "入口无法打开，请在设置中检查目标。"); }
    }, ct);
    public Task<CommandResult> OpenSoundSettingsAsync(CancellationToken ct) => OpenSettingsAsync("ms-settings:sound", ct);
    public Task<CommandResult> OpenPowerSettingsAsync(CancellationToken ct) => OpenSettingsAsync("ms-settings:powersleep", ct);
    private Task<CommandResult> OpenSettingsAsync(string uri, CancellationToken ct) => Task.Run(() =>
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            platform.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return CommandResult.Confirmed;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OperationCanceledException)
        { return new CommandResult(CommandOutcome.Failed, FailureCode.NativeFailure, "系统设置无法打开。"); }
    }, ct);
    private static CommandResult Missing() => new(CommandOutcome.Failed, FailureCode.Unavailable, "目标已不存在，请在设置中重新选择。");
}

