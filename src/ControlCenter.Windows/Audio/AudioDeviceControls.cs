using System.Runtime.InteropServices;
using ControlCenter.Core;
namespace ControlCenter.Windows.Audio;
public sealed partial class CoreAudioService
{
    public Task<MicrophoneState?> ReadMicrophoneAsync(CancellationToken ct) => worker.InvokeAsync(() =>
    {
        Ensure();
        int hr = enumerator!.GetDefaultAudioEndpoint(1, 1, out var device);
        if (hr == unchecked((int)0x80070490)) return null;
        Check(hr);
        try
        {
            Check(device.GetId(out var id));
            var volume = OpenVolume(id);
            try { Check(volume.GetMute(out var muted)); return new MicrophoneState(id, FriendlyName(device), muted); }
            finally { Release(volume); }
        }
        finally { Release(device); }
    }, ct);
    public Task SetMicrophoneMuteAsync(string id, bool muted, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        Ensure(); Check(enumerator!.GetDefaultAudioEndpoint(1, 1, out var device));
        try
        {
            Check(device.GetId(out var current));
            if (current != id) throw new ServiceException(FailureCode.DeviceGone, "输入设备已变化，请刷新后重试。");
            var volume = OpenVolume(id);
            try
            {
                var operationContext = context; Check(volume.SetMute(muted, ref operationContext));
                Check(volume.GetMute(out var actual));
                if (actual != muted) throw new ServiceException(FailureCode.VerificationFailed, "麦克风静音未确认。");
                return true;
            }
            finally { Release(volume); }
        }
        finally { Release(device); }
    }, ct);
    public Task SwitchOutputAsync(string id, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        Ensure();
        Check(enumerator!.EnumAudioEndpoints(0, 1, out var devices));
        bool found = false;
        try
        {
            Check(devices.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                Check(devices.Item(i, out var device));
                try { Check(device.GetId(out var candidate)); if (candidate == id) found = true; }
                finally { Release(device); }
            }
        }
        finally { Release(devices); }
        if (!found) throw new ServiceException(FailureCode.DeviceGone, "输出设备已断开。");
        // Isolated compatibility adapter: undocumented Windows policy interface, with readback and rollback.
        var previous = new[] { DefaultId(0), DefaultId(1) };
        var policy = (IEndpointPolicy)Activator.CreateInstance(Type.GetTypeFromCLSID(new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"), true)!)!;
        try
        {
            try
            {
                for (int role = 0; role < 2; role++) { ct.ThrowIfCancellationRequested(); Check(policy.SelectEndpoint(id, role)); }
                if (DefaultId(0) != id || DefaultId(1) != id) throw new ServiceException(FailureCode.VerificationFailed, "输出切换未确认。");
            }
            catch
            {
                bool restored = true;
                for (int role = 0; role < 2; role++)
                {
                    var current = DefaultId(role);
                    if (current == previous[role]) continue;
                    if (current == id && previous[role] is { } old) restored &= policy.SelectEndpoint(old, role) >= 0 && DefaultId(role) == old;
                    else restored = false; // Preserve an external change instead of overwriting it during rollback.
                }
                throw new ServiceException(restored ? FailureCode.Unavailable : FailureCode.VerificationFailed,
                    restored ? "直接切换失败，已恢复原输出；请使用系统声音设置。" : "切换和恢复未完全确认，请检查系统声音设置。");
            }
            Notify(); return true;
        }
        finally { Release(policy); }
    }, ct);
}
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IEndpointPolicy
{
    void Slot0(); void Slot1(); void Slot2(); void Slot3(); void Slot4();
    void Slot5(); void Slot6(); void Slot7(); void Slot8(); void Slot9();
    [PreserveSig] int SelectEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}
