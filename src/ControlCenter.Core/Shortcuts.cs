using System.Security.Cryptography;
using System.Text;
namespace ControlCenter.Core;
public sealed record ShortcutDefinition(string Id, string Label, string Kind, string Target, string[]? Arguments = null, string? WorkingDirectory = null);
public static class ShortcutPolicy
{
    public static void Validate(ShortcutDefinition shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut.Id) || shortcut.Id.Length > 80 || string.IsNullOrWhiteSpace(shortcut.Label) || shortcut.Label.Length > 80)
            throw new InvalidDataException("入口名称或编号无效。");
        if (string.IsNullOrWhiteSpace(shortcut.Target) || shortcut.Target.Length > 4096 || shortcut.Target.Contains('\0'))
            throw new InvalidDataException("入口目标无效。");
        if (shortcut.Kind == "knownFolder")
        {
            if (shortcut.Target != "Downloads") throw new InvalidDataException("不支持的系统目录。");
        }
        else if (shortcut.Kind == "url")
        {
            if (!Uri.TryCreate(shortcut.Target, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidDataException("网页仅支持不含凭据的 HTTP/HTTPS 地址。");
        }
        else if (shortcut.Kind is "folder" or "application")
        {
            // Avoid network paths (including extended UNC/device paths) and implicit relative shell resolution.
            if (shortcut.Target.Length < 3 || !char.IsAsciiLetter(shortcut.Target[0]) || shortcut.Target[1] != ':' || shortcut.Target[2] != '\\'
                || shortcut.Target[3..].Contains(':'))
                throw new InvalidDataException("请选择本机绝对路径。");
            if (shortcut.Target.IndexOfAny(['"', '<', '>', '|', '*', '?', '\r', '\n']) >= 0)
                throw new InvalidDataException("路径包含无效字符。");
            if (shortcut.Kind == "application" && !shortcut.Target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("程序入口只接受 .exe 文件。");
        }
        else throw new InvalidDataException("不支持的入口类型。");
        if (shortcut.Arguments is { } args && (args.Length > 32 || args.Any(x => x is null || x.Length > 4096 || x.Contains('\0'))))
            throw new InvalidDataException("程序参数无效。");
        if (shortcut.Kind != "application" && ((shortcut.Arguments?.Length ?? 0) > 0 || shortcut.WorkingDirectory is not null))
            throw new InvalidDataException("只有程序入口可以设置参数和工作目录。");
        if (shortcut.WorkingDirectory is { } cwd)
            Validate(new("working-directory", "工作目录", "folder", cwd));
    }
    public static string Fingerprint(ShortcutDefinition shortcut)
    {
        Validate(shortcut);
        var canonical = System.Text.Json.JsonSerializer.Serialize(new { shortcut.Kind, shortcut.Target, Arguments = shortcut.Arguments ?? [], shortcut.WorkingDirectory });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
public interface IShortcutTrustStore
{
    bool IsTrusted(ShortcutDefinition shortcut);
    Task ConfirmAsync(ShortcutDefinition shortcut, CancellationToken ct);
}
public sealed class ShortcutCoordinator(IShortcutLauncher launcher)
{
    private readonly HashSet<string> pending = [];
    private readonly object gate = new();
    public async Task<CommandResult> LaunchAsync(ShortcutDefinition definition, CancellationToken ct = default)
    {
        lock (gate) { if (!pending.Add(definition.Id)) return new(CommandOutcome.Superseded); }
        try { return await launcher.LaunchAsync(definition, ct); }
        finally { lock (gate) pending.Remove(definition.Id); }
    }
}

