using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed class ExecutionStateBackend : IAwakeBackend
{
    public bool Apply(bool active, bool keepDisplay)
        => SetThreadExecutionState(0x80000000u | (active ? 1u : 0u) | (active && keepDisplay ? 2u : 0u)) != 0;
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
}
public sealed class AwakeService : IAwakeService
{
    private readonly BlockingCollection<Action> queue = new();
    private readonly AwakeSession session;
    private readonly Thread worker;
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AwakeSnapshot current = new(AwakeStatus.Off);
    private int disposed;
    public AwakeSnapshot Current => Volatile.Read(ref current);
    public event Action? Changed;
    public AwakeService(IAwakeBackend backend, TimeProvider? clock = null)
    {
        session = new(backend, clock ?? TimeProvider.System);
        worker = new Thread(Run) { IsBackground = true, Name = "PCC.Awake", Priority = ThreadPriority.BelowNormal };
        worker.Start();
    }
    private void Publish()
    {
        var previous = Current;
        Volatile.Write(ref current, session.Current);
        if (previous != current && Changed is { } handlers)
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception) { /* An observer must not terminate the request-owning thread. */ }
    }
    private void Run()
    {
        Exception? failure = null;
        try
        {
            while (!queue.IsCompleted)
            {
                if (queue.TryTake(out var action, session.Current.Status == AwakeStatus.Active ? 1000 : Timeout.Infinite)) action();
                session.Check(false); Publish();
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            try { session.Stop(); Publish(); }
            catch (Exception ex) { failure ??= ex; }
            finally
            {
                if (failure is null) stopped.TrySetResult(); else stopped.TrySetException(failure);
            }
        }
    }
    private Task<T> Invoke<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref disposed) != 0) return Task.FromException<T>(new ObjectDisposedException(nameof(AwakeService)));
        try { queue.Add(() => { try { var result = action(); Publish(); completion.SetResult(result); } catch (Exception ex) { completion.SetException(ex); } }); }
        catch (InvalidOperationException) { completion.TrySetException(new ObjectDisposedException(nameof(AwakeService))); }
        return completion.Task;
    }
    public Task<CommandResult> StartAsync(int minutes, bool keepDisplay) => Invoke(() => session.Start(minutes, keepDisplay));
    public Task<CommandResult> StopAsync() => Invoke(session.Stop);
    public Task CheckAsync(bool resumed = false) => Invoke(() => { session.Check(resumed); return true; });
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) queue.CompleteAdding();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (!worker.Join(TimeSpan.FromSeconds(1))) throw new TimeoutException("唤醒线程未及时结束。");
        if (Current.Status == AwakeStatus.ReleaseUnconfirmed)
            throw new ServiceException(FailureCode.NativeFailure, "退出时释放请求未确认；线程已结束。");
    }
}
