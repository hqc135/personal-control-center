using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using ControlCenter.Core;
namespace ControlCenter.Windows.Power;
public sealed class PowerSchemeService : IPowerService
{
    private readonly NativeWorker worker = new("PCC Power", () => { });
    private nint notification;
    public event Action? Invalidated;
    private static readonly Guid Personality = new("245D8541-3943-4422-B025-13A784F679B7");
    public void AttachNotifications(nint hwnd)
    {
        if (notification != 0) return;
        var guid = Personality;
        notification = RegisterPowerSettingNotification(hwnd, ref guid, 0);
        if (notification == 0) throw new ServiceException(FailureCode.NativeFailure, "无法订阅电源变化；打开面板时仍会重新读取。", Marshal.GetLastWin32Error());
    }
    public void HandleMessage(int message, nint wParam)
    {
        if (message == 0x218 && (int)wParam is 0x8013 or 0x12 or 0x7 or 0xA) Invalidated?.Invoke();
    }
    public Task<PowerSnapshot> ReadAsync(CancellationToken ct) => worker.InvokeAsync(Read, ct);
    private static PowerSnapshot Read()
    {
        var list = ImmutableArray.CreateBuilder<PowerScheme>();
        for (uint i = 0; i < 256; i++)
        {
            uint size = 16;
            uint result = PowerEnumerate(0, 0, 0, 16, i, out var id, ref size);
            if (result == 259) break;
            Check(result);
            uint bytes = 0;
            result = PowerReadFriendlyName(0, ref id, 0, 0, null, ref bytes);
            if (result is not (0 or 234)) Check(result);
            if (bytes is 0 or > 65536) throw new ServiceException(FailureCode.NativeFailure, "电源方案名称长度异常。");
            var buffer = new byte[bytes];
            Check(PowerReadFriendlyName(0, ref id, 0, 0, buffer, ref bytes));
            list.Add(new(id, Encoding.Unicode.GetString(buffer).TrimEnd('\0')));
        }
        Check(PowerGetActiveScheme(0, out var pointer));
        Guid active;
        try { active = Marshal.PtrToStructure<Guid>(pointer); } finally { if (pointer != 0) LocalFree(pointer); }
        bool? ac = GetSystemPowerStatus(out var status) && status.AcLineStatus != 255 ? status.AcLineStatus == 1 : null;
        return new(list.ToImmutable(), active, ac);
    }
    public Task ActivateAsync(Guid id, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        // The coordinator checks existence; the native API remains authoritative if a scheme disappears meanwhile.
        Check(PowerSetActiveScheme(0, ref id)); return true;
    }, ct);
    private static void Check(uint result)
    {
        if (result != 0) throw new ServiceException(result == 5 ? FailureCode.PermissionDenied : FailureCode.NativeFailure,
            result == 5 ? "系统拒绝切换电源方案，不会自动提权。" : "电源方案接口调用失败。", unchecked((int)result));
    }
    public async ValueTask DisposeAsync()
    {
        if (notification != 0)
        {
            if (!UnregisterPowerSettingNotification(notification)) System.Diagnostics.Trace.WriteLine("Power notification cleanup failed.");
            notification = 0;
        }
        await worker.DisposeAsync();
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus { public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(nint root, nint scheme, nint subgroup, uint access, uint index, out Guid buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(nint root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(nint root, ref Guid scheme, nint subgroup, nint setting, [Out] byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint RegisterPowerSettingNotification(nint hwnd, ref Guid setting, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterPowerSettingNotification(nint handle);
}

