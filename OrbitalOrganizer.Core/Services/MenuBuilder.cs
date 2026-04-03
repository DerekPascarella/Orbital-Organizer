using System.Text;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Generates LIST.INI and GameList.txt content for RMENU/RmenuKai.
/// </summary>
public static class MenuBuilder
{
    /// <summary>
    /// Generates the LIST.INI file content from the game list.
    /// </summary>
    /// <param name="games">Ordered list of games (excluding the menu item at position 01).</param>
    /// <param name="useVirtualFolderSubfolders">Whether multi-disc games should use virtual folder subfolders.</param>
    /// <returns>The LIST.INI file content string.</returns>
    public static string GenerateListIni(IReadOnlyList<SaturnGame> games, bool useVirtualFolderSubfolders, MenuKind menuKind = MenuKind.RmenuKai)
    {
        var sb = new StringBuilder();

        // Entry 01 is always RMENU itself
        sb.AppendLine("01.title=RMENU");
        sb.AppendLine("01.disc=1/1");
        sb.AppendLine("01.region=JTUE");
        sb.AppendLine("01.version=V0.2.0");
        sb.AppendLine("01.date=20170228");

        int listNumber = 1;
        bool includeFolders = menuKind != MenuKind.Rmenu;

        // First pass: primary folder entries
        var altEntries = new List<(string Num, SaturnGame Game, string AltFolder)>();

        foreach (var game in games)
        {
            listNumber++;
            string num = listNumber.ToString("D2");
            if (listNumber >= 100) num = listNumber.ToString();

            string title = BuildListTitle(game, useVirtualFolderSubfolders && includeFolders, includeFolders);
            AppendEntryBlock(sb, num, title, game);

            // Collect alt folder entries for second pass
            if (includeFolders && game.AlternativeFolders.Count > 0)
            {
                foreach (var altFolder in game.AlternativeFolders)
                    altEntries.Add((num, game, altFolder));
            }
        }

        // Second pass: alt folder entries (after all primaries, matching RmenuKai convention)
        foreach (var (num, game, altFolder) in altEntries)
        {
            string altTitle = BuildListTitleWithFolder(game, altFolder,
                useVirtualFolderSubfolders && includeFolders);
            AppendEntryBlock(sb, num, altTitle, game);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates the GameList.txt content as a formatted table with box-drawing characters.
    /// The Folder column is only included for RmenuKai/Both modes.
    /// </summary>
    public static string GenerateGameList(IReadOnlyList<SaturnGame> games, MenuKind menuKind = MenuKind.RmenuKai)
    {
        var sb = new StringBuilder();
        bool includeFolders = menuKind != MenuKind.Rmenu;

        // Build the full list including the menu entry at position 01
        var rows = new List<(string Num, string Folder, string Title, string Disc, string Serial, string Region)>();
        rows.Add(("01", "", "RMENU", "1/1", "", "JTUE"));

        int listNumber = 1;
        foreach (var game in games)
        {
            listNumber++;
            string num = listNumber < 100 ? listNumber.ToString("D2") : listNumber.ToString();
            string title = DuplicateDetector.StripUniqueTag(game.Name);
            string folder = includeFolders ? FormatFolderList(game) : "";
            string disc = !string.IsNullOrEmpty(game.Disc) ? game.Disc : "1/1";
            string serial = game.ProductId ?? "";
            string region = !string.IsNullOrWhiteSpace(game.Region) ? game.Region : "N/A";
            rows.Add((num, folder, title, disc, serial, region));
        }

        // Calculate column widths (minimum = header length)
        int colNum = Math.Max(1, rows.Max(r => r.Num.Length));
        int colTitle = Math.Max(5, rows.Max(r => r.Title.Length));
        int colDisc = Math.Max(4, rows.Max(r => r.Disc.Length));
        int colSerial = Math.Max(6, rows.Max(r => r.Serial.Length));
        int colRegion = Math.Max(6, rows.Max(r => r.Region.Length));

        if (includeFolders)
        {
            int colFolder = Math.Max(6, rows.Max(r => r.Folder.Length));

            string TopLine() => $"┌{"".PadRight(colNum + 2, '─')}┬{"".PadRight(colFolder + 2, '─')}┬{"".PadRight(colTitle + 2, '─')}┬{"".PadRight(colDisc + 2, '─')}┬{"".PadRight(colSerial + 2, '─')}┬{"".PadRight(colRegion + 2, '─')}┐";
            string MidLine() => $"├{"".PadRight(colNum + 2, '─')}┼{"".PadRight(colFolder + 2, '─')}┼{"".PadRight(colTitle + 2, '─')}┼{"".PadRight(colDisc + 2, '─')}┼{"".PadRight(colSerial + 2, '─')}┼{"".PadRight(colRegion + 2, '─')}┤";
            string BottomLine() => $"└{"".PadRight(colNum + 2, '─')}┴{"".PadRight(colFolder + 2, '─')}┴{"".PadRight(colTitle + 2, '─')}┴{"".PadRight(colDisc + 2, '─')}┴{"".PadRight(colSerial + 2, '─')}┴{"".PadRight(colRegion + 2, '─')}┘";
            string DataRow(string num, string folder, string title, string disc, string serial, string region) =>
                $"│ {num.PadLeft(colNum)} │ {folder.PadRight(colFolder)} │ {title.PadRight(colTitle)} │ {disc.PadRight(colDisc)} │ {serial.PadRight(colSerial)} │ {region.PadRight(colRegion)} │";

            sb.AppendLine(TopLine());
            sb.AppendLine(DataRow("#", "Folder", "Title", "Disc", "Serial", "Region"));
            sb.AppendLine(MidLine());

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine(DataRow(r.Num, r.Folder, r.Title, r.Disc, r.Serial, r.Region));
                if (i < rows.Count - 1)
                    sb.AppendLine(MidLine());
            }

            sb.AppendLine(BottomLine());
        }
        else
        {
            string TopLine() => $"┌{"".PadRight(colNum + 2, '─')}┬{"".PadRight(colTitle + 2, '─')}┬{"".PadRight(colDisc + 2, '─')}┬{"".PadRight(colSerial + 2, '─')}┬{"".PadRight(colRegion + 2, '─')}┐";
            string MidLine() => $"├{"".PadRight(colNum + 2, '─')}┼{"".PadRight(colTitle + 2, '─')}┼{"".PadRight(colDisc + 2, '─')}┼{"".PadRight(colSerial + 2, '─')}┼{"".PadRight(colRegion + 2, '─')}┤";
            string BottomLine() => $"└{"".PadRight(colNum + 2, '─')}┴{"".PadRight(colTitle + 2, '─')}┴{"".PadRight(colDisc + 2, '─')}┴{"".PadRight(colSerial + 2, '─')}┴{"".PadRight(colRegion + 2, '─')}┘";
            string DataRow(string num, string title, string disc, string serial, string region) =>
                $"│ {num.PadLeft(colNum)} │ {title.PadRight(colTitle)} │ {disc.PadRight(colDisc)} │ {serial.PadRight(colSerial)} │ {region.PadRight(colRegion)} │";

            sb.AppendLine(TopLine());
            sb.AppendLine(DataRow("#", "Title", "Disc", "Serial", "Region"));
            sb.AppendLine(MidLine());

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine(DataRow(r.Num, r.Title, r.Disc, r.Serial, r.Region));
                if (i < rows.Count - 1)
                    sb.AppendLine(MidLine());
            }

            sb.AppendLine(BottomLine());
        }

        return sb.ToString();
    }

    private static void AppendEntryBlock(StringBuilder sb, string num, string title, SaturnGame game)
    {
        sb.AppendLine($"{num}.title={title}");
        sb.AppendLine($"{num}.disc={game.Disc}");
        sb.AppendLine($"{num}.region={game.Region}");

        string version = game.Version;
        if (!string.IsNullOrEmpty(version) && version != "NA" &&
            !version.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            version = "V" + version;
        sb.AppendLine($"{num}.version={version}");
        sb.AppendLine($"{num}.date={game.ReleaseDate}");
    }

    /// <summary>
    /// Builds a title string using a specific folder path (for alt folder entries).
    /// </summary>
    private static string BuildListTitleWithFolder(SaturnGame game, string folder, bool useVirtualFolderSubfolders)
    {
        string title = DuplicateDetector.StripUniqueTag(game.Name);

        int discNumber = 0;
        bool isMultiDisc = false;
        if (game.Disc != "1/1" && !string.IsNullOrEmpty(game.Disc))
        {
            var parts = game.Disc.Split('/');
            if (parts.Length >= 2 && int.TryParse(parts[0], out discNumber) && int.TryParse(parts[1], out int total) && total > 1)
                isMultiDisc = true;
        }

        string cleanTitle = StripDiscSuffix(title);

        if (!string.IsNullOrWhiteSpace(folder))
        {
            string folderPath = folder.Replace('\\', '/');
            if (!folderPath.StartsWith('/')) folderPath = "/" + folderPath;
            if (!folderPath.EndsWith('/')) folderPath += "/";

            if (useVirtualFolderSubfolders && isMultiDisc)
                return $"{folderPath}{cleanTitle}/Disc {discNumber}";
            else
                return $"{folderPath}{cleanTitle}";
        }
        else
        {
            if (useVirtualFolderSubfolders && isMultiDisc)
                return $"/{cleanTitle}/Disc {discNumber}";
            else
                return cleanTitle;
        }
    }

    /// <summary>
    /// Builds the title string for a LIST.INI entry, including virtual folder path.
    /// Uses forward slashes as required by RmenuKai.
    /// </summary>
    private static string BuildListTitle(SaturnGame game, bool useVirtualFolderSubfolders, bool includeFolders)
    {
        string title = DuplicateDetector.StripUniqueTag(game.Name);

        // Legacy RMENU doesn't support virtual folder paths
        if (!includeFolders)
            return title;

        // Extract disc number for multi-disc subfolder handling
        int discNumber = 0;
        bool isMultiDisc = false;
        if (game.Disc != "1/1" && !string.IsNullOrEmpty(game.Disc))
        {
            var parts = game.Disc.Split('/');
            if (parts.Length >= 2 && int.TryParse(parts[0], out discNumber) && int.TryParse(parts[1], out int total) && total > 1)
                isMultiDisc = true;
        }

        // Strip " - Disc N" suffix from the title if present (we'll handle it via folder path)
        string cleanTitle = StripDiscSuffix(title);

        // Build the full path
        if (!string.IsNullOrWhiteSpace(game.Folder))
        {
            // Convert backslashes to forward slashes for LIST.INI
            string folderPath = game.Folder.Replace('\\', '/');
            if (!folderPath.StartsWith('/')) folderPath = "/" + folderPath;
            if (!folderPath.EndsWith('/')) folderPath += "/";

            if (useVirtualFolderSubfolders && isMultiDisc)
                return $"{folderPath}{cleanTitle}/Disc {discNumber}";
            else
                return $"{folderPath}{cleanTitle}";
        }
        else
        {
            if (useVirtualFolderSubfolders && isMultiDisc)
                return $"/{cleanTitle}/Disc {discNumber}";
            else
                return cleanTitle;
        }
    }

    /// <summary>
    /// Formats all folder paths (primary + alt) as a comma-separated list for display.
    /// </summary>
    private static string FormatFolderList(SaturnGame game)
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(game.Folder))
            folders.Add(game.Folder);
        foreach (var alt in game.AlternativeFolders)
        {
            if (!string.IsNullOrWhiteSpace(alt))
                folders.Add(alt);
        }

        if (folders.Count == 0) return "";
        if (folders.Count == 1) return folders[0];
        if (folders.Count == 2) return $"{folders[0]} and {folders[1]}";
        return string.Join(", ", folders.Take(folders.Count - 1)) + ", and " + folders[^1];
    }

    /// <summary>
    /// Strips " - Disc N" suffix from a game title.
    /// </summary>
    private static string StripDiscSuffix(string title)
    {
        // Match patterns like " - Disc 1", " - Disk 2", " - CD 3"
        var match = System.Text.RegularExpressions.Regex.Match(
            title,
            @"\s*-\s*(?:Disc|Disk|CD)\s*\d+\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? title[..match.Index] : title;
    }
}
