using System.Runtime.InteropServices;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed class BrightnessService : IAsyncDisposable
{
    private readonly NativeWorker worker = new("PCC brightness", () => { });
    public Task<DisplayBrightness[]> ReadAsync(CancellationToken ct) => worker.InvokeAsync(() =>
    {
        var result = new List<DisplayBrightness>();
        try
        {
            QueryWmi("WmiMonitorBrightness", item =>
            {
                if ((bool)ReadProperty(item, "Active")) result.Add(new("wmi:" + (string)ReadProperty(item, "InstanceName"), "内置显示屏", (byte)ReadProperty(item, "CurrentBrightness")));
            });
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80041010)) { } // No WMI brightness class on this hardware.
        catch (COMException ex) { throw new ServiceException(FailureCode.NativeFailure, $"内屏亮度读取失败（{ex.HResult:X8}），可使用显示设置。", ex.HResult); }
        EnumeratePhysical((id, name, handle) =>
        {
            ct.ThrowIfCancellationRequested();
            if (GetMonitorBrightness(handle, out var min, out var current, out var max) && max > min)
                result.Add(new(id, name, (int)Math.Clamp((current - (double)min) * 100 / (max - min), 0, 100)));
        });
        return result.ToArray();
    }, ct);
    public Task SetAsync(string id, int percent, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        if (percent is < 0 or > 100) throw new InvalidDataException("亮度必须为 0–100。");
        ct.ThrowIfCancellationRequested(); bool changed = false;
        if (id.StartsWith("wmi:", StringComparison.Ordinal))
        {
            QueryWmi("WmiMonitorBrightnessMethods", item =>
            {
                if ("wmi:" + (string)ReadProperty(item, "InstanceName") != id) return;
                ct.ThrowIfCancellationRequested();
                uint code = item.WmiSetBrightness(0u, (byte)percent);
                if (code != 0) throw new IOException("屏幕拒绝亮度设置。");
                changed = true;
            });
            bool verified = false;
            QueryWmi("WmiMonitorBrightness", item =>
            {
                if ("wmi:" + (string)ReadProperty(item, "InstanceName") == id) verified = Math.Abs((byte)ReadProperty(item, "CurrentBrightness") - percent) <= 2;
            });
            if (changed && !verified) throw new IOException("亮度写入后尚未读回目标值，请刷新确认。");
        }
        else
        {
            EnumeratePhysical((candidate, displayName, handle) =>
            {
                if (candidate != id) return;
                ct.ThrowIfCancellationRequested();
                if (!GetMonitorBrightness(handle, out var min, out _, out var max) || max <= min) return;
                uint value = min + (uint)Math.Round((max - min) * percent / 100d);
                if (!SetMonitorBrightness(handle, value)) throw new IOException("外屏不接受亮度设置，可使用显示器菜单。");
                changed = true;
                if (!GetMonitorBrightness(handle, out _, out var actual, out _) || Math.Abs(actual - (double)value) > Math.Max(1, (max - min) * .02))
                    throw new IOException("外屏亮度未确认，请刷新。");
            });
        }
        if (!changed) throw new ServiceException(FailureCode.DeviceGone, "原显示屏不可用，请刷新。");
        return true;
    }, ct);
    // Use the named property collection: dynamic WMI members failed on later instances
    // during real-machine refresh, although the first instance was readable.
    private static object ReadProperty(dynamic item, string name)
    {
        object? properties = null, property = null;
        try
        {
            properties = item.Properties_;
            property = ((dynamic)properties).Item(name);
            return ((dynamic)property).Value;
        }
        finally { Release(property); Release(properties); }
    }
    private static void QueryWmi(string className, Action<dynamic> action)
    {
        object? locator = null, service = null, security = null, collection = null;
        string phase = "创建连接";
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true)!);
            phase = "连接命名空间";
            service = ((dynamic)locator!).ConnectServer(".", "root\\wmi");
            phase = "设置连接上下文";
            security = ((dynamic)service).Security_;
            ((dynamic)security).ImpersonationLevel = 3;
            phase = "执行查询";
            collection = ((dynamic)service).ExecQuery("SELECT * FROM " + className);
            phase = "读取数量";
            int count = ((dynamic)collection).Count;
            for (int i = 0; i < count; i++)
            {
                phase = "获取对象";
                object item = ((dynamic)collection).ItemIndex(i);
                try { phase = "读取属性"; action(item); }
                finally { Release(item); }
            }
        }
        catch (COMException ex) when (ex.HResult != unchecked((int)0x80041010))
        { throw new ServiceException(FailureCode.NativeFailure, $"内屏亮度{phase}失败（{ex.HResult:X8}）。", ex.HResult); }
        finally { Release(collection); Release(security); Release(service); Release(locator); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private static void EnumeratePhysical(Action<string, string, nint> action)
    {
        Exception? error = null;
        MonitorCallback callback = (nint monitor, nint dc, ref Rect rect, nint data) =>
        {
            try
            {
                var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = "" };
                if (!GetMonitorInfo(monitor, ref info) || !GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count is 0 or > 16) return true;
                var physical = new PhysicalMonitor[count];
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) return true;
                try
                {
                    for (int i = 0; i < physical.Length; i++) action($"ddc:{info.Device}:{i}:{physical[i].Description}", physical[i].Description, physical[i].Handle);
                }
                finally { DestroyPhysicalMonitors(count, physical); }
                return true;
            }
            catch (Exception ex) { error = ex; return false; }
        };
        EnumDisplayMonitors(0, 0, callback, 0);
        if (error is not null) throw error;
    }
    public ValueTask DisposeAsync() => worker.DisposeAsync();
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect rect, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PhysicalMonitor
    { public nint Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("dxva2.dll")] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll", CharSet = CharSet.Unicode)] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] physical);
    [DllImport("dxva2.dll")] private static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] physical);
    [DllImport("dxva2.dll")] private static extern bool GetMonitorBrightness(nint handle, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll")] private static extern bool SetMonitorBrightness(nint handle, uint value);
}
