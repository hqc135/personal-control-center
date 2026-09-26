using System.Text.Json;
using ControlCenter.Windows;
var result = ReadOnlyProbe.Run();
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
return result.AudioHResult < 0 || result.PowerError != 0 ? 1 : 0;

