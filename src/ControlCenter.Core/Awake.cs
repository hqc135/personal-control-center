namespace ControlCenter.Core;
public enum AwakeStatus { Off, Active, ReleaseUnconfirmed }
public sealed record AwakeSnapshot(AwakeStatus Status, DateTimeOffset? Deadline = null, bool KeepDisplay = false, string? Message = null, TimeSpan Remaining = default);
public interface IAwakeBackend { bool Apply(bool active, bool keepDisplay); }
public interface IAwakeService : IAsyncDisposable
{
    AwakeSnapshot Current { get; }
    event Action? Changed;
    Task<CommandResult> StartAsync(int minutes, bool keepDisplay);
    Task<CommandResult> StopAsync();
    Task CheckAsync(bool resumed = false);
}
// Owned exclusively by the service thread. Neither active state nor deadlines are persisted.
public sealed class AwakeSession(IAwakeBackend backend, TimeProvider clock)
{
    public AwakeSnapshot Current { get; private set; } = new(AwakeStatus.Off);
    private long started;
    private TimeSpan budget;
    public CommandResult Start(int minutes, bool display)
    {
        if (minutes is < 1 or > 480) return new(CommandOutcome.Failed, FailureCode.InvalidConfiguration, "时长须为 1–480 分钟。");
        if (Current.Status == AwakeStatus.ReleaseUnconfirmed)
            return new(CommandOutcome.UnknownOutcome, FailureCode.NativeFailure, "请先重试结束未确认的请求。");
        if (!backend.Apply(true, display))
            return new(CommandOutcome.Failed, FailureCode.NativeFailure, "保持唤醒申请失败；原状态保留。");
        started = clock.GetTimestamp(); budget = TimeSpan.FromMinutes(minutes);
        Current = new(AwakeStatus.Active, clock.GetUtcNow() + budget, display, "仅保持本应用的临时请求。", budget);
        return CommandResult.Confirmed;
    }
    public CommandResult Stop()
    {
        if (Current.Status == AwakeStatus.Off) return CommandResult.Confirmed;
        if (!backend.Apply(false, false))
        {
            Current = Current with { Status = AwakeStatus.ReleaseUnconfirmed, Message = "释放未确认，请重试结束或退出程序。" };
            return new(CommandOutcome.UnknownOutcome, FailureCode.NativeFailure, Current.Message);
        }
        Current = new(AwakeStatus.Off, Message: "已结束本应用的保持唤醒。");
        return new(CommandOutcome.Confirmed, Message: Current.Message);
    }
    public void Check(bool resumed)
    {
        if (Current.Status != AwakeStatus.Active) return;
        if (clock.GetUtcNow() >= Current.Deadline || clock.GetElapsedTime(started) >= budget) { Stop(); return; }
        var utcRemaining = Current.Deadline!.Value - clock.GetUtcNow();
        var elapsedRemaining = budget - clock.GetElapsedTime(started);
        Current = Current with { Remaining = utcRemaining < elapsedRemaining ? utcRemaining : elapsedRemaining };
        if (resumed && !backend.Apply(true, Current.KeepDisplay))
        {
            // A failed reassert does not prove the preceding request was cleared.
            Current = Current with { Status = AwakeStatus.ReleaseUnconfirmed, Message = "恢复后的请求未确认，请结束后重试。" };
        }
    }
}
