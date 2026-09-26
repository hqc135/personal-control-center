using System.Text.Json;
using ControlCenter.Core;
namespace ControlCenter.Windows.Launch;
// Separate machine-local ledger; importing config.json never imports trust.
public sealed class ShortcutTrustStore(string directory) : IShortcutTrustStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private HashSet<string> fingerprints = [];
    public async Task LoadAsync(CancellationToken ct = default)
    {
        string path = Path.Combine(directory, "trusted-shortcuts.json");
        if (!File.Exists(path)) return;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 65536) return;
            var values = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path, ct)) ?? [];
            fingerprints = values.Where(x => x is { Length: 64 } && x.All(Uri.IsHexDigit)).Take(256).ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { fingerprints = []; }
    }
    public bool IsTrusted(ShortcutDefinition shortcut) => Volatile.Read(ref fingerprints).Contains(ShortcutPolicy.Fingerprint(shortcut));
    public async Task ConfirmAsync(ShortcutDefinition shortcut, CancellationToken ct)
    {
        if (shortcut.Kind != "application") throw new InvalidDataException("只有程序入口需要此确认。");
        string fingerprint = ShortcutPolicy.Fingerprint(shortcut);
        await gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            var next = new HashSet<string>(fingerprints, StringComparer.Ordinal) { fingerprint };
            if (next.Count > 256) throw new InvalidDataException("本机确认记录已满。");
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, $"trust-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(next), ct);
            var target = Path.Combine(directory, "trusted-shortcuts.json");
            if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
            Volatile.Write(ref fingerprints, next);
        }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
}

