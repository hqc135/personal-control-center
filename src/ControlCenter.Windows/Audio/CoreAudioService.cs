using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ControlCenter.Core;
namespace ControlCenter.Windows.Audio;
public sealed class CoreAudioService : IAudioService
{
    private readonly NativeWorker worker;
    private readonly AudioNotifications notifications;
    private IMMDeviceEnumerator? enumerator;
    private IAudioEndpointVolume? subscribedVolume;
    private string? subscribedId;
    private readonly Guid context = Guid.NewGuid();
    private int notificationQueued, disposed;
    public event Action? Invalidated;
    public CoreAudioService()
    {
        notifications = new AudioNotifications(Notify);
        worker = new("PCC Audio MTA", Cleanup);
    }
    private void Notify()
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref notificationQueued, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Interlocked.Exchange(ref notificationQueued, 0);
            if (Volatile.Read(ref disposed) == 0) Invalidated?.Invoke();
        });
    }
    private void Ensure()
    {
        if (enumerator is not null) return;
        var created = (IMMDeviceEnumerator)new DeviceEnumerator();
        try { Check(created.RegisterEndpointNotificationCallback(notifications)); enumerator = created; }
        catch { Marshal.FinalReleaseComObject(created); throw; }
    }
    public Task<AudioSnapshot> ReadAsync(CancellationToken ct) => worker.InvokeAsync(() =>
    {
        Ensure();
        var devices = ImmutableArray.CreateBuilder<AudioDevice>();
        Check(enumerator!.EnumAudioEndpoints(0, 1, out var collection));
        try
        {
            Check(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                Check(collection.Item(i, out var device));
                try
                {
                    Check(device.GetId(out var id));
                    devices.Add(new(id, FriendlyName(device)));
                }
                finally { Release(device); }
            }
        }
        finally { Release(collection); }
        var current = DefaultId(1); var communications = DefaultId(2);
        BindVolume(current);
        AudioLevel? level;
        try { level = current is not null && subscribedVolume is not null ? ReadLevel(current, subscribedVolume) : null; }
        catch { UnbindVolume(); throw; }
        return new AudioSnapshot(devices.ToImmutable(), current, communications, level, DefaultId(0));
    }, ct);
    public Task<AudioLevel> ReadLevelAsync(string id, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        Ensure(); var volume = OpenVolume(id);
        try { return ReadLevel(id, volume); } finally { Release(volume); }
    }, ct);
    public Task SetVolumeAsync(string id, float scalar, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        if (!float.IsFinite(scalar) || scalar is < 0 or > 1) throw new ServiceException(FailureCode.InvalidConfiguration, "音量范围无效。");
        Ensure(); var volume = OpenVolume(id);
        try { var operationContext = context; Check(volume.SetMasterVolumeLevelScalar(scalar, ref operationContext)); return true; }
        finally { Release(volume); }
    }, ct);
    public Task SetMuteAsync(string id, bool muted, CancellationToken ct) => worker.InvokeAsync(() =>
    {
        Ensure(); var volume = OpenVolume(id);
        try { var operationContext = context; Check(volume.SetMute(muted, ref operationContext)); return true; }
        finally { Release(volume); }
    }, ct);
    private string? DefaultId(int role)
    {
        int result = enumerator!.GetDefaultAudioEndpoint(0, role, out var device);
        if (result == unchecked((int)0x80070490)) return null; // No default endpoint.
        Check(result);
        try { Check(device.GetId(out var id)); return id; } finally { Release(device); }
    }
    private IAudioEndpointVolume OpenVolume(string id)
    {
        Check(enumerator!.GetDevice(id, out var device));
        try
        {
            Check(device.GetState(out var state));
            if (state != 1) throw new ServiceException(FailureCode.DeviceGone, "原输出设备已断开。");
            var iid = typeof(IAudioEndpointVolume).GUID;
            Check(device.Activate(ref iid, 23, 0, out var instance));
            return (IAudioEndpointVolume)instance;
        }
        finally { Release(device); }
    }
    private void BindVolume(string? id)
    {
        if (subscribedId == id) return;
        UnbindVolume();
        if (id is null) return;
        var volume = OpenVolume(id);
        try { Check(volume.RegisterControlChangeNotify(notifications)); subscribedVolume = volume; subscribedId = id; }
        catch { Release(volume); throw; }
    }
    private void UnbindVolume()
    {
        if (subscribedVolume is not null)
        {
            int result = subscribedVolume.UnregisterControlChangeNotify(notifications);
            Release(subscribedVolume); subscribedVolume = null; subscribedId = null;
            if (result < 0) System.Diagnostics.Trace.WriteLine($"Audio unsubscribe HRESULT: {result:X8}");
        }
    }
    private static AudioLevel ReadLevel(string id, IAudioEndpointVolume volume)
    {
        Check(volume.GetMasterVolumeLevelScalar(out var scalar)); Check(volume.GetMute(out var muted));
        return new(id, scalar, muted);
    }
    private static string FriendlyName(IMMDevice device)
    {
        Check(device.OpenPropertyStore(0, out var properties));
        var key = new PropertyKey { FormatId = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
        var value = new PropVariant();
        try
        {
            Check(properties.GetValue(ref key, out value));
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "音频输出" : "音频输出";
        }
        finally { PropVariantClear(ref value); Release(properties); }
    }
    private static void Check(int hr)
    {
        if (hr >= 0) return;
        var code = hr == unchecked((int)0x80070005) ? FailureCode.PermissionDenied :
            hr is unchecked((int)0x80070490) or unchecked((int)0x88890004) ? FailureCode.DeviceGone : FailureCode.NativeFailure;
        throw new ServiceException(code, code == FailureCode.DeviceGone ? "音频设备已不可用，请刷新。" : "音频接口调用失败。", hr);
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    private void Cleanup()
    {
        UnbindVolume();
        if (enumerator is not null)
        {
            int result = enumerator.UnregisterEndpointNotificationCallback(notifications);
            Release(enumerator); enumerator = null;
            if (result < 0) System.Diagnostics.Trace.WriteLine($"Endpoint unsubscribe HRESULT: {result:X8}");
        }
    }
    public async ValueTask DisposeAsync() { if (Interlocked.Exchange(ref disposed, 1) == 0) await worker.DisposeAsync(); }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}

