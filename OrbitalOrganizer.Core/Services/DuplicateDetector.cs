using System.Text.RegularExpressions;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Detects and tags duplicate game names to keep them unique in the game list.
/// Handles the [UNIQUE-XXXXXX] tag convention.
/// </summary>
public static class DuplicateDetector
{
    private static readonly Regex UniqueTagPattern = new(
        @"\s*\[UNIQUE-[A-Z0-9]+\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DiscSuffixPattern = new(
        @"\s*-\s*(?:Disc|Disk|CD)\s*0*([1-9]\d*)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Strips any [UNIQUE-XXXXXX] tag from a game name.
    /// </summary>
    public static string StripUniqueTag(string name)
    {
        return UniqueTagPattern.Replace(name, "");
    }

    /// <summary>
    /// Checks whether a name contains a [UNIQUE-XXXXXX] tag.
    /// </summary>
    public static bool HasUniqueTag(string name)
    {
        return UniqueTagPattern.IsMatch(name);
    }

    /// <summary>
    /// Extracts the [UNIQUE-XXXXXX] tag from a string (e.g., folder name).
    /// Returns null if no tag found.
    /// </summary>
    public static string? ExtractUniqueTag(string input)
    {
        var match = Regex.Match(input, @"\[UNIQUE-[A-Z0-9]+\]", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Normalizes a game name for duplicate comparison.
    /// Strips folder path prefix and whitespace, but keeps disc suffix.
    /// </summary>
    public static string GetBaseName(string name)
    {
        // Strip any [UNIQUE-] tag first
        string clean = StripUniqueTag(name);

        // Normalize " - Disc X" to "/Disc X" so multi-disc labels match
        clean = DiscSuffixPattern.Replace(clean, m => $"/Disc {m.Groups[1].Value}");

        // Remove leading virtual folder path (everything before the last '/')
        int lastSlash = clean.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash < clean.Length - 1)
        {
            // Check if what follows is "Disc N" (multi-disc subfolder)
            string afterSlash = clean[(lastSlash + 1)..].Trim();
            if (afterSlash.StartsWith("Disc ", StringComparison.OrdinalIgnoreCase))
            {
                // Keep the disc part, strip further
                int prevSlash = clean.LastIndexOf('/', lastSlash - 1);
                if (prevSlash >= 0)
                    clean = clean[(prevSlash + 1)..];
            }
            else
            {
                clean = afterSlash;
            }
        }

        return clean.Trim();
    }

    /// <summary>
    /// Checks if a game name is a duplicate of any existing name in the set.
    /// Uses case-insensitive base name comparison.
    /// </summary>
    public static bool IsDuplicate(string gameName, IEnumerable<string> existingNames)
    {
        string baseName = GetBaseName(gameName).ToLowerInvariant();

        foreach (var existing in existingNames)
        {
            if (GetBaseName(existing).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Appends a [UNIQUE-XXXXXX] tag to a game name. Inserts before "/Disc N" if present,
    /// otherwise appends at the end.
    /// </summary>
    public static string AppendUniqueTag(string gameName)
    {
        string tag = $"[UNIQUE-{FolderHelper.GenerateUniqueTag()}]";

        // Try to insert before "/Disc N" suffix
        var discMatch = Regex.Match(gameName, @"(/Disc\s*0*[1-9]\d*\s*$)", RegexOptions.IgnoreCase);
        if (discMatch.Success)
        {
            return gameName[..discMatch.Index] + $" {tag}" + gameName[discMatch.Index..];
        }

        return $"{gameName} {tag}";
    }

    /// <summary>
    /// Strips [UNIQUE-XXXXXX] tags for display and sorting purposes.
    /// Also converts "/Disc" back to " - Disc" for display.
    /// </summary>
    public static string FormatForDisplay(string gameName)
    {
        string display = StripUniqueTag(gameName);
        display = display.Replace("/Disc", " - Disc");
        return display.Trim();
    }
}
