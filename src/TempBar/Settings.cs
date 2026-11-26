using System.Text.Json;

namespace TempBar;

public sealed class Settings
{
    public bool ShowOverlay { get; set; } = true;
    public bool Fahrenheit { get; set; }
    public int IntervalMs { get; set; } = 2000;
    public int WarnCelsius { get; set; } = 85;

    // Distance from the taskbar's right edge to the overlay's right edge, in physical pixels.
    // Null means "sit just left of the notification area".
    public int? OffsetFromRight { get; set; }

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TempBar");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }
}
