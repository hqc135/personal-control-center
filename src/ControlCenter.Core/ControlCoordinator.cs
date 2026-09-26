namespace ControlCenter.Core;
public sealed record VolumeRequest(string EndpointId, float Scalar);
public sealed class ControlCoordinator : IAsyncDisposable
{
    private readonly IAudioService audio;
    private readonly IPowerService power;
    private readonly LatestCommandQueue<VolumeRequest> volumeQueue;
    private readonly LatestCommandQueue<Guid> powerQueue;
    private readonly SemaphoreSlim audioWrites = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private int audioRefreshRequested, powerRefreshRequested, audioRefreshRunning, powerRefreshRunning;
    private volatile bool visible, disposed;
    private volatile bool audioEnabled = true, powerEnabled = true;
    private CancellationTokenSource? visibleLifetime;
    public ModuleStore<AudioSnapshot> Audio { get; } = new();
    public ModuleStore<PowerSnapshot> Power { get; } = new();
    public ControlCoordinator(IAudioService audio, IPowerService power, TimeSpan? volumeInterval = null)
    {
        this.audio = audio; this.power = power;
        volumeQueue = new(WriteVolumeAsync, volumeInterval ?? TimeSpan.FromMilliseconds(34));
        powerQueue = new(WritePowerAsync, TimeSpan.Zero);
        audio.Invalidated += OnAudioInvalidated; power.Invalidated += OnPowerInvalidated;
    }
    public Task<CommandResult> SetVolumeAsync(string endpointId, float scalar)
    {
        if (!float.IsFinite(scalar) || scalar is < 0 or > 1)
            return Task.FromResult(new CommandResult(CommandOutcome.Failed, FailureCode.InvalidConfiguration, "音量范围无效。"));
        return volumeQueue.SubmitAsync(new(endpointId, scalar));
    }
    public Task<CommandResult> SetPowerAsync(Guid id) => powerQueue.SubmitAsync(id);
    public void CancelPendingVolume() => volumeQueue.CancelPending();
    public void SetModules(bool audioVisible, bool powerVisible)
    {
        if (audioEnabled == audioVisible && powerEnabled == powerVisible) return;
        bool wasVisible = visible; SetVisible(false);
        audioEnabled = audioVisible; powerEnabled = powerVisible;
        if (wasVisible) SetVisible(true);
    }
    public void SetVisible(bool value)
    {
        if (disposed || visible == value) return;
        visible = value;
        visibleLifetime?.Cancel();
        if (value)
        {
            RequestAudioRefresh(); RequestPowerRefresh();
            if (!powerEnabled) return;
            var panelLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            visibleLifetime = panelLifetime;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
                    while (await timer.WaitForNextTickAsync(panelLifetime.Token)) RequestPowerRefresh();
                }
                catch (OperationCanceledException) { }
                finally { panelLifetime.Dispose(); }
            });
        }
        else visibleLifetime = null;
    }
    public Task RefreshAsync() => Task.WhenAll(audioEnabled ? RefreshAudioAsync(lifetime.Token) : Task.CompletedTask, powerEnabled ? RefreshPowerAsync(lifetime.Token) : Task.CompletedTask);
    public async Task RefreshAudioAsync(CancellationToken ct = default)
    {
        var token = Audio.BeginRead();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { Audio.CompleteRead(token, await audio.ReadAsync(deadline.Token).WaitAsync(deadline.Token)); }
        catch (Exception ex) { Audio.FailRead(token, Describe(ex)); }
    }
    public async Task RefreshPowerAsync(CancellationToken ct = default)
    {
        var token = Power.BeginRead();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try { Power.CompleteRead(token, await power.ReadAsync(deadline.Token).WaitAsync(deadline.Token)); }
        catch (Exception ex) { Power.FailRead(token, Describe(ex)); }
    }
    private async Task<CommandResult> WriteVolumeAsync(VolumeRequest request, CancellationToken ct)
    {
        await audioWrites.WaitAsync(ct);
        try
        {
            Audio.BeginWrite();
            return await VerifyAudioAsync(request.EndpointId,
                () => audio.SetVolumeAsync(request.EndpointId, request.Scalar, ct),
                value => Math.Abs(value.Volume - request.Scalar) <= 0.0051f, ct);
        }
        finally { audioWrites.Release(); }
    }
    public async Task<CommandResult> SetMuteAsync(string id, bool muted)
    {
        var ct = lifetime.Token;
        await audioWrites.WaitAsync(ct);
        try { Audio.BeginWrite(); return await VerifyAudioAsync(id, () => audio.SetMuteAsync(id, muted, ct), value => value.Muted == muted, ct); }
        finally { audioWrites.Release(); }
    }
    private async Task<CommandResult> VerifyAudioAsync(string id, Func<Task> write, Func<AudioLevel, bool> matches, CancellationToken ct)
    {
        CommandResult result;
        bool attempted = false;
        try
        {
            // Re-check membership immediately before the write; a detached target must never redirect to another device.
            using var preflight = CancellationTokenSource.CreateLinkedTokenSource(ct);
            preflight.CancelAfter(TimeSpan.FromSeconds(2));
            var before = await audio.ReadAsync(preflight.Token).WaitAsync(preflight.Token);
            if (!before.Devices.Any(x => x.Id == id)) throw new ServiceException(FailureCode.DeviceGone, "原输出设备已断开，操作已停止。");
            ct.ThrowIfCancellationRequested();
            attempted = true; await write();
            // Cancellation after entering a native API cannot retract its side effect. Verify with a separate bounded token.
            using var verify = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var actual = await audio.ReadLevelAsync(id, verify.Token).WaitAsync(verify.Token);
            result = actual.EndpointId == id && matches(actual) ? CommandResult.Confirmed : new(CommandOutcome.Failed, FailureCode.VerificationFailed, "系统回读与请求不一致，请重试。");
        }
        catch (Exception ex) { result = Failure(ex, attempted); }
        using (var refresh = new CancellationTokenSource(TimeSpan.FromSeconds(2))) await RefreshAudioAsync(refresh.Token);
        Audio.CompleteWrite(result); return result;
    }
    private async Task<CommandResult> WritePowerAsync(Guid id, CancellationToken ct)
    {
        Power.BeginWrite();
        CommandResult result; bool attempted = false;
        try
        {
            using var preflight = CancellationTokenSource.CreateLinkedTokenSource(ct);
            preflight.CancelAfter(TimeSpan.FromSeconds(2));
            var before = await power.ReadAsync(preflight.Token).WaitAsync(preflight.Token);
            if (!before.Schemes.Any(x => x.Id == id)) throw new ServiceException(FailureCode.Unavailable, "该电源方案已不存在。");
            ct.ThrowIfCancellationRequested(); attempted = true;
            await power.ActivateAsync(id, ct);
            using var verify = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var actual = await power.ReadAsync(verify.Token).WaitAsync(verify.Token);
            result = actual.ActiveId == id ? CommandResult.Confirmed : new(CommandOutcome.Failed, FailureCode.VerificationFailed, "电源方案未确认切换，请重试。");
        }
        catch (Exception ex) { result = Failure(ex, attempted); }
        using (var refresh = new CancellationTokenSource(TimeSpan.FromSeconds(2))) await RefreshPowerAsync(refresh.Token);
        Power.CompleteWrite(result); return result;
    }
    private void OnAudioInvalidated() { Audio.Invalidate(); if (visible) RequestAudioRefresh(); }
    private void OnPowerInvalidated() { Power.Invalidate(); if (visible) RequestPowerRefresh(); }
    private void RequestAudioRefresh()
    {
        if (!audioEnabled) return;
        Interlocked.Exchange(ref audioRefreshRequested, 1);
        if (Interlocked.CompareExchange(ref audioRefreshRunning, 1, 0) == 0) _ = Task.Run(async () =>
        {
            try { while (Interlocked.Exchange(ref audioRefreshRequested, 0) != 0 && !disposed && visible) await RefreshAudioAsync(lifetime.Token); }
            finally { Interlocked.Exchange(ref audioRefreshRunning, 0); if (Volatile.Read(ref audioRefreshRequested) != 0 && !disposed && visible) RequestAudioRefresh(); }
        });
    }
    private void RequestPowerRefresh()
    {
        if (!powerEnabled) return;
        Interlocked.Exchange(ref powerRefreshRequested, 1);
        if (Interlocked.CompareExchange(ref powerRefreshRunning, 1, 0) == 0) _ = Task.Run(async () =>
        {
            try { while (Interlocked.Exchange(ref powerRefreshRequested, 0) != 0 && !disposed && visible) await RefreshPowerAsync(lifetime.Token); }
            finally { Interlocked.Exchange(ref powerRefreshRunning, 0); if (Volatile.Read(ref powerRefreshRequested) != 0 && !disposed && visible) RequestPowerRefresh(); }
        });
    }
    private static string Describe(Exception ex) => ex switch
    {
        ServiceException service => service.Message,
        OperationCanceledException or TimeoutException => "读取超时或已取消，状态待刷新。",
        _ => "系统状态读取失败，请刷新。"
    };
    private static CommandResult Failure(Exception ex, bool attempted)
        => new(attempted ? CommandOutcome.UnknownOutcome : CommandOutcome.Failed,
            ex is ServiceException service ? service.Code : ex is OperationCanceledException or TimeoutException ? FailureCode.Timeout : FailureCode.NativeFailure,
            (attempted ? "操作结果待确认。" : "") + Describe(ex));
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true; visible = false;
        audio.Invalidated -= OnAudioInvalidated; power.Invalidated -= OnPowerInvalidated;
        lifetime.Cancel();
        await volumeQueue.DisposeAsync(); await powerQueue.DisposeAsync();
        await audioWrites.WaitAsync();
        audioWrites.Release();
        // Services own native resources; the composition root disposes them after the coordinator.
    }
}

