using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed class JsonConfigRepository(string directory) : IConfigRepository, IConfigRecovery
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string ConfigPath => Path.Combine(directory, "config.json");
    private bool readOnly;
    public Task<IReadOnlyList<ConfigBackup>> ListBackupsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string path = Path.Combine(directory, "backups");
        IReadOnlyList<ConfigBackup> result = Directory.Exists(path)
            ? Directory.GetFiles(path, "config-*.json").OrderDescending().Take(5).Select(p => new ConfigBackup(Path.GetFileName(p), File.GetLastWriteTimeUtc(p))).ToArray() : [];
        return Task.FromResult(result);
    }
    public async Task<AppConfig> ReadBackupAsync(string id, CancellationToken ct = default)
    {
        if (Path.GetFileName(id) != id || !System.Text.RegularExpressions.Regex.IsMatch(id, @"^config-[0-9]+\.json$"))
            throw new InvalidDataException("备份编号无效。");
        return ConfigCodec.Decode(await ReadBoundedAsync(Path.Combine(directory, "backups", id), ct));
    }
    public async Task<ConfigLoadResult> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath)) return new(new());
        try { return new(ConfigCodec.Decode(await ReadBoundedAsync(ConfigPath, ct))); }
        catch (FutureConfigException) { readOnly = true; return new(new(), "较新版本配置：使用安全默认值，原文件只读保护。", true); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            // Preserve the broken original. Recovery is explicit on the next settings save.
            foreach (var backup in Directory.Exists(Path.Combine(directory, "backups"))
                ? Directory.GetFiles(Path.Combine(directory, "backups"), "*.json").OrderDescending() : Enumerable.Empty<string>())
            {
                try { return new(ConfigCodec.Decode(await ReadBoundedAsync(backup, ct)), "配置损坏，已从备份读取；原文件保留。"); }
                catch (Exception error) when (error is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
            }
            return new(new(), "配置无法读取，使用默认值；原文件保留。");
        }
    }
    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > ConfigCodec.MaxBytes) throw new InvalidDataException("配置过大。");
        var data = new byte[(int)file.Length];
        await file.ReadExactlyAsync(data, ct);
        return data;
    }
    public async Task SaveAsync(AppConfig config, CancellationToken ct = default)
    {
        var bytes = ConfigCodec.Encode(config);
        await gate.WaitAsync(ct);
        string? temp = null;
        try
        {
            if (readOnly) throw new FutureConfigException();
            if (File.Exists(ConfigPath))
            {
                try { ConfigCodec.Decode(await ReadBoundedAsync(ConfigPath, ct)); }
                catch (FutureConfigException) { readOnly = true; throw; }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
            }
            Directory.CreateDirectory(directory);
            temp = Path.Combine(directory, $".config-{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes, ct); stream.Flush(true); }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(ConfigPath))
            {
                var backups = Path.Combine(directory, "backups");
                Directory.CreateDirectory(backups);
                File.Replace(temp, ConfigPath, Path.Combine(backups, $"config-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.json"));
                foreach (var old in Directory.GetFiles(backups, "*.json").OrderDescending().Skip(5)) File.Delete(old);
            }
            else File.Move(temp, ConfigPath);
        }
        finally { if (temp is not null && File.Exists(temp)) File.Delete(temp); gate.Release(); }
    }
}

