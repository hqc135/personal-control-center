using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ControlCenter.Core;
namespace ControlCenter.Windows;
public sealed record UpdateRelease(string Version, string Page, string AssetApi, string Sha256, long Size);
// Explicit, manual operation only. Stages versions side by side; never overwrites or executes running binaries.
public sealed class UpdatePackages(string directory)
{
    private static HttpClient Client(string? token)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PersonalControlCenter/0.7.0");
        if (!string.IsNullOrWhiteSpace(token)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return client;
    }
    public async Task<UpdateRelease?> CheckAsync(string? token, CancellationToken ct)
    {
        using var client = Client(token);
        using var response = await client.GetAsync("https://api.github.com/repos/hqc135/personal-control-center/releases?per_page=20", ct);
        if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new IOException("私有仓库需要有访问权的 GitHub token，也可以在浏览器下载 ZIP 后手动校验。token 不保存到磁盘。");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var candidates = new List<UpdateRelease>();
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v'), out _)) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (!(asset.GetProperty("name").GetString()?.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                if (digest is null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || !ValidHash(digest[7..])) continue;
                candidates.Add(new(tag, release.GetProperty("html_url").GetString()!, asset.GetProperty("url").GetString()!, digest[7..], asset.GetProperty("size").GetInt64()));
            }
        }
        return candidates.OrderByDescending(x => Version.Parse(x.Version.TrimStart('v'))).FirstOrDefault();
    }
    public async Task<string> DownloadAsync(UpdateRelease release, string? token, CancellationToken ct)
    {
        if (!Uri.TryCreate(release.AssetApi, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "api.github.com"
            || !uri.AbsolutePath.StartsWith("/repos/hqc135/personal-control-center/releases/assets/", StringComparison.Ordinal) || release.Size is <= 0 or > 500_000_000)
            throw new InvalidDataException("更新附件地址或大小无效。");
        Directory.CreateDirectory(directory); var temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using var client = Client(token); client.DefaultRequestHeaders.Accept.Add(new("application/octet-stream"));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct); response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[81920]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count; if (total > release.Size) throw new InvalidDataException("下载大小超过声明。");
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (total != release.Size) throw new InvalidDataException("下载未完整。");
            }
            return await StageAsync(temp, release.Sha256, ct);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static bool ValidHash(string hash) => hash.Length == 64 && hash.All(Uri.IsHexDigit);
    public async Task<string> StageAsync(string zip, string expectedHash, CancellationToken ct)
    {
        if (!ValidHash(expectedHash)) throw new InvalidDataException("请输入发布者提供的 64 位 SHA256。");
        // Hold the file read-only through hashing and extraction to prevent replacement between verification and use.
        await using var stream = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 500_000_000) throw new InvalidDataException("安装包超过大小限制。");
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA256 不匹配，未解压。");
        stream.Position = 0; using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        if (archive.Entries.Count > 10000 || archive.Entries.Sum(x => x.Length) > 1_500_000_000) throw new InvalidDataException("压缩包内容超过限制。");
        var receipt = archive.GetEntry("RELEASE.json") ?? throw new InvalidDataException("缺少 RELEASE.json。");
        if (receipt.Length > 2_000_000) throw new InvalidDataException("版本清单过大。");
        using var doc = JsonDocument.Parse(receipt.Open());
        var version = doc.RootElement.GetProperty("Version").GetString();
        if (!Version.TryParse(version, out _)) throw new InvalidDataException("版本号无效。");
        if (archive.GetEntry("PersonalControlCenter.exe") is null || archive.GetEntry("coreclr.dll") is null) throw new InvalidDataException("需要完整的 self-contained 安装包。");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, version + "-" + actual[..12] + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(target);
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                var normalized = entry.FullName.Replace('\\', '/');
                if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Split('/').Any(x => x is ".." or "." || x.EndsWith(' ') || x.EndsWith('.')) || !seen.Add(normalized))
                    throw new InvalidDataException("压缩包路径不安全或重复。");
                var destination = Path.GetFullPath(Path.Combine(target, normalized));
                if (!destination.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("压缩包路径越界。");
                if (normalized.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open(); await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                await source.CopyToAsync(output, ct);
            }
            await File.WriteAllTextAsync(Path.Combine(target, "VERIFIED-SHA256.txt"), actual, ct);
            return target;
        }
        catch { if (Directory.Exists(target)) Directory.Delete(target, true); throw; }
    }
    public string[] Versions() => Directory.Exists(directory) ? Directory.GetDirectories(directory).Where(x => File.Exists(Path.Combine(x, "VERIFIED-SHA256.txt")) && File.Exists(Path.Combine(x, "PersonalControlCenter.exe"))).OrderDescending().ToArray() : [];
}
