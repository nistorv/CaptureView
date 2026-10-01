using System.Diagnostics;
using System.Text.Json;

namespace CaptureView.Services;

public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CaptureView", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string? VideoDeviceId { get; set; }
    public VideoFormat? VideoFormat { get; set; }
    public string? AudioInputId { get; set; }
    public string? AudioOutputId { get; set; }
    public double Volume { get; set; } = 1.0;
    public bool Muted { get; set; }
    public bool KeepAspectRatio { get; set; } = true;
    public bool AlwaysOnTop { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load settings: {ex}");
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save settings: {ex}");
        }
    }
}
