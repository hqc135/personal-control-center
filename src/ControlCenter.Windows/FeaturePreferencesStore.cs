using System.Text.Json;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed class FeaturePreferencesStore(string directory) : IFeaturePreferencesStore
{
    private string? loadedHash;
    public FeaturePreferences Load(FeaturePreferences? defaults = null)
    {
        var path = Path.Combine(directory, "features.json");
        if (!File.Exists(path)) { loadedHash = null; return defaults ?? new(); }
        if (new FileInfo(path).Length > ConfigCodec.MaxBytes) throw new InvalidDataException("功能配置过大。");
        var bytes = File.ReadAllBytes(path);
        var value = JsonSerializer.Deserialize<FeaturePreferences>(bytes, ConfigCodec.Options) ?? throw new InvalidDataException("功能配置为空。");
        loadedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        value.Validate(); return value;
    }
    public void Save(FeaturePreferences value)
    {
        value.Validate(); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "features.json"); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        if (File.Exists(path))
        {
            using var current = File.OpenRead(path);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(current)) != loadedHash) throw new ConfigConflictException();
        }
        else if (loadedHash is not null) throw new ConfigConflictException();
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, ConfigCodec.Options);
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            loadedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
