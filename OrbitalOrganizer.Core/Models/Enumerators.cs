namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// Tracks whether a game item needs to be written to the SD card.
/// </summary>
public enum WorkMode
{
    /// <summary>Item is already on the SD card and unchanged.</summary>
    None,
    /// <summary>Item is new and needs to be copied to the SD card.</summary>
    New,
    /// <summary>Item is on the SD card but needs to be moved/renumbered.</summary>
    Move
}

/// <summary>
/// The disc image format of a game.
/// </summary>
public enum FileFormat
{
    /// <summary>Standard uncompressed disc image (CDI, MDF, IMG, ISO).</summary>
    Uncompressed,
    /// <summary>CloneCD format (CCD + IMG + SUB).</summary>
    CloneCd,
    /// <summary>CUE/BIN format (needs conversion to CCD/IMG/SUB).</summary>
    CueBin,
    /// <summary>CHD format (needs CHD-to-CUE/BIN, then CUE/BIN-to-CCD/IMG/SUB).</summary>
    Chd,
    /// <summary>Compressed archive (.7z, .rar, .zip) containing disc image(s).</summary>
    Compressed
}

/// <summary>
/// The type of menu system on the SD card.
/// </summary>
public enum MenuKind
{
    /// <summary>No menu detected.</summary>
    None,
    /// <summary>Legacy RMENU only.</summary>
    Rmenu,
    /// <summary>RmenuKai (enhanced menu with virtual folder support).</summary>
    RmenuKai,
    /// <summary>Both RmenuKai (in folder 01) and legacy RMENU (separate folder).</summary>
    Both
}

/// <summary>
/// How to determine the display name of a game.
/// </summary>
public enum RenameBy
{
    /// <summary>Use the name from IP.BIN header.</summary>
    IpBin,
    /// <summary>Use the folder name.</summary>
    Folder,
    /// <summary>Use the disc image file name.</summary>
    File
}

/// <summary>
/// Identifies who originally set up an SD card, which determines
/// what sidecar data can be recovered from LIST.INI during migration.
/// </summary>
public enum CardOrigin
{
    /// <summary>Not managed by Orbital Organizer (no GameList.txt).</summary>
    NonOO,
    /// <summary>Managed by orbital_organizer.pl (GameList.txt without box-drawing).</summary>
    PerlOO,
    /// <summary>Managed by the C# Orbital Organizer GUI (GameList.txt with box-drawing table).</summary>
    CSharpOO
}
