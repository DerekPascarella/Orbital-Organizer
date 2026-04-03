using System.Runtime.InteropServices;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Scans an SD card for Saturn disc images in numbered folders.
/// Uses lazy loading: reads sidecar text files first, falls back to IP.BIN parsing.
/// </summary>
public static class CardScanner
{
    /// <summary>
    /// Scans the SD card for games, invoking a callback as each is loaded.
    /// Folder 01 is skipped (that's the menu system).
    /// Loads from sidecar files where available; folders missing sidecars get
    /// a placeholder entry (Ip will be null) for later metadata scanning.
    /// Returns the number of games found.
    /// </summary>
    public static async Task<int> ScanCardAsync(string sdCardPath, Action<SaturnGame> onGameLoaded)
    {
        var directories = Directory.GetDirectories(sdCardPath)
            .Select(d => new { Path = d, Name = Path.GetFileName(d) })
            .Where(d => int.TryParse(d.Name, out _))
            .Where(d => d.Name != Constants.MenuFolderName)
            .OrderBy(d => int.Parse(d.Name))
            .ToList();

        int count = 0;

        foreach (var dir in directories)
        {
            int folderNumber = int.Parse(dir.Name);

            var game = await Task.Run(() => LoadGameFromFolder(dir.Path, folderNumber));
            if (game != null)
            {
                onGameLoaded(game);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Loads a single game from a numbered folder.
    /// Uses sidecar files if all are present (fast path). Otherwise creates a
    /// placeholder entry without parsing IP.BIN, leaving Ip as null so the
    /// caller can identify items needing a metadata scan.
    /// </summary>
    private static SaturnGame? LoadGameFromFolder(string folderPath, int folderNumber)
    {
        // Single enumeration of all files in the folder. The list is reused
        // for sidecar lookups, disc image detection, and image file population
        // so we never hit the filesystem twice for the same folder.
        var files = Directory.GetFiles(folderPath);

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool hasDiscImage = files.Any(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return Constants.AllImageExtensions.Contains(ext) ||
                   (isWindows && ext == ".chd");
        });

        // Use the file-list overload so sidecar existence checks are
        // in-memory dictionary lookups, not per-file File.Exists calls.
        var (game, hasAllSidecars) = MetadataManager.ReadFromFolder(folderPath, files);

        bool hasName = !string.IsNullOrWhiteSpace(game.Name);

        if (!hasName && !hasDiscImage)
            return null;

        if (!hasName)
            game.Name = Path.GetFileName(folderPath);

        game.SdNumber = folderNumber;
        game.FullFolderPath = folderPath;
        game.WorkMode = WorkMode.None;
        game.NeedsMetadataScan = !hasAllSidecars && hasDiscImage;

        PopulateImageFiles(game, files);

        return game;
    }

    /// <summary>
    /// Fills missing metadata for a game folder. Tries LIST.INI first
    /// (if an entry is provided), then falls back to IP.BIN parsing for
    /// anything still missing. Writes sidecar files so future loads are fast.
    /// </summary>
    public static void ScanAndCacheMetadata(SaturnGame game, MigrationService.ListIniEntry? listIniEntry = null)
    {
        if (string.IsNullOrEmpty(game.FullFolderPath))
            return;

        string folderPath = game.FullFolderPath;

        // Step 1: Try to fill gaps from LIST.INI (preserves user-customized values)
        if (listIniEntry != null)
        {
            if (string.IsNullOrWhiteSpace(game.Name) || game.Name == Path.GetFileName(folderPath))
                game.Name = listIniEntry.Title;
            if (string.IsNullOrWhiteSpace(game.Region))
                game.Region = listIniEntry.Region;
            if (string.IsNullOrWhiteSpace(game.Version))
            {
                // LIST.INI stores version with "V" prefix (e.g., "V1.003")
                // but Version.txt stores it without (e.g., "1.003")
                string ver = listIniEntry.Version;
                if (!string.IsNullOrEmpty(ver) && ver.Contains('V'))
                    ver = ver[(ver.LastIndexOf('V') + 1)..];
                game.Version = ver;
            }
            if (string.IsNullOrWhiteSpace(game.ReleaseDate))
                game.ReleaseDate = listIniEntry.Date;
            if (string.IsNullOrWhiteSpace(game.Disc) || game.Disc == "1/1")
            {
                if (!string.IsNullOrWhiteSpace(listIniEntry.Disc))
                    game.Disc = listIniEntry.Disc;
            }
            if (string.IsNullOrWhiteSpace(game.Folder) && !string.IsNullOrEmpty(listIniEntry.VirtualFolder))
                game.Folder = listIniEntry.VirtualFolder.Replace('/', '\\').Trim('\\');

            if (game.AlternativeFolders.Count == 0 && listIniEntry.AlternativeFolders.Count > 0)
            {
                game.AlternativeFolders = listIniEntry.AlternativeFolders
                    .Select(f => f.Replace('/', '\\').Trim('\\'))
                    .ToList();
            }
        }

        // Step 2: Fall back to IP.BIN for anything LIST.INI couldn't provide
        // (ProductID always comes from here since LIST.INI never has it)
        bool stillMissing = string.IsNullOrWhiteSpace(game.ProductId) ||
                            string.IsNullOrWhiteSpace(game.Name) || game.Name == Path.GetFileName(folderPath) ||
                            string.IsNullOrWhiteSpace(game.Region) || game.Region == "NA" ||
                            string.IsNullOrWhiteSpace(game.Version) || game.Version == "NA" ||
                            string.IsNullOrWhiteSpace(game.ReleaseDate) || game.ReleaseDate == "NA" ||
                            string.IsNullOrWhiteSpace(game.Disc);

        if (stillMissing)
        {
            var (offset, filePath) = IpBinParser.FindIpBinInFolder(folderPath);
            if (offset >= 0 && filePath != null)
            {
                // CHD needs libchdr to extract IP.BIN
                IpBin? ip = Path.GetExtension(filePath).Equals(".chd", StringComparison.OrdinalIgnoreCase)
                    ? IpBinParser.ParseHeaderFromChd(filePath)
                    : IpBinParser.ParseHeader(filePath, offset);

                if (ip != null)
                {
                    game.Ip = ip;

                    if (string.IsNullOrWhiteSpace(game.Name) || game.Name == Path.GetFileName(folderPath))
                        game.Name = ip.Title;
                    if (string.IsNullOrWhiteSpace(game.ProductId))
                        game.ProductId = ip.ProductId;
                    if (string.IsNullOrWhiteSpace(game.Region) || game.Region == "NA")
                        game.Region = ip.Region;
                    if (string.IsNullOrWhiteSpace(game.Version) || game.Version == "NA")
                        game.Version = ip.Version;
                    if (string.IsNullOrWhiteSpace(game.ReleaseDate) || game.ReleaseDate == "NA")
                        game.ReleaseDate = ip.ReleaseDate;
                    if ((string.IsNullOrWhiteSpace(game.Disc) || game.Disc == "1/1") && !string.IsNullOrWhiteSpace(ip.Disc))
                        game.Disc = ip.Disc;
                }
            }
        }

        // Defaults for anything we still don't have
        if (string.IsNullOrWhiteSpace(game.Disc)) game.Disc = "1/1";
        if (string.IsNullOrWhiteSpace(game.Region)) game.Region = "NA";
        if (string.IsNullOrWhiteSpace(game.Version)) game.Version = "NA";
        if (string.IsNullOrWhiteSpace(game.ReleaseDate)) game.ReleaseDate = "NA";

        // Write sidecar files so the next load is fast
        MetadataManager.WriteMissingSidecarFiles(folderPath, game);

        game.NeedsMetadataScan = false;
    }

    /// <summary>
    /// Checks whether a folder contains any recognized disc image files.
    /// </summary>
    public static bool HasDiscImage(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return false;

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        foreach (var file in Directory.GetFiles(folderPath))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (Constants.AllImageExtensions.Contains(ext))
                return true;
            if (isWindows && ext == ".chd")
                return true;
        }

        return false;
    }

    /// <summary>
    /// Populates the game's ImageFiles and Length from a pre-enumerated file list.
    /// </summary>
    private static void PopulateImageFiles(SaturnGame game, string[] files)
    {
        game.ImageFiles.Clear();
        long totalSize = 0;
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();

            if (Constants.AllImageExtensions.Contains(ext) ||
                ext == ".img" || ext == ".sub" || ext == ".bin" ||
                (isWindows && ext == ".chd"))
            {
                game.ImageFiles.Add(file);
                totalSize += new FileInfo(file).Length;
            }
        }

        game.Length = totalSize;

        if (game.ImageFiles.Any(f => Path.GetExtension(f).Equals(".ccd", StringComparison.OrdinalIgnoreCase)))
            game.FileFormat = FileFormat.CloneCd;
        else if (game.ImageFiles.Any(f => Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase)))
            game.FileFormat = FileFormat.CueBin;
        else if (isWindows && game.ImageFiles.Any(f => Path.GetExtension(f).Equals(".chd", StringComparison.OrdinalIgnoreCase)))
            game.FileFormat = FileFormat.Chd;
        else
            game.FileFormat = FileFormat.Uncompressed;
    }
}
