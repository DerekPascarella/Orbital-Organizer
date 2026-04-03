using System.Text.RegularExpressions;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Strips Redump-style metadata tags from file and folder names.
/// e.g. "Grandia (Japan) (Disc 1) (1M)" -> "Grandia"
/// </summary>
public static class NameSanitizer
{
    // Matches a Redump-style parenthetical tag preceded by a space.
    // Covers: regions, language codes, disc numbers, revisions, memory variants,
    // special editions, prototypes, demos, and other standard Redump tags.
    private static readonly Regex RedumpTagPattern = new(
        @" \((" +
        // Regions (single or comma-separated)
        @"Japan|USA|Europe|World|Korea|Brazil|France|Germany|Italy|Spain|Australia|" +
        // Multi-region like "Japan, Europe" or "USA, Europe"
        @"(?:Japan|USA|Europe|France|Germany|Italy|Spain|Australia|Brazil|Korea)(?:,\s*(?:Japan|USA|Europe|France|Germany|Italy|Spain|Australia|Brazil|Korea))+|" +
        // Language codes like "En,Fr,De" or "En,Fr,De,Es,It"
        @"(?:En|Fr|De|Es|It|Ja|Nl|Sv|Pt|Ko|Zh|Da|Fi|No|Pl|Ru)(?:,(?:En|Fr|De|Es|It|Ja|Nl|Sv|Pt|Ko|Zh|Da|Fi|No|Pl|Ru))+|" +
        // Disc numbers
        @"Disc \d+|" +
        // Revisions
        @"Rev [A-Z0-9]+|" +
        // Memory/stamper variants
        @"\d+[MS]|" +
        // Multi-variant like "1M, 2M" or "8M, 13M"
        @"\d+[MS](?:,\s*\d+[MS])+|" +
        // Special tags (strip these; Demo/Beta/Proto/Special/Omake CD are kept)
        @"Unl|Sample|Rerelease|Satakore" +
        @")\)",
        RegexOptions.Compiled);

    // Matches " [anything]" bracket tags like [CCD-IMG-SUB] or [Rhea-Phoebe Version]
    private static readonly Regex BracketTagPattern = new(
        @" \[[^\]]+\]",
        RegexOptions.Compiled);

    /// <summary>
    /// Strips Redump metadata tags and bracket tags from a name.
    /// Only strips recognized Redump-style tags; unrecognized parentheticals are kept.
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        // Strip bracket tags (e.g., [Rhea-Phoebe Version])
        name = BracketTagPattern.Replace(name, "");

        // Find the first Redump tag and strip everything from there onward
        var match = RedumpTagPattern.Match(name);
        if (match.Success && match.Index > 0)
            name = name[..match.Index];

        return name.Trim();
    }
}
