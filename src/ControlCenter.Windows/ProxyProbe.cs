using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public interface IProxyTransport
{
    Task ConnectAsync(IPAddress address, int port, CancellationToken ct);
    Task<int> RequestAsync(ProxyConfig config, CancellationToken ct);
}
public sealed class ProxyTransport : IProxyTransport
{
    public async Task ConnectAsync(IPAddress address, int port, CancellationToken ct)
    {
        using var client = new TcpClient(address.AddressFamily);
        await client.ConnectAsync(address, port, ct);
    }
    public static SocketsHttpHandler CreateHandler(ProxyConfig config)
    {
        ProxyPolicy.Validate(config);
        return new()
        {
            UseProxy = true,
            Proxy = new WebProxy(new UriBuilder("http", config.Host, config.Port).Uri) { BypassProxyOnLocal = false, Credentials = null },
            UseCookies = false, Credentials = null, DefaultProxyCredentials = null,
            AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(5),
            MaxResponseHeadersLength = 32
        };
    }
    public async Task<int> RequestAsync(ProxyConfig config, CancellationToken ct)
    {
        using var handler = CreateHandler(config);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, config.TestUrl);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        return (int)response.StatusCode; // No response body read, no redirect, no implicit proxy or credentials.
    }
}
public sealed class ProxyProbe(IProxyTransport transport) : IProxyProbe
{
    public Task<ProxyResult> LocalAsync(ProxyConfig config, CancellationToken ct) => RunAsync(config, false, ct);
    public Task<ProxyResult> RemoteAsync(ProxyConfig config, CancellationToken ct) => RunAsync(config, true, ct);
    private async Task<ProxyResult> RunAsync(ProxyConfig config, bool remote, CancellationToken ct)
    {
        ProxyPolicy.Validate(config);
        if (remote && config.TestUrl is null) throw new InvalidDataException("请先设置测试目标。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(remote ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(500));
        try
        {
            if (!remote) { await transport.ConnectAsync(IPAddress.Parse(config.Host), config.Port, deadline.Token); return new(ProxyResultKind.LocalReachable); }
            int status = await transport.RequestAsync(config, deadline.Token);
            return status == 407 ? new(ProxyResultKind.ProxyFailure, status) : new(ProxyResultKind.HttpResponse, status);
        }
        catch (OperationCanceledException) { return new(ct.IsCancellationRequested ? ProxyResultKind.Cancelled : ProxyResultKind.Timeout); }
        catch (HttpRequestException ex) { return new(ex.HttpRequestError switch {
            HttpRequestError.ProxyTunnelError => ProxyResultKind.ProxyFailure,
            HttpRequestError.SecureConnectionError => ProxyResultKind.TlsFailure,
            _ => ProxyResultKind.TransportFailure }); }
        catch (SocketException) { return new(remote ? ProxyResultKind.TransportFailure : ProxyResultKind.LocalUnavailable); }
    }
}
