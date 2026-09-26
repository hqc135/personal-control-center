namespace ControlCenter.Core;
public enum ShutdownModule { Awake, Coordinator, Audio, Power }
public enum ShutdownResult { Completed, Failed, TimedOut }
public static class ShutdownSequence
{
    public static async Task RunAsync(IEnumerable<(ShutdownModule Module, Func<Task> Stop)> steps,
        Action<ShutdownModule, ShutdownResult> report, TimeSpan? timeout = null)
    {
        foreach (var (module, stop) in steps)
        {
            var result = ShutdownResult.Completed;
            try { await stop().WaitAsync(timeout ?? TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { result = ShutdownResult.TimedOut; }
            catch (Exception) { result = ShutdownResult.Failed; }
            report(module, result);
        }
    }
}
