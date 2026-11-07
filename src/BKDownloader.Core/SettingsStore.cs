using System.Text.Json;

namespace BKDownloader.Core;

public static class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BKTools", "BKDayzModDownloader");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static AppSettings Load(string? path = null)
    {
        var file = path ?? SettingsPath;
        return File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? new() : new();
    }
    public static void Save(AppSettings settings, string? path = null)
    {
        var file = path ?? SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, file, overwrite: true);
    }
}
