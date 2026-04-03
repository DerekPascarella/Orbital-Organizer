using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Detects the menu system type present on an SD card.
/// </summary>
public static class MenuDetector
{
    /// <summary>
    /// Detects the menu system in folder 01. Only inspects folder 01 itself;
    /// legacy RMENU instances in other folders are discovered during the
    /// normal card scan (see Manager.LoadItemsFromCardAsync).
    /// </summary>
    public static MenuKind Detect(string sdCardPath)
    {
        string menuFolder = Path.Combine(sdCardPath, Constants.MenuFolderName);

        if (!Directory.Exists(menuFolder))
            return MenuKind.None;

        bool isRmenuKai = false;
        bool foundMenu = false;

        // Check BIN/RMENU/0.BIN first (Orbital Organizer saved format)
        string zeroBinPath = Path.Combine(menuFolder, "BIN", "RMENU", "0.BIN");
        if (File.Exists(zeroBinPath))
        {
            foundMenu = true;
            isRmenuKai = FindBytesInFile(zeroBinPath, Constants.RmenuKaiSignature);
        }

        // Scan disc image files in folder 01 (covers both fresh cards and as a fallback)
        if (!isRmenuKai)
        {
            foreach (var file in Directory.GetFiles(menuFolder))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".iso" || ext == ".cdi" || ext == ".mdf" || ext == ".img" || ext == ".ccd")
                {
                    foundMenu = true;
                    if (FindBytesInFile(file, Constants.RmenuKaiSignature))
                    {
                        isRmenuKai = true;
                        break;
                    }
                }
            }
        }

        if (!foundMenu)
            return MenuKind.None;

        return isRmenuKai ? MenuKind.RmenuKai : MenuKind.Rmenu;
    }

    // No longer needed. Legacy RMENU instances are now detected during
    // the normal card scan in Manager.LoadItemsFromCardAsync, which
    // already reads Name.txt for every folder. Doing a separate pre-scan
    // here was redundant and caused a noticeable delay before items
    // appeared in the games list.
    //
    // private static bool HasLegacyRmenuElsewhere(string sdCardPath) { ... }
    // public static int FindLegacyRmenuFolder(string sdCardPath) { ... }

    /// <summary>
    /// Returns true if a specified byte pattern is found anywhere in a file.
    /// </summary>
    public static bool FindBytesInFile(string filePath, byte[] pattern)
    {
        if (!File.Exists(filePath)) return false;

        byte[] target = pattern;
        int chunkSize = 1024 * 1024;
        byte[] buffer = new byte[chunkSize];
        long offset = 0;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        while (true)
        {
            fs.Position = offset;
            int bytesRead = fs.Read(buffer, 0, chunkSize);
            if (bytesRead == 0) break;

            if (IndexOfPattern(buffer, bytesRead, target) >= 0)
                return true;

            // Overlap by pattern length - 1 to catch patterns spanning chunk boundaries
            offset += chunkSize - target.Length + 1;
        }

        return false;
    }

    private static int IndexOfPattern(byte[] buffer, int bufferLength, byte[] pattern)
    {
        int limit = bufferLength - pattern.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (buffer[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }
}
