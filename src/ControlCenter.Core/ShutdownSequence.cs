namespace ControlCenter.Core;
public enum ShutdownModule { Awake, Coordinator, Audio, Power }
public enum ShutdownResult { Completed, Failed, TimedOut }
public sealed record ShutdownEntry(Guid CorrelationId, ShutdownModule Module, ShutdownResult Result, TimeSpan Elapsed, int? HResult = null);
public static class ShutdownSequence
{
    public static Task RunAsync(IEnumerable<(ShutdownModule Module, Func<Task> Stop)> steps,
        Action<ShutdownModule, ShutdownResult> report, TimeSpan? timeout = null)
        => RunAsync(steps, entry => report(entry.Module, entry.Result), timeout);
    public static async Task RunAsync(IEnumerable<(ShutdownModule Module, Func<Task> Stop)> steps,
        Action<ShutdownEntry> report, TimeSpan? timeout = null)
    {
        Guid correlation = Guid.NewGuid();
        foreach (var (module, stop) in steps)
        {
            var result = ShutdownResult.Completed;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int? hresult = null;
            try { await stop().WaitAsync(timeout ?? TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { result = ShutdownResult.TimedOut; }
            catch (Exception ex) { result = ShutdownResult.Failed; hresult = ex.HResult; }
            // Diagnostics must never stop the remaining cleanup.
            try { report(new(correlation, module, result, System.Diagnostics.Stopwatch.GetElapsedTime(started), hresult)); }
            catch (Exception) { }
        }
    }
}
