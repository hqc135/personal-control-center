using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using ControlCenter.Core;
using ControlCenter.Windows;
using Xunit;
namespace ControlCenter.Tests;
internal sealed class FakeTransport : IProxyTransport
{
    public Func<CancellationToken, Task> Connect = _ => Task.CompletedTask;
    public Func<CancellationToken, Task<int>> Request = _ => Task.FromResult(200);
    public Task ConnectAsync(IPAddress address, int port, CancellationToken ct) => Connect(ct);
    public Task<int> RequestAsync(ProxyConfig config, CancellationToken ct) => Request(ct);
}
internal sealed class FakeProbe : IProxyProbe
{
    public int LocalCalls, RemoteCalls;
    public CancellationToken LocalToken, RemoteToken;
    public Func<Task<ProxyResult>> Local = () => Task.FromResult(new ProxyResult(ProxyResultKind.LocalReachable));
    public Func<Task<ProxyResult>> Remote = () => Task.FromResult(new ProxyResult(ProxyResultKind.HttpResponse, 200));
    public Task<ProxyResult> LocalAsync(ProxyConfig config, CancellationToken ct) { LocalCalls++; LocalToken = ct; return Local(); }
    public Task<ProxyResult> RemoteAsync(ProxyConfig config, CancellationToken ct) { RemoteCalls++; RemoteToken = ct; return Remote(); }
}
public class ProxyTests
{
    private static ProxyConfig Config => new(TestUrl: "https://example.com/");
    [Theory] [InlineData("localhost", 7897)] [InlineData("192.168.1.1", 7897)] [InlineData("0.0.0.0", 80)]
    [InlineData("127.0.0.1", 0)] [InlineData("::1", 65536)]
    public void RejectsNonLoopbackOrInvalidPort(string host, int port)
        => Assert.Throws<InvalidDataException>(() => ProxyPolicy.Validate(new(host, port)));
    [Theory] [InlineData("127.0.0.1")] [InlineData("::1")]
    public void AcceptsLiteralLoopback(string host) => ProxyPolicy.Validate(new(host));
    [Theory] [InlineData("https://user:password@example.com")] [InlineData("file:///C:/secret")] [InlineData("https://example.com/#secret")] [InlineData("bad")]
    public void RejectsUnsafeTarget(string target) => Assert.Throws<InvalidDataException>(() => ProxyPolicy.Validate(new(TestUrl: target)));
    [Fact] public void ExplicitHandlerDisablesAmbientStateAndRedirectsWithoutOpeningConnection()
    {
        using var handler = ProxyTransport.CreateHandler(Config);
        Assert.True(handler.UseProxy); Assert.NotNull(handler.Proxy);
        Assert.False(handler.UseCookies); Assert.False(handler.AllowAutoRedirect);
        Assert.Null(handler.Credentials); Assert.Null(handler.DefaultProxyCredentials); Assert.Null(handler.Proxy.Credentials);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(new Uri("http://127.0.0.1:7897"), handler.Proxy.GetProxy(new Uri(Config.TestUrl!)));
    }
    [Theory] [InlineData(200)] [InlineData(403)] [InlineData(302)] [InlineData(500)]
    public async Task AnyHttpResponseIsReportedAsResponse(int status)
    {
        var probe = new ProxyProbe(new FakeTransport { Request = _ => Task.FromResult(status) });
        var result = await probe.RemoteAsync(Config, default);
        Assert.Equal(ProxyResultKind.HttpResponse, result.Kind); Assert.Equal(status, result.StatusCode);
    }
    [Fact] public async Task ProxyAuthenticationResponseIsDistinct()
    {
        var result = await new ProxyProbe(new FakeTransport { Request = _ => Task.FromResult(407) }).RemoteAsync(Config, default);
        Assert.Equal(ProxyResultKind.ProxyFailure, result.Kind);
    }
    [Theory] [InlineData(HttpRequestError.ProxyTunnelError, ProxyResultKind.ProxyFailure)]
    [InlineData(HttpRequestError.SecureConnectionError, ProxyResultKind.TlsFailure)]
    [InlineData(HttpRequestError.ConnectionError, ProxyResultKind.TransportFailure)]
    public async Task ReportsOnlySupportedErrorAttribution(HttpRequestError error, ProxyResultKind expected)
    {
        var probe = new ProxyProbe(new FakeTransport { Request = _ => throw new HttpRequestException(error) });
        Assert.Equal(expected, (await probe.RemoteAsync(Config, default)).Kind);
    }
    [Fact] public async Task LocalRefusalIsNotGlobalInternetFailure()
    {
        var probe = new ProxyProbe(new FakeTransport { Connect = _ => throw new SocketException((int)SocketError.ConnectionRefused) });
        Assert.Equal(ProxyResultKind.LocalUnavailable, (await probe.LocalAsync(Config, default)).Kind);
    }
    [Fact] public async Task LocalDeadlineCancelsTransport()
    {
        var probe = new ProxyProbe(new FakeTransport { Connect = ct => Task.Delay(Timeout.Infinite, ct) });
        var result = await probe.LocalAsync(Config, default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(ProxyResultKind.Timeout, result.Kind);
    }
    [Fact] public async Task CallerCancellationIsNotTimeout()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var probe = new ProxyProbe(new FakeTransport { Request = async ct => { await Task.Delay(Timeout.Infinite, ct); return 200; } });
        Assert.Equal(ProxyResultKind.Cancelled, (await probe.RemoteAsync(Config, cts.Token)).Kind);
    }
    [Fact] public async Task VisiblePollingNeverTestsRemoteAndRapidReopenIsRateLimited()
    {
        var clock = new TestClock(); var probe = new FakeProbe();
        using var monitor = new ProxyMonitor(probe, action => action(), clock);
        monitor.Configure(Config); Assert.Equal(0, probe.LocalCalls);
        monitor.SetVisible(true); Assert.Equal(1, probe.LocalCalls); Assert.Equal(0, probe.RemoteCalls);
        monitor.SetVisible(false); monitor.SetVisible(true); await monitor.RefreshLocalAsync();
        Assert.Equal(1, probe.LocalCalls);
        clock.Advance(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)); await monitor.RefreshLocalAsync();
        Assert.Equal(2, probe.LocalCalls); Assert.Equal(0, probe.RemoteCalls);
    }
    [Fact] public async Task HiddenPanelCancelsRemoteAndRejectsLateResponse()
    {
        var delayed = new TaskCompletionSource<ProxyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new FakeProbe { Remote = () => delayed.Task };
        using var monitor = new ProxyMonitor(probe, action => action()); monitor.Configure(Config); monitor.SetVisible(true);
        var request = monitor.TestRemoteAsync(); await monitor.TestRemoteAsync(); Assert.Equal(1, probe.RemoteCalls);
        monitor.SetVisible(false); Assert.True(probe.RemoteToken.IsCancellationRequested);
        delayed.SetResult(new(ProxyResultKind.HttpResponse, 200)); await request;
        Assert.Equal(ProxyResultKind.Cancelled, monitor.Remote?.Kind); Assert.False(monitor.RemoteBusy);
    }
    [Fact] public async Task HiddenLocalResponseCannotReplaceNewConfigState()
    {
        var delayed = new TaskCompletionSource<ProxyResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new FakeProbe { Local = () => delayed.Task };
        using var monitor = new ProxyMonitor(probe, action => action()); monitor.SetVisible(true);
        monitor.SetVisible(false); Assert.True(probe.LocalToken.IsCancellationRequested);
        monitor.Configure(new(Port: 9999)); delayed.SetResult(new(ProxyResultKind.LocalReachable));
        await Task.Delay(30); Assert.Null(monitor.Local); Assert.Equal(0, probe.RemoteCalls);
    }
    [Fact] public async Task MissingTargetDoesNotStartRemoteProbe()
    {
        var probe = new FakeProbe(); using var monitor = new ProxyMonitor(probe, action => action());
        monitor.SetVisible(true); await monitor.TestRemoteAsync(); Assert.Equal(0, probe.RemoteCalls);
    }
    [Fact] public void ConfigRoundtripPreservesProxyExtensionFields()
    {
        var decoded = ConfigCodec.Decode(System.Text.Encoding.UTF8.GetBytes("""{"schemaVersion":1,"proxy":{"host":"::1","port":9000,"future":"keep"}}"""));
        Assert.Equal("keep", ConfigCodec.Decode(ConfigCodec.Encode(decoded)).Proxy.Additional!["future"].GetString());
    }
    [Fact] public void ProxyLaunchMustReferenceApplicationEntry()
        => Assert.Throws<InvalidDataException>(() => ConfigCodec.Validate(new() { Proxy = new(ShortcutId: "downloads") }));
}
