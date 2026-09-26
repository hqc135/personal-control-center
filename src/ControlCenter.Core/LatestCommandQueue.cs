namespace ControlCenter.Core;
// One in flight plus one pending. Replaced requests are completed explicitly, never silently dropped.
public sealed class LatestCommandQueue<T>(Func<T, CancellationToken, Task<CommandResult>> execute, TimeSpan interval) : IAsyncDisposable
{
    private sealed record Work(T Value, TaskCompletionSource<CommandResult> Completion);
    private readonly object gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private Work? pending;
    private Task runner = Task.CompletedTask;
    private bool running, disposed;
    public Task<CommandResult> SubmitAsync(T value)
    {
        var next = new Work(value, new(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (gate)
        {
            if (disposed) return Task.FromResult(new CommandResult(CommandOutcome.Failed, FailureCode.Cancelled, "程序正在退出。"));
            pending?.Completion.TrySetResult(new(CommandOutcome.Superseded));
            pending = next;
            if (!running) { running = true; runner = Task.Run(RunAsync); }
        }
        return next.Completion.Task;
    }
    private async Task RunAsync()
    {
        while (true)
        {
            Work? work;
            lock (gate)
            {
                work = pending; pending = null;
                if (work is null) { running = false; return; }
            }
            try { work.Completion.TrySetResult(await execute(work.Value, lifetime.Token)); }
            catch (OperationCanceledException) { work.Completion.TrySetResult(new(CommandOutcome.Failed, FailureCode.Cancelled, "操作已取消。")); }
            catch (Exception) { work.Completion.TrySetResult(new(CommandOutcome.UnknownOutcome, FailureCode.NativeFailure, "操作结果待确认，请刷新。")); }
            try { if (interval > TimeSpan.Zero) await Task.Delay(interval, lifetime.Token); }
            catch (OperationCanceledException) { }
        }
    }
    public void CancelPending()
    {
        lock (gate) { pending?.Completion.TrySetResult(new(CommandOutcome.Failed, FailureCode.Cancelled, "待执行操作已取消。")); pending = null; }
    }
    public async ValueTask DisposeAsync()
    {
        Task finish;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; lifetime.Cancel(); CancelPending(); finish = runner;
        }
        await finish;
        lifetime.Dispose();
    }
}

