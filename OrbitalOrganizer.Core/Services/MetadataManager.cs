using System.Text.RegularExpressions;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Reads and writes sidecar metadata text files (Name.txt, Disc.txt, etc.)
/// for game folders on the SD card.
/// </summary>
public static class MetadataManager
{
    private static readonly Regex DiscSuffixRegex = new(
        @"\s*-\s*(?:Disc|Disk|CD)\s*\d+\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static SaturnGame? ReadFromFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var game = new SaturnGame { FullFolderPath = folderPath };

        string namePath = Path.Combine(folderPath, Constants.NameFile);
        if (File.Exists(namePath))
            game.Name = StripDiscSuffix(ReadTextFile(namePath).Trim());

        string discPath = Path.Combine(folderPath, Constants.DiscFile);
        if (File.Exists(discPath))
            game.Disc = ReadTextFile(discPath).Trim();

        string regionPath = Path.Combine(folderPath, Constants.RegionFile);
        if (File.Exists(regionPath))
            game.Region = ReadTextFile(regionPath).Trim();

        string versionPath = Path.Combine(folderPath, Constants.VersionFile);
        if (File.Exists(versionPath))
            game.Version = ReadTextFile(versionPath).Trim();

        string datePath = Path.Combine(folderPath, Constants.DateFile);
        if (File.Exists(datePath))
            game.ReleaseDate = ReadTextFile(datePath).Trim();

        string folderTxtPath = Path.Combine(folderPath, Constants.FolderFile);
        if (File.Exists(folderTxtPath))
        {
            string rawFolder = ReadTextFile(folderTxtPath).Trim();
            game.Folder = rawFolder.Replace('/', '\\').Trim('\\');
        }

        // Read alternative folder paths
        var altFolders = new List<string>();
        foreach (var altFileName in Constants.FolderAltFiles)
        {
            string altPath = Path.Combine(folderPath, altFileName);
            if (File.Exists(altPath))
            {
                string altValue = ReadTextFile(altPath).Trim();
                if (!string.IsNullOrEmpty(altValue))
                    altFolders.Add(altValue.Replace('/', '\\').Trim('\\'));
            }
        }
        if (altFolders.Count > 0)
            game.AlternativeFolders = altFolders;

        string productIdPath = Path.Combine(folderPath, Constants.ProductIdFile);
        if (File.Exists(productIdPath))
            game.ProductId = ReadTextFile(productIdPath).Trim();

        return game;
    }

    /// <summary>
    /// Reads sidecar metadata from a pre-enumerated file list, avoiding
    /// per-file File.Exists calls. Also reports whether all required
    /// sidecar cache files were present.
    /// </summary>
    public static (SaturnGame game, bool hasAllSidecars) ReadFromFolder(string folderPath, string[] files)
    {
        var game = new SaturnGame { FullFolderPath = folderPath };

        // Build a lookup from filename to full path for fast in-memory searching
        var fileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
            fileMap[Path.GetFileName(f)] = f;

        bool hasName = false, hasDisc = false, hasRegion = false;
        bool hasVersion = false, hasDate = false, hasProductId = false;

        if (fileMap.TryGetValue(Constants.NameFile, out var namePath))
        {
            game.Name = StripDiscSuffix(ReadTextFile(namePath).Trim());
            hasName = true;
        }

        if (fileMap.TryGetValue(Constants.DiscFile, out var discPath))
        {
            game.Disc = ReadTextFile(discPath).Trim();
            hasDisc = true;
        }

        if (fileMap.TryGetValue(Constants.RegionFile, out var regionPath))
        {
            game.Region = ReadTextFile(regionPath).Trim();
            hasRegion = true;
        }

        if (fileMap.TryGetValue(Constants.VersionFile, out var versionPath))
        {
            game.Version = ReadTextFile(versionPath).Trim();
            hasVersion = true;
        }

        if (fileMap.TryGetValue(Constants.DateFile, out var datePath))
        {
            game.ReleaseDate = ReadTextFile(datePath).Trim();
            hasDate = true;
        }

        if (fileMap.TryGetValue(Constants.FolderFile, out var folderTxtPath))
        {
            string rawFolder = ReadTextFile(folderTxtPath).Trim();
            game.Folder = rawFolder.Replace('/', '\\').Trim('\\');
        }

        var altFolders = new List<string>();
        foreach (var altFileName in Constants.FolderAltFiles)
        {
            if (fileMap.TryGetValue(altFileName, out var altPath))
            {
                string altValue = ReadTextFile(altPath).Trim();
                if (!string.IsNullOrEmpty(altValue))
                    altFolders.Add(altValue.Replace('/', '\\').Trim('\\'));
            }
        }
        if (altFolders.Count > 0)
            game.AlternativeFolders = altFolders;

        if (fileMap.TryGetValue(Constants.ProductIdFile, out var productIdPath))
        {
            game.ProductId = ReadTextFile(productIdPath).Trim();
            hasProductId = true;
        }

        bool hasAllSidecars = hasName && hasDisc && hasRegion && hasVersion && hasDate && hasProductId;
        return (game, hasAllSidecars);
    }

    public static bool HasAllSidecarFiles(string folderPath)
    {
        return File.Exists(Path.Combine(folderPath, Constants.NameFile)) &&
               File.Exists(Path.Combine(folderPath, Constants.DiscFile)) &&
               File.Exists(Path.Combine(folderPath, Constants.RegionFile)) &&
               File.Exists(Path.Combine(folderPath, Constants.VersionFile)) &&
               File.Exists(Path.Combine(folderPath, Constants.DateFile)) &&
               File.Exists(Path.Combine(folderPath, Constants.ProductIdFile));
    }

    public static void WriteToFolder(string folderPath, SaturnGame game)
    {
        Directory.CreateDirectory(folderPath);

        WriteTextFile(Path.Combine(folderPath, Constants.NameFile), game.Name);
        WriteTextFile(Path.Combine(folderPath, Constants.DiscFile), game.Disc);
        WriteTextFile(Path.Combine(folderPath, Constants.RegionFile), game.Region);
        WriteTextFile(Path.Combine(folderPath, Constants.VersionFile), game.Version);
        WriteTextFile(Path.Combine(folderPath, Constants.DateFile), game.ReleaseDate);

        string folderTxtPath = Path.Combine(folderPath, Constants.FolderFile);
        if (!string.IsNullOrWhiteSpace(game.Folder))
        {
            // RmenuKai uses forward slashes for virtual folder paths on disk
            WriteTextFile(folderTxtPath, game.Folder.Replace('\\', '/'));
        }
        else if (File.Exists(folderTxtPath))
        {
            File.Delete(folderTxtPath);
        }

        // Write alternative folder path sidecar files
        for (int i = 0; i < Constants.FolderAltFiles.Length; i++)
        {
            string altFilePath = Path.Combine(folderPath, Constants.FolderAltFiles[i]);
            string altValue = (i < game.AlternativeFolders.Count)
                ? game.AlternativeFolders[i]
                : string.Empty;

            if (string.IsNullOrEmpty(altValue))
            {
                if (File.Exists(altFilePath))
                    File.Delete(altFilePath);
            }
            else
            {
                WriteTextFile(altFilePath, altValue.Replace('\\', '/'));
            }
        }

        WriteTextFile(Path.Combine(folderPath, Constants.ProductIdFile), game.ProductId ?? "");
    }

    /// <summary>
    /// Writes sidecar files that don't already exist, preserving any user edits.
    /// </summary>
    public static void WriteMissingSidecarFiles(string folderPath, SaturnGame game)
    {
        Directory.CreateDirectory(folderPath);

        WriteIfMissing(Path.Combine(folderPath, Constants.NameFile), game.Name);
        WriteIfMissing(Path.Combine(folderPath, Constants.DiscFile), game.Disc);
        WriteIfMissing(Path.Combine(folderPath, Constants.RegionFile), game.Region);
        WriteIfMissing(Path.Combine(folderPath, Constants.VersionFile), game.Version);
        WriteIfMissing(Path.Combine(folderPath, Constants.DateFile), game.ReleaseDate);

        // Folder.txt uses forward slashes on disk (RmenuKai convention)
        if (!string.IsNullOrWhiteSpace(game.Folder))
            WriteIfMissing(Path.Combine(folderPath, Constants.FolderFile),
                game.Folder.Replace('\\', '/'));

        // Write any alternative folder paths that aren't on disk yet
        for (int i = 0; i < game.AlternativeFolders.Count && i < Constants.FolderAltFiles.Length; i++)
        {
            string altValue = game.AlternativeFolders[i];
            if (!string.IsNullOrEmpty(altValue))
                WriteIfMissing(Path.Combine(folderPath, Constants.FolderAltFiles[i]),
                    altValue.Replace('\\', '/'));
        }

        // Always create ProductID.txt (even if empty) so HasAllSidecarFiles
        // returns true and we don't re-scan this folder on every load.
        string pidPath = Path.Combine(folderPath, Constants.ProductIdFile);
        if (!File.Exists(pidPath))
            WriteTextFile(pidPath, game.ProductId ?? "");
    }

    public static string ReadTextFile(string path)
    {
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    public static void WriteTextFile(string path, string content)
    {
        File.WriteAllText(path, content, System.Text.Encoding.UTF8);
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (!File.Exists(path) && !string.IsNullOrEmpty(content))
            WriteTextFile(path, content);
    }

    /// <summary>
    /// Strips legacy " - Disc N" suffixes from game titles.
    /// orbital_organizer.pl appended these to Name.txt; we store
    /// disc info separately in Disc.txt instead.
    /// </summary>
    private static string StripDiscSuffix(string title)
    {
        return DiscSuffixRegex.Replace(title, "");
    }
}
