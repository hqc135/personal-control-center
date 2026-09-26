using ControlCenter.Core;
namespace ControlCenter.Windows;
// Fixed fields only: no exception messages, paths, URLs, endpoints or user input.
public sealed class ShutdownLog(string directory)
{
    private readonly object gate = new();
    public void Write(ShutdownModule module, ShutdownResult result)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "lifecycle.log");
                if (File.Exists(path) && new FileInfo(path).Length >= 2 * 1024 * 1024)
                {
                    for (int i = 4; i >= 1; i--)
                    {
                        string source = i == 1 ? path : path + "." + (i - 1);
                        if (File.Exists(source)) File.Move(source, path + "." + i, true);
                    }
                }
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} shutdown {module} {result}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
