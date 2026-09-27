using System.Security.Cryptography;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed class JsonConfigRepository(string directory) : IConfigRepository, IConfigRecovery, IConfigSaveStatus
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string ConfigPath => Path.Combine(directory, "config.json");
    private bool readOnly, observed, futureProtected;
    private string? revision;
    public string? LastSaveWarning { get; private set; }
    private static string? Revision(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));
    private static bool Recoverable(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException;
    public Task<IReadOnlyList<ConfigBackup>> ListBackupsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string path = Path.Combine(directory, "backups");
        IReadOnlyList<ConfigBackup> result = Directory.Exists(path)
            ? Directory.GetFiles(path, "config-*.json").Where(p => IsBackupId(Path.GetFileName(p))).OrderDescending().Take(5)
                .Select(p => new ConfigBackup(Path.GetFileName(p), File.GetLastWriteTimeUtc(p))).ToArray() : [];
        return Task.FromResult(result);
    }
    private static bool IsBackupId(string id) => System.Text.RegularExpressions.Regex.IsMatch(id, @"^config-[0-9]+\.json$");
    public async Task<AppConfig> ReadBackupAsync(string id, CancellationToken ct = default)
    {
        if (Path.GetFileName(id) != id || !IsBackupId(id)) throw new InvalidDataException("备份编号无效。");
        return ConfigCodec.Decode(await ConfigFiles.ReadAsync(Path.Combine(directory, "backups", id), ct));
    }
    private async Task<byte[]?> ReadCurrentAsync(CancellationToken ct)
    {
        try { return await ConfigFiles.ReadAsync(ConfigPath, ct); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public async Task<ConfigLoadResult> LoadAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            readOnly = false; observed = false; futureProtected = false;
            try
            {
                var bytes = await ReadCurrentAsync(ct);
                revision = Revision(bytes); observed = true;
                return bytes is null ? new(new()) : new(ConfigCodec.Decode(bytes));
            }
            catch (FutureConfigException) { readOnly = true; futureProtected = true; return new(new(), "较新版本配置：使用安全默认值，原文件只读保护。", true); }
            catch (Exception ex) when (Recoverable(ex))
            {
                // If the original could not be read completely, it cannot be safely replaced.
                if (!observed) { readOnly = true; return new(new(), "配置不可完整读取，使用默认值并保持只读；请修复权限或文件大小后重新载入。", true); }
                IReadOnlyList<ConfigBackup> backups;
                try { backups = await ListBackupsAsync(ct); }
                catch (Exception error) when (Recoverable(error)) { backups = []; }
                foreach (var backup in backups)
                {
                    try { return new(await ReadBackupAsync(backup.Id, ct), "配置损坏，已从备份读取；原文件保留。"); }
                    catch (Exception error) when (Recoverable(error)) { }
                }
                return new(new(), "配置无法解析，使用默认值；原文件保留。");
            }
        }
        finally { gate.Release(); }
    }
    private static void CheckVersion(byte[]? bytes)
    {
        if (bytes is null) return;
        try { ConfigCodec.Decode(bytes); }
        catch (FutureConfigException) { throw; }
        catch (Exception ex) when (Recoverable(ex)) { } // Explicit repair of a readable invalid original remains allowed.
    }
    private async Task<FileStream> LockWriterAsync(CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(directory, ".config-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException)
            {
                try { await Task.Delay(40, deadline.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("另一项配置保存尚未结束，请稍后重试。"); }
            }
        }
    }
    public async Task SaveAsync(AppConfig config, CancellationToken ct = default)
    {
        var bytes = ConfigCodec.Encode(config);
        await gate.WaitAsync(ct);
        string? temp = null;
        LastSaveWarning = null;
        try
        {
            if (futureProtected) throw new FutureConfigException();
            if (readOnly) throw new IOException("当前配置为只读；请重新载入并确认提示后再保存。");
            Directory.CreateDirectory(directory);
            await using var writer = await LockWriterAsync(ct);
            var before = await ReadCurrentAsync(ct);
            CheckVersion(before);
            if (observed && Revision(before) != revision) throw new ConfigConflictException();
            temp = Path.Combine(directory, $".config-{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { await stream.WriteAsync(bytes, ct); stream.Flush(true); }
            ct.ThrowIfCancellationRequested();
            var current = await ReadCurrentAsync(ct);
            CheckVersion(current);
            if (Revision(current) != Revision(before)) throw new ConfigConflictException();
            if (current is not null)
            {
                var backups = Path.Combine(directory, "backups");
                Directory.CreateDirectory(backups);
                File.Replace(temp, ConfigPath, Path.Combine(backups, $"config-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}.json"));
            }
            else File.Move(temp, ConfigPath);
            revision = Revision(bytes); observed = true;
            // The commit already succeeded. Pruning failure is a warning, never a false "save failed".
            try
            {
                string backups = Path.Combine(directory, "backups");
                if (Directory.Exists(backups))
                    foreach (var old in Directory.GetFiles(backups, "config-*.json").Where(p => IsBackupId(Path.GetFileName(p))).OrderDescending().Skip(5)) File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LastSaveWarning = "配置已保存，但旧备份暂未清理；稍后保存时会重试。"; }
        }
        finally
        {
            try
            {
                if (temp is not null && File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { LastSaveWarning ??= "配置临时文件未清理；不会将临时文件作为当前配置读取。"; }
            finally { gate.Release(); }
        }
    }
}

