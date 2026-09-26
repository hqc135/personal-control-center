using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace ControlCenter.Core;
public sealed record ProxyConfig(string Host = "127.0.0.1", int Port = 7897, string? TestUrl = null, string? ShortcutId = null)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Additional { get; init; }
}
public static class ProxyPolicy
{
    public static void Validate(ProxyConfig config)
    {
        if (config is null || !IPAddress.TryParse(config.Host, out var ip) || !IPAddress.IsLoopback(ip) || config.Port is < 1 or > 65535)
            throw new InvalidDataException("代理地址须为本机 IP 字面量，端口须为 1–65535。");
        if (config.TestUrl is not null && (!Uri.TryCreate(config.TestUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || config.TestUrl.Length > 2048))
            throw new InvalidDataException("测试目标须为不含账户信息和片段的 HTTP/HTTPS 地址。");
    }
}
public enum ProxyResultKind { LocalReachable, LocalUnavailable, HttpResponse, ProxyFailure, TlsFailure, TransportFailure, Timeout, Cancelled }
public sealed record ProxyResult(ProxyResultKind Kind, int? StatusCode = null)
{
    public string Message => Kind switch
    {
        ProxyResultKind.LocalReachable => "本机端口可达；尚不能确认服务身份或外网连接。",
        ProxyResultKind.LocalUnavailable => "本机端口不可达。",
        ProxyResultKind.HttpResponse => $"收到 HTTP {StatusCode}；仅代表此目标的响应。",
        ProxyResultKind.ProxyFailure => "代理协商失败或代理要求认证。",
        ProxyResultKind.TlsFailure => "TLS 握手失败；未绕过证书验证。",
        ProxyResultKind.Timeout => "检测超时。",
        ProxyResultKind.Cancelled => "检测已取消。",
        _ => "连接失败；无法确认具体链路原因。"
    };
}
public interface IProxyProbe
{
    Task<ProxyResult> LocalAsync(ProxyConfig config, CancellationToken ct);
    Task<ProxyResult> RemoteAsync(ProxyConfig config, CancellationToken ct);
}
// UI-thread-owned scheduling; generations also reject transports that finish after cancellation.
public sealed class ProxyMonitor(IProxyProbe probe, Action<Action> dispatch, TimeProvider? time = null) : IDisposable
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private ProxyConfig config = new();
    private CancellationTokenSource? visibleLifetime, remoteLifetime;
    private long generation;
    private long? lastLocal;
    private bool visible, disposed, localBusy;
    public ProxyResult? Local { get; private set; }
    public ProxyResult? Remote { get; private set; }
    public bool RemoteBusy { get; private set; }
    public event Action? Changed;
    public void Configure(ProxyConfig value)
    {
        ProxyPolicy.Validate(value);
        if (config == value) return;
        bool wasVisible = visible; SetVisible(false); config = value;
        Local = null; Remote = null; Changed?.Invoke();
        if (wasVisible) SetVisible(true);
    }
    public void SetVisible(bool value)
    {
        if (disposed || visible == value) return;
        visible = value; generation++;
        visibleLifetime?.Cancel(); visibleLifetime = null;
        remoteLifetime?.Cancel(); RemoteBusy = false;
        if (!value) { if (Remote is null) Remote = new(ProxyResultKind.Cancelled); Changed?.Invoke(); return; }
        visibleLifetime = new();
        _ = PollAsync(visibleLifetime);
    }
    private async Task PollAsync(CancellationTokenSource lifetime)
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await RefreshLocalAsync();
                await Task.Delay(TimeSpan.FromSeconds(10), clock, lifetime.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { lifetime.Dispose(); }
    }
    public async Task RefreshLocalAsync()
    {
        if (!visible || disposed || localBusy || (lastLocal is { } previous && clock.GetElapsedTime(previous) < TimeSpan.FromSeconds(10))) return;
        long epoch = generation; var selected = config; var ct = visibleLifetime!.Token;
        lastLocal = clock.GetTimestamp(); localBusy = true;
        ProxyResult result;
        try { result = await probe.LocalAsync(selected, ct); }
        catch (OperationCanceledException) { result = new(ProxyResultKind.Cancelled); }
        catch (Exception) { result = new(ProxyResultKind.TransportFailure); }
        finally { localBusy = false; }
        dispatch(() => { if (!disposed && visible && generation == epoch) { Local = result; Changed?.Invoke(); } });
    }
    public async Task TestRemoteAsync()
    {
        if (!visible || disposed || RemoteBusy || config.TestUrl is null) return;
        long epoch = generation; var selected = config;
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(visibleLifetime!.Token);
        remoteLifetime = lifetime; RemoteBusy = true; Remote = null; Changed?.Invoke();
        ProxyResult result;
        try { result = await probe.RemoteAsync(selected, lifetime.Token); }
        catch (OperationCanceledException) { result = new(ProxyResultKind.Cancelled); }
        catch (Exception) { result = new(ProxyResultKind.TransportFailure); }
        finally { lifetime.Dispose(); if (ReferenceEquals(remoteLifetime, lifetime)) remoteLifetime = null; }
        dispatch(() => { if (!disposed && visible && generation == epoch) { RemoteBusy = false; Remote = result; Changed?.Invoke(); } });
    }
    public void Dispose() { if (disposed) return; SetVisible(false); disposed = true; }
}
