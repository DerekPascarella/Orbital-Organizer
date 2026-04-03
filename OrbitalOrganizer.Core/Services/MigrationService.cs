using System.Text.RegularExpressions;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Handles migration from legacy RMENU SD cards that weren't set up with Orbital Organizer.
/// Parses LIST.INI to extract game titles and virtual folder paths, then generates
/// sidecar metadata files.
/// </summary>
public static class MigrationService
{
    /// <summary>
    /// Parsed entry from a LIST.INI file.
    /// </summary>
    public class ListIniEntry
    {
        public string FolderNumber { get; set; } = "";
        public string Title { get; set; } = "";
        public string Disc { get; set; } = "1/1";
        public string Region { get; set; } = "";
        public string Version { get; set; } = "";
        public string Date { get; set; } = "";
        public string VirtualFolder { get; set; } = "";
        public List<string> AlternativeFolders { get; set; } = new();
    }

    /// <summary>
    /// Parses LIST.INI and returns a dictionary of folder numbers to their parsed entries.
    /// Skips folder 01 (the menu system itself).
    /// </summary>
    // Word-form disc numbers for matching disc identifiers like "Disc One"
    private static readonly Dictionary<string, int> WordToNumber = new(StringComparer.OrdinalIgnoreCase)
    {
        ["one"] = 1,
        ["two"] = 2,
        ["three"] = 3,
        ["four"] = 4,
        ["five"] = 5,
        ["six"] = 6,
        ["seven"] = 7,
        ["eight"] = 8,
        ["nine"] = 9,
        ["ten"] = 10
    };

    /// <summary>
    /// Checks if text is a disc identifier (e.g., "Disc 1", "Disk Two", "CD3").
    /// </summary>
    private static bool IsDiscIdentifier(string text)
    {
        text = text.Trim();
        // Numeric form: "Disc 1", "Disk 2", "CD3"
        if (Regex.IsMatch(text, @"^\s*(Disc|Disk|CD)\s*\d+\s*$", RegexOptions.IgnoreCase))
            return true;
        // Word form: "Disc One", "Disk Two"
        var wordMatch = Regex.Match(text, @"^\s*(Disc|Disk|CD)\s+(\w+)\s*$", RegexOptions.IgnoreCase);
        if (wordMatch.Success && WordToNumber.ContainsKey(wordMatch.Groups[2].Value))
            return true;
        return false;
    }

    public static Dictionary<string, ListIniEntry> ParseListIni(string listIniPath)
    {
        var entries = new Dictionary<string, ListIniEntry>();
        if (!File.Exists(listIniPath))
            return entries;

        var lines = File.ReadAllLines(listIniPath, System.Text.Encoding.UTF8);
        string? currentFolder = null;

        foreach (var line in lines)
        {
            // NN.title=Game Title
            var titleMatch = Regex.Match(line, @"^(\d{2,})\.title=(.+)");
            if (titleMatch.Success && titleMatch.Groups[1].Value != "01")
            {
                currentFolder = titleMatch.Groups[1].Value;
                string rawTitle = titleMatch.Groups[2].Value.Trim();

                // Parse the title/folder from this entry
                string parsedTitle = rawTitle;
                string parsedFolder = "";

                if (rawTitle.StartsWith('/'))
                {
                    var trimmed = rawTitle.TrimStart('/');
                    var parts = trimmed.Split('/').ToList();

                    if (parts.Count > 1 && IsDiscIdentifier(parts[^1]))
                        parts.RemoveAt(parts.Count - 1);

                    parsedTitle = parts.Count > 0 ? parts[^1] : trimmed;

                    if (parts.Count > 1)
                        parsedFolder = string.Join("/", parts.Take(parts.Count - 1));
                }

                // If this folder number already exists, this is an alt folder entry
                if (entries.TryGetValue(currentFolder, out var existing))
                {
                    if (!string.IsNullOrEmpty(parsedFolder) &&
                        parsedFolder != existing.VirtualFolder &&
                        !existing.AlternativeFolders.Contains(parsedFolder))
                    {
                        existing.AlternativeFolders.Add(parsedFolder);
                    }
                }
                else
                {
                    var entry = new ListIniEntry
                    {
                        FolderNumber = currentFolder,
                        Title = parsedTitle,
                        VirtualFolder = parsedFolder
                    };
                    entries[currentFolder] = entry;
                }

                continue;
            }

            // NN.disc=X/Y
            var discMatch = Regex.Match(line, @"^(\d{2,})\.disc=(.+)");
            if (discMatch.Success && discMatch.Groups[1].Value != "01")
            {
                string folder = discMatch.Groups[1].Value;
                string discValue = discMatch.Groups[2].Value.Trim();

                if (entries.TryGetValue(folder, out var entry) && entry.Disc == "1/1")
                {
                    entry.Disc = discValue;
                }
                continue;
            }

            // NN.region=... (only set on first encounter)
            var regionMatch = Regex.Match(line, @"^(\d{2,})\.region=(.+)");
            if (regionMatch.Success && entries.TryGetValue(regionMatch.Groups[1].Value, out var rEntry)
                && string.IsNullOrEmpty(rEntry.Region))
            {
                rEntry.Region = regionMatch.Groups[2].Value.Trim();
                continue;
            }

            // NN.version=... (only set on first encounter)
            var versionMatch = Regex.Match(line, @"^(\d{2,})\.version=(.+)");
            if (versionMatch.Success && entries.TryGetValue(versionMatch.Groups[1].Value, out var vEntry)
                && string.IsNullOrEmpty(vEntry.Version))
            {
                vEntry.Version = versionMatch.Groups[2].Value.Trim();
                continue;
            }

            // NN.date=... (only set on first encounter)
            var dateMatch = Regex.Match(line, @"^(\d{2,})\.date=(.+)");
            if (dateMatch.Success && entries.TryGetValue(dateMatch.Groups[1].Value, out var dEntry)
                && string.IsNullOrEmpty(dEntry.Date))
            {
                dEntry.Date = dateMatch.Groups[2].Value.Trim();
            }
        }

        return entries;
    }

    /// <summary>
    /// Generates sidecar metadata files for all game folders on the SD card
    /// based on data parsed from LIST.INI. Does not overwrite existing sidecar files.
    ///
    /// What gets written depends on the card origin:
    ///   NonOO:            Name.txt + Folder.txt/Folder_Alt*.txt only.
    ///                     Disc/Region/Version/Date come from IP.BIN during the metadata scan.
    ///   PerlOO / CSharpOO: All available LIST.INI fields (recovery for deleted sidecars).
    /// </summary>
    /// <returns>Number of folders processed.</returns>
    public static int GenerateSidecarFiles(string sdCardPath, Dictionary<string, ListIniEntry> entries,
        CardOrigin origin, IProgress<string>? progress = null)
    {
        int count = 0;
        bool writeAllFields = origin != CardOrigin.NonOO;

        foreach (var kvp in entries)
        {
            string folderPath = Path.Combine(sdCardPath, kvp.Key);
            if (!Directory.Exists(folderPath))
                continue;

            var entry = kvp.Value;
            progress?.Report($"Writing metadata for folder {kvp.Key}: {entry.Title}...");

            // Name.txt and Folder.txt are always written from LIST.INI,
            // regardless of card origin. Name.txt preserves the user's
            // customized RMENU title; Folder.txt preserves virtual folder paths.
            string namePath = Path.Combine(folderPath, Constants.NameFile);
            if (!File.Exists(namePath))
                MetadataManager.WriteTextFile(namePath, entry.Title);

            string folderTxtPath = Path.Combine(folderPath, Constants.FolderFile);
            if (!File.Exists(folderTxtPath) && !string.IsNullOrEmpty(entry.VirtualFolder))
                MetadataManager.WriteTextFile(folderTxtPath, entry.VirtualFolder);

            for (int i = 0; i < entry.AlternativeFolders.Count && i < Constants.FolderAltFiles.Length; i++)
            {
                string altFilePath = Path.Combine(folderPath, Constants.FolderAltFiles[i]);
                if (!File.Exists(altFilePath))
                    MetadataManager.WriteTextFile(altFilePath, entry.AlternativeFolders[i]);
            }

            // Disc/Region/Version/Date are only written from LIST.INI on
            // OO-managed cards (sidecar recovery). For non-OO cards, these
            // values come from IP.BIN during the metadata scan instead.
            if (writeAllFields)
            {
                string discPath = Path.Combine(folderPath, Constants.DiscFile);
                if (!File.Exists(discPath) && !string.IsNullOrEmpty(entry.Disc))
                    MetadataManager.WriteTextFile(discPath, entry.Disc);

                string regionPath = Path.Combine(folderPath, Constants.RegionFile);
                if (!File.Exists(regionPath) && !string.IsNullOrEmpty(entry.Region))
                    MetadataManager.WriteTextFile(regionPath, entry.Region);

                string versionPath = Path.Combine(folderPath, Constants.VersionFile);
                if (!File.Exists(versionPath) && !string.IsNullOrEmpty(entry.Version))
                {
                    // LIST.INI stores version with "V" prefix but Version.txt stores it without
                    string ver = entry.Version;
                    if (ver.Contains('V'))
                        ver = ver[(ver.LastIndexOf('V') + 1)..];
                    MetadataManager.WriteTextFile(versionPath, ver);
                }

                string datePath = Path.Combine(folderPath, Constants.DateFile);
                if (!File.Exists(datePath) && !string.IsNullOrEmpty(entry.Date))
                    MetadataManager.WriteTextFile(datePath, entry.Date);
            }

            count++;
        }

        return count;
    }

}
