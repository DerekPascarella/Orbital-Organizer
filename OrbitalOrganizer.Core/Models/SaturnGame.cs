using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrbitalOrganizer.Core.Services;

namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// Represents a single SEGA Saturn disc image on the SD card.
/// </summary>
public class SaturnGame : INotifyPropertyChanged
{
    private const int MaxAlternativeFolders = 5;

    private string _name = string.Empty;
    private string _folder = string.Empty;
    private string _productId = string.Empty;
    private string _disc = "1/1";
    private int _sdNumber;
    private WorkMode _workMode = WorkMode.None;
    private List<string> _alternativeFolders = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Display name of the game.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            var sanitized = AsciiSanitizer.SanitizeName(value);
            if (_name != sanitized) { _name = sanitized; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// Virtual folder path using backslash separators (e.g., "Shmup\Raizing").
    /// Empty string means no folder (root level).
    /// </summary>
    public string Folder
    {
        get => _folder;
        set
        {
            var cleaned = AsciiSanitizer.CleanFolderPath(value);
            if (_folder != cleaned) { _folder = cleaned; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// Product/serial ID from IP.BIN header (e.g., "T-14302G").
    /// </summary>
    public string ProductId
    {
        get => _productId;
        set
        {
            var sanitized = AsciiSanitizer.SanitizeProductId(value);
            if (_productId != sanitized) { _productId = sanitized; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// Disc number in "X/Y" format (e.g., "1/1", "2/4").
    /// </summary>
    public string Disc
    {
        get => _disc;
        set
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                var parts = trimmed.Split('/');
                if (!(parts.Length == 2 &&
                    int.TryParse(parts[0], out _) &&
                    int.TryParse(parts[1], out _)))
                    trimmed = "1/1";
            }
            else
            {
                trimmed = "1/1";
            }

            if (_disc != trimmed) { _disc = trimmed; OnPropertyChanged(); }
        }
    }

    /// <summary>
    /// Current numbered folder on the SD card (e.g., 2 for folder "02").
    /// 0 means the item is not yet on the SD card.
    /// </summary>
    public int SdNumber
    {
        get => _sdNumber;
        set { if (_sdNumber != value) { _sdNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(Location)); OnPropertyChanged(nameof(IsNotOnSdCard)); } }
    }

    /// <summary>
    /// Display string for Location column.
    /// </summary>
    public string Location => SdNumber > 0 ? "SD card" : "Other";

    /// <summary>
    /// Whether this item needs to be written/moved on the SD card.
    /// </summary>
    public WorkMode WorkMode
    {
        get => _workMode;
        set { if (_workMode != value) { _workMode = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Additional virtual folder paths (max 5) for RmenuKai multi-label support.
    /// </summary>
    public List<string> AlternativeFolders
    {
        get => _alternativeFolders;
        set
        {
            if (value == null)
            {
                _alternativeFolders = new List<string>();
            }
            else
            {
                _alternativeFolders = value
                    .Select(p => AsciiSanitizer.CleanFolderPath(p))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxAlternativeFolders)
                    .ToList();
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The disc image format.
    /// </summary>
    public FileFormat FileFormat { get; set; } = FileFormat.Uncompressed;

    /// <summary>
    /// For compressed archives, stores the format of the disc image inside
    /// the archive (e.g., CueBin, CloneCd). Null when FileFormat is not Compressed.
    /// </summary>
    public FileFormat? InnerFileFormat { get; set; }

    /// <summary>
    /// Parsed IP.BIN metadata. May be null if not yet loaded.
    /// </summary>
    public IpBin? Ip { get; set; }

    /// <summary>
    /// Full paths to all disc image files (e.g., .ccd + .img + .sub).
    /// </summary>
    public List<string> ImageFiles { get; set; } = new();

    /// <summary>
    /// Full path to the game's folder on the SD card (e.g., "H:\02").
    /// Empty if the item is not on the SD card.
    /// </summary>
    public string FullFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Source path for items being added from PC (not yet on SD card).
    /// </summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Total size of all disc image files in bytes.
    /// </summary>
    private long _length;
    public long Length
    {
        get => _length;
        set { if (_length != value) { _length = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Region string from IP.BIN (e.g., "JTU", "E").
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Version string from IP.BIN (e.g., "1.000").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Release date from IP.BIN (e.g., "19960308").
    /// </summary>
    public string ReleaseDate { get; set; } = string.Empty;

    /// <summary>
    /// Whether this item is the menu system (folder 01) and should be locked.
    /// </summary>
    public bool IsMenuItem => SdNumber == Constants.MenuFolderNumber && !IsLegacyRmenu;

    /// <summary>
    /// Whether this item is a legacy RMENU instance (movable, not locked to position 01).
    /// </summary>
    public bool IsLegacyRmenu { get; set; }

    /// <summary>
    /// Whether the info button should be available for this item.
    /// </summary>
    public bool IsGameEntry => !IsMenuItem && !IsLegacyRmenu;

    /// <summary>
    /// Whether this item has not yet been saved to the SD card (added from PC).
    /// </summary>
    public bool IsNotOnSdCard => SdNumber == 0;

    /// <summary>
    /// Set during loading when the folder is missing one or more sidecar
    /// cache files and has a disc image that can be scanned for IP.BIN.
    /// </summary>
    public bool NeedsMetadataScan { get; set; }

    /// <summary>
    /// Set when the user edits the Product ID through the UI.
    /// Only games with this flag get their disc images patched on save.
    /// </summary>
    public bool ProductIdDirty { get; set; }

    /// <summary>
    /// Set when any sidecar-backed property is modified through the UI
    /// (Name, Folder, ProductId, Disc, AlternativeFolders).
    /// Only games with this flag get their sidecar files rewritten on save.
    /// </summary>
    public bool SidecarsDirty { get; set; }

    /// <summary>
    /// Generates the formatted folder number string (e.g., "02", "100", "1000").
    /// </summary>
    public string FolderNumberFormatted
    {
        get
        {
            if (SdNumber <= 0) return string.Empty;
            if (SdNumber < 100) return SdNumber.ToString("D2");
            return SdNumber.ToString();
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
