using System.Collections.Immutable;
namespace ControlCenter.Core;

public enum FailureCode { None, Unavailable, DeviceGone, PermissionDenied, Timeout, VerificationFailed, InvalidConfiguration, Cancelled, NativeFailure, Untrusted }
public enum CommandOutcome { Idle, Pending, Confirmed, Failed, UnknownOutcome, Superseded }
public sealed record CommandResult(CommandOutcome Outcome, FailureCode Code = FailureCode.None, string? Message = null)
{
    public static CommandResult Confirmed => new(CommandOutcome.Confirmed);
}
public sealed class ServiceException(FailureCode code, string message, int? nativeCode = null) : Exception(message)
{
    public FailureCode Code { get; } = code;
    public int? NativeCode { get; } = nativeCode;
}
public sealed record AudioDevice(string Id, string Name);
public sealed record AudioLevel(string EndpointId, float Volume, bool Muted);
public sealed record AudioSnapshot(ImmutableArray<AudioDevice> Devices, string? DefaultEndpointId, string? CommunicationsEndpointId, AudioLevel? Level, string? ConsoleEndpointId = null);
public sealed record PowerScheme(Guid Id, string Name);
public sealed record PowerSnapshot(ImmutableArray<PowerScheme> Schemes, Guid ActiveId, bool? OnAcPower);
public interface IAudioService : IAsyncDisposable
{
    event Action? Invalidated;
    Task<AudioSnapshot> ReadAsync(CancellationToken ct);
    Task<AudioLevel> ReadLevelAsync(string endpointId, CancellationToken ct);
    Task SetVolumeAsync(string endpointId, float scalar, CancellationToken ct);
    Task SetMuteAsync(string endpointId, bool muted, CancellationToken ct);
}
public interface IPowerService : IAsyncDisposable
{
    event Action? Invalidated;
    Task<PowerSnapshot> ReadAsync(CancellationToken ct);
    Task ActivateAsync(Guid schemeId, CancellationToken ct);
}
public interface IShortcutLauncher
{
    Task<CommandResult> LaunchAsync(ShortcutDefinition shortcut, CancellationToken ct);
    Task<CommandResult> OpenSoundSettingsAsync(CancellationToken ct);
}
public sealed record ModuleState<T>(T? Value, bool IsRefreshing = false, bool IsStale = true,
    DateTimeOffset? ObservedAt = null, string? Error = null, CommandOutcome Operation = CommandOutcome.Idle) where T : class;
public sealed class ModuleStore<T> where T : class
{
    private readonly object gate = new();
    private long epoch, query;
    private ModuleState<T> state = new(null);
    public event Action? Changed;
    public ModuleState<T> Current { get { lock (gate) return state; } }
    public (long Epoch, long Query) BeginRead()
    {
        (long, long) token;
        lock (gate) { token = (epoch, ++query); state = state with { IsRefreshing = true }; }
        Changed?.Invoke(); return token;
    }
    public void CompleteRead((long Epoch, long Query) token, T value)
    {
        lock (gate)
        {
            if (token != (epoch, query)) return;
            state = state with { Value = value, IsRefreshing = false, IsStale = false, ObservedAt = DateTimeOffset.UtcNow, Error = null };
        }
        Changed?.Invoke();
    }
    public void FailRead((long Epoch, long Query) token, string error)
    {
        lock (gate)
        {
            if (token != (epoch, query)) return;
            state = state with { IsRefreshing = false, IsStale = true, Error = error };
        }
        Changed?.Invoke();
    }
    public void Invalidate()
    {
        lock (gate) { epoch++; state = state with { IsRefreshing = false, IsStale = true }; }
        Changed?.Invoke();
    }
    public void BeginWrite()
    {
        lock (gate) { epoch++; state = state with { IsRefreshing = false, IsStale = true, Operation = CommandOutcome.Pending, Error = null }; }
        Changed?.Invoke();
    }
    public void CompleteWrite(CommandResult result)
    {
        lock (gate) { state = state with { Operation = result.Outcome, Error = result.Message ?? state.Error }; }
        Changed?.Invoke();
    }
}

