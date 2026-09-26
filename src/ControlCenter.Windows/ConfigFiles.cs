using ControlCenter.Core;
namespace ControlCenter.Windows;
public static class ConfigFiles
{
    public static async Task<byte[]> ReadAsync(string path, CancellationToken ct = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (file.Length > ConfigCodec.MaxBytes) throw new InvalidDataException("配置超过 256 KiB。");
        byte[] bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, ct); return bytes;
    }
    public static async Task ExportAsync(string path, AppConfig config, CancellationToken ct = default)
    {
        string name = Path.GetFileName(path);
        if (name.Equals("config.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("trusted-shortcuts.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("state.json", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("config-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择便携导出文件名，不覆盖运行配置、信任记录或备份。");
        var data = ConfigCodec.Encode(ConfigurationTransfer.Portable(config));
        string target = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { await file.WriteAsync(data, ct); file.Flush(true); }
            ct.ThrowIfCancellationRequested();
            if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
