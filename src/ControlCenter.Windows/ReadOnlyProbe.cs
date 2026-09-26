using System.Runtime.InteropServices;
namespace ControlCenter.Windows;
public sealed record ProbeResult(int AudioEndpointCount, int AudioHResult, int PowerSchemeCount, uint PowerError, bool ActivePowerAvailable);
public static class ReadOnlyProbe
{
    // Deliberately exposes counts only: endpoint IDs and personal device names are not written to evidence.
    public static ProbeResult Run()
    {
        int count = 0, hr = 0, schemes = 0; uint error = 0;
        IMMDeviceEnumerator? enumerator = null; IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            hr = enumerator.EnumAudioEndpoints(0, 1, out collection);
            if (hr >= 0) { hr = collection.GetCount(out uint value); count = (int)value; }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { hr = ex.HResult; }
        finally
        {
            if (collection is not null) Marshal.FinalReleaseComObject(collection);
            if (enumerator is not null) Marshal.FinalReleaseComObject(enumerator);
        }
        for (uint index = 0; index < 256; index++)
        {
            uint size = 16;
            error = PowerEnumerate(0, 0, 0, 16, index, out _, ref size);
            if (error == 259) { error = 0; break; }
            if (error != 0) break;
            schemes++;
        }
        uint activeResult = PowerGetActiveScheme(0, out var active);
        if (active != 0) LocalFree(active);
        return new(count, hr, schemes, error, activeResult == 0);
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint count); }
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(nint root, nint scheme, nint subgroup, uint access, uint index, out Guid buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}

