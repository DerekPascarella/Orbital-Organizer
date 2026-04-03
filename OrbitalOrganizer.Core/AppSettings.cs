using System.Runtime.InteropServices;
using System.Text.Json;

namespace OrbitalOrganizer.Core;

/// <summary>
/// Persisted application settings, saved as JSON.
/// On macOS, stored in ~/Library/Application Support/OrbitalOrganizer/ since
/// the .app bundle filesystem is read-only. On Windows/Linux, alongside the executable.
/// </summary>
public class AppSettings
{
    public bool EnableLockCheck { get; set; } = true;
    public string TempFolder { get; set; } = "";
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 960;
    public double WindowHeight { get; set; } = 800;
    public string SkippedUpdateVersion { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string GetSettingsDir()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "OrbitalOrganizer");
        }

        return AppDomain.CurrentDomain.BaseDirectory;
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(GetSettingsDir(), "settings.json");
    }

    public static AppSettings Load()
    {
        string path = GetSettingsPath();
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            string dir = GetSettingsDir();
            Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(GetSettingsPath(), json);
        }
        catch
        {
            // Settings file might be read-only or inaccessible; silently ignore
        }
    }

    /// <summary>
    /// Checks if the settings file exists and is marked read-only.
    /// Returns the file path if read-only, or null if writable/missing.
    /// </summary>
    public static string? CheckReadOnly()
    {
        string path = GetSettingsPath();
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (info.IsReadOnly)
                return path;
        }
        return null;
    }
}
