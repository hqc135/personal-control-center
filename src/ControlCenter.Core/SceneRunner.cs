namespace ControlCenter.Core;
public sealed class SceneRunner(ControlCoordinator coordinator, IAwakeService awake)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private SceneDefinition? applied;
    private AudioLevel? previousAudio;
    private Guid? previousPower;
    private AwakeSnapshot? previousAwake;
    private DateTimeOffset? appliedDeadline;
    public bool CanRestore => applied is not null;
    public async Task<string> ApplyAsync(SceneDefinition scene)
    {
        scene.Validate(); await gate.WaitAsync();
        try
        {
            if (applied is not null) return "请先恢复上一个场景，再执行新场景。";
            await Task.WhenAll(coordinator.RefreshAudioAsync(), coordinator.RefreshPowerAsync());
            if ((scene.Volume is not null || scene.Muted is not null) && (coordinator.Audio.Current.IsStale || coordinator.Audio.Current.Value?.Level is null))
                return "音频状态不可读，场景未执行。";
            if (scene.PowerScheme is { } scheme && (coordinator.Power.Current.IsStale || coordinator.Power.Current.Value?.Schemes.Any(x => x.Id == scheme) != true))
                return "电源方案不可用，场景未执行。";
            if (scene.AwakeMinutes is not null && awake.Current.Status == AwakeStatus.ReleaseUnconfirmed) return "请先结束未确认的唤醒请求。";
            previousAudio = coordinator.Audio.Current.Value?.Level; previousPower = coordinator.Power.Current.Value?.ActiveId;
            previousAwake = awake.Current; applied = scene;
            var log = new List<string>();
            async Task Step(string name, Func<Task<CommandResult>> action)
            {
                var result = await action();
                log.Add(name + (result.Outcome == CommandOutcome.Confirmed ? "：已确认" : "：" + (result.Message ?? "未确认")));
                if (result.Outcome != CommandOutcome.Confirmed) throw new InvalidOperationException();
            }
            try
            {
                if (scene.Volume is { } v) await Step("音量", () => coordinator.SetVolumeAsync(previousAudio!.EndpointId, v / 100f));
                if (scene.Muted is { } m) await Step("静音", () => coordinator.SetMuteAsync(previousAudio!.EndpointId, m));
                if (scene.PowerScheme is { } p) await Step("电源", () => coordinator.SetPowerAsync(p));
                if (scene.AwakeMinutes is { } a)
                {
                    await Step("保持唤醒", () => awake.StartAsync(a, scene.KeepDisplay));
                    appliedDeadline = awake.Current.Deadline;
                }
            }
            catch { log.Add("已停止后续步骤；可恢复已生效的项目。"); }
            return string.Join("\n", log);
        }
        finally { gate.Release(); }
    }
    public async Task<string> RestoreAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (applied is not { } scene) return "没有待恢复场景。";
            await Task.WhenAll(coordinator.RefreshAudioAsync(), coordinator.RefreshPowerAsync());
            var log = new List<string>(); bool failed = false;
            async Task Restore(string label, Func<Task<CommandResult>> action)
            {
                var result = await action();
                failed |= result.Outcome != CommandOutcome.Confirmed;
                log.Add(label + (result.Outcome == CommandOutcome.Confirmed ? "已恢复" : "恢复未确认，可重试"));
            }
            var audio = coordinator.Audio.Current;
            if (previousAudio is { } old && (scene.Volume is not null || scene.Muted is not null))
            {
                if (audio.IsStale || audio.Value?.Level is not { } current || current.EndpointId != old.EndpointId) { log.Add("输出设备或状态已变化，跳过音频恢复"); }
                else
                {
                    if (scene.Volume is { } v && Math.Abs(current.Volume - v / 100f) < .011) await Restore("音量", () => coordinator.SetVolumeAsync(old.EndpointId, old.Volume));
                    else if (scene.Volume is not null) log.Add("音量已被另行调整，保留当前值");
                    if (scene.Muted is { } m && current.Muted == m) await Restore("静音", () => coordinator.SetMuteAsync(old.EndpointId, old.Muted));
                    else if (scene.Muted is not null) log.Add("静音已被另行调整，保留当前值");
                }
            }
            if (scene.PowerScheme is { } power && previousPower is { } original)
            {
                if (!coordinator.Power.Current.IsStale && coordinator.Power.Current.Value?.ActiveId == power) await Restore("电源", () => coordinator.SetPowerAsync(original));
                else log.Add("电源已变化，保留当前方案");
            }
            if (scene.AwakeMinutes is not null && appliedDeadline is not null)
            {
                if (awake.Current.Deadline == appliedDeadline || awake.Current.Status == AwakeStatus.Off)
                {
                    int remaining = previousAwake?.Deadline is { } end ? (int)Math.Ceiling((end - DateTimeOffset.UtcNow).TotalMinutes) : 0;
                    await Restore("唤醒", () => previousAwake?.Status == AwakeStatus.Active && remaining > 0
                        ? awake.StartAsync(Math.Clamp(remaining, 1, 480), previousAwake.KeepDisplay) : awake.StopAsync());
                }
                else log.Add("唤醒请求已变化，保留当前请求");
            }
            if (!failed) { applied = null; appliedDeadline = null; }
            return string.Join("\n", log);
        }
        finally { gate.Release(); }
    }
}
