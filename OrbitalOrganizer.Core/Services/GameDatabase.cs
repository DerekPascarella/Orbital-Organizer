using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Loads and saves the GameDB.json metadata database at the SD card root.
/// </summary>
public class GameDatabase
{
    public int Version { get; set; } = 1;
    public Dictionary<string, GameDbEntry> Items { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static string GetPath(string sdCardPath) =>
        Path.Combine(sdCardPath, Constants.GameDatabaseFile);

    // A bad database must never block loading. Whole-file damage returns
    // null and per-entry damage drops only that entry, so affected folders
    // fall back to the sidecar reader and the file is rewritten on save.
    public static async Task<GameDatabase?> LoadAsync(string sdCardPath)
    {
        string path = GetPath(sdCardPath);
        try
        {
            if (!File.Exists(path))
                return null;

            string json = await File.ReadAllTextAsync(path);
            var db = new GameDatabase();
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number)
                db.Version = v.GetInt32();

            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
                return null;

            foreach (var prop in items.EnumerateObject())
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<GameDbEntry>(prop.Value.GetRawText(), SerializerOptions);
                    if (entry == null || !entry.IsUsable)
                        continue;

                    if (entry.AltFolders != null)
                        entry.AltFolders = entry.AltFolders
                            .Where(f => !string.IsNullOrWhiteSpace(f))
                            .ToList();

                    db.Items[prop.Name] = entry;
                }
                catch (JsonException) { }
            }

            return db;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string sdCardPath)
    {
        string path = GetPath(sdCardPath);
        string json = JsonSerializer.Serialize(this, SerializerOptions);
        string tmp = path + ".tmp";

        await Task.Run(() =>
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }
            File.Move(tmp, path, overwrite: true);
        });
    }
}
