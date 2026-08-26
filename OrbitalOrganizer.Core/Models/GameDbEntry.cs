using System.Text.Json.Serialization;

namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// One game's metadata in GameDB.json, keyed by its numbered folder name.
/// </summary>
public class GameDbEntry
{
    public string? Name { get; set; }
    public string? ProductId { get; set; }
    public string? Disc { get; set; }
    public string? Region { get; set; }
    public string? Version { get; set; }
    public string? Date { get; set; }
    public string? Folder { get; set; }
    public List<string>? AltFolders { get; set; }

    /// <summary>
    /// Identity gate. An entry without a usable name counts as no entry.
    /// </summary>
    [JsonIgnore]
    public bool IsUsable => !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// Cache gate, the database equivalent of having all sidecar files.
    /// Empty strings count as known values (ProductId may be empty).
    /// </summary>
    [JsonIgnore]
    public bool HasCacheData =>
        Name != null && ProductId != null && Disc != null &&
        Region != null && Version != null && Date != null;
}
