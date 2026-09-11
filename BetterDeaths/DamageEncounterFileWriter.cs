namespace BetterDeaths;

using System.IO;
using System.Text.Json;

internal static class DamageEncounterFileWriter
{
    public static void Write<T>(string path, T data, JsonSerializerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            JsonSerializer.Serialize(stream, data, options);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
