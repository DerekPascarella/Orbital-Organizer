namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// Represents parsed SEGA Saturn IP.BIN header metadata.
/// </summary>
public class IpBin
{
    public string Title { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ReleaseDate { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Disc { get; set; } = "1/1";
    public string HardwareId { get; set; } = string.Empty;

    /// <summary>
    /// Whether this IP.BIN was populated from defaults rather than actual disc data.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The byte offset where the IP.BIN header was found in the disc image.
    /// -1 if not found.
    /// </summary>
    public long HeaderOffset { get; set; } = -1;

    /// <summary>
    /// Extracts the disc number from the Disc field (e.g., "2/4" returns 2).
    /// </summary>
    public int DiscNumber
    {
        get
        {
            var parts = Disc.Split('/');
            return parts.Length >= 1 && int.TryParse(parts[0], out var num) ? num : 1;
        }
    }

    /// <summary>
    /// Extracts the total disc count from the Disc field (e.g., "2/4" returns 4).
    /// </summary>
    public int TotalDiscs
    {
        get
        {
            var parts = Disc.Split('/');
            return parts.Length >= 2 && int.TryParse(parts[1], out var num) ? num : 1;
        }
    }

    /// <summary>
    /// Whether this is a multi-disc game (total discs > 1).
    /// </summary>
    public bool IsMultiDisc => TotalDiscs > 1;
}
