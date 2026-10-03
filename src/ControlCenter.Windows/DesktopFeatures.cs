using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using ControlCenter.Core;
using ControlCenter.Windows.Audio;
using Windows.Devices.Radios;
using Windows.Media.Control;
namespace ControlCenter.Windows;

public sealed class DesktopFeatures(CoreAudioService audio, BrightnessService brightness) : IDesktopFeatures
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    public async Task<DesktopSnapshot> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var errors = new List<string>();
            MicrophoneState? mic = null; DisplayBrightness[] displays = []; MediaSession[] media = [];
            string battery = "", network = "未连接", bluetooth = "状态未知";
            if (GetSystemPowerStatus(out var status))
            {
                if ((status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255)
                    battery = (status.BatteryLifePercent <= 100 ? $"{status.BatteryLifePercent}%" : "电量未知") +
                        (status.ACLineStatus == 1 ? ((status.BatteryFlag & 8) != 0 ? " · 正在充电" : " · 已接电源") : " · 电池供电");
            }
            else errors.Add("电池读取失败");
            try { mic = await audio.ReadMicrophoneAsync(ct); } catch { errors.Add("麦克风读取失败"); }
            try { displays = await brightness.ReadAsync(ct); } catch (ServiceException ex) { errors.Add(ex.Message); } catch { errors.Add("亮度接口不可用，可打开显示设置"); }
            try
            {
                var links = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .Select(x => new { x.Name, IP = x.GetIPProperties() })
                    .Where(x => x.IP.UnicastAddresses.Any(a => !System.Net.IPAddress.IsLoopback(a.Address)))
                    .ToArray();
                var routed = links.Where(x => x.IP.GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any))).ToArray();
                var shown = (routed.Length > 0 ? routed : links).Take(2).ToArray();
                network = string.Join(" · ", shown.Select(x => x.Name));
                if (links.Length > shown.Length) network += $"（另有 {links.Length - shown.Length} 个本地连接）";
                if (network.Length == 0) network = "未连接";
                else network += " · 链路已连接（不代表互联网可用）";
            }
            catch { network = "网络状态未知"; }
            try
            {
                var radios = await Radio.GetRadiosAsync().AsTask(ct);
                var bt = radios.Where(x => x.Kind == RadioKind.Bluetooth).ToArray();
                bluetooth = bt.Length == 0 ? "未发现可访问的蓝牙适配器" : string.Join(" · ", bt.Select(x => x.Name + ": " + (x.State == RadioState.On ? "已开启" : x.State == RadioState.Off ? "已关闭" : "不可用")));
            }
            catch { bluetooth = "蓝牙状态不可读，请打开设置"; }
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct);
                var items = new List<MediaSession>();
                foreach (var session in manager.GetSessions())
                {
                    try
                    {
                        var properties = await session.TryGetMediaPropertiesAsync().AsTask(ct);
                        var playback = session.GetPlaybackInfo(); var controls = playback.Controls;
                        items.Add(new(session.SourceAppUserModelId, properties.Title, properties.Artist,
                            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                            controls.IsPlayPauseToggleEnabled, controls.IsPreviousEnabled, controls.IsNextEnabled));
                    }
                    catch { errors.Add("一个媒体会话已失效"); }
                }
                media = items.ToArray();
            }
            catch { errors.Add("媒体会话不可用"); }
            return new(battery, network, bluetooth, mic, displays, media, errors.ToArray());
        }
        finally { gate.Release(); }
    }
    public async Task MediaAsync(string id, string action, CancellationToken ct)
    {
        manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct);
        var session = manager.GetSessions().FirstOrDefault(x => x.SourceAppUserModelId == id)
            ?? throw new ServiceException(FailureCode.DeviceGone, "播放器已关闭。");
        bool accepted = action switch
        {
            "previous" => await session.TrySkipPreviousAsync().AsTask(ct),
            "next" => await session.TrySkipNextAsync().AsTask(ct),
            "toggle" => await session.TryTogglePlayPauseAsync().AsTask(ct),
            _ => throw new InvalidDataException("未知媒体操作。")
        };
        if (!accepted) throw new ServiceException(FailureCode.Unavailable, "播放器未接受操作。");
    }
    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public uint BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
}
