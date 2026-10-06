using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrbitalOrganizer.Core.Services;

namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// Represents a single SEGA Saturn disc image on the SD card.
/// </summary>
public class SaturnGame : INotifyPropertyChanged
{
    internal Action? EnsureCanMutate { get; set; }
    internal Action<Action>? NotifyObservers { get; set; }
    internal CardSaveRow? PreparedSource { get; set; }
    internal string SaveId { get; set; } = Guid.NewGuid().ToString("N");

    private const int MaxAlternativeFolders = 5;

    private string _name = string.Empty;
    private string _folder = string.Empty;
    private string _productId = string.Empty;
    private string _disc = "1/1";
    private int _sdNumber;
    private WorkMode _workMode = WorkMode.None;
    private IList<string>? _alternativeFolders;
    private bool _isMatch;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Display name of the game.
    /// </summary>
    public string Name
    {
        get => _name;
        set
        {
            EnsureCanMutate?.Invoke();
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
            EnsureCanMutate?.Invoke();
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
            EnsureCanMutate?.Invoke();
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
            EnsureCanMutate?.Invoke();
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
        set { EnsureCanMutate?.Invoke(); if (_sdNumber != value) { _sdNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(Location)); OnPropertyChanged(nameof(IsNotOnSdCard)); } }
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
        set { EnsureCanMutate?.Invoke(); if (_workMode != value) { _workMode = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Additional virtual folder paths (max 5) for RmenuKai multi-label support.
    /// </summary>
    public IList<string> AlternativeFolders
    {
        get => _alternativeFolders ??= new GuardedImageCollection(() => EnsureCanMutate?.Invoke());
        set
        {
            EnsureCanMutate?.Invoke();
            if (value == null)
            {
                _alternativeFolders = new GuardedImageCollection(() => EnsureCanMutate?.Invoke());
            }
            else
            {
                _alternativeFolders = new GuardedImageCollection(() => EnsureCanMutate?.Invoke(), value
                    .Select(p => AsciiSanitizer.CleanFolderPath(p))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxAlternativeFolders));
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The disc image format.
    /// </summary>
    private FileFormat _fileFormat = FileFormat.Uncompressed;
    public FileFormat FileFormat { get => _fileFormat; set { EnsureCanMutate?.Invoke(); _fileFormat = value; } }

    /// <summary>
    /// For compressed archives, stores the format of the disc image inside
    /// the archive (e.g., CueBin, CloneCd). Null when FileFormat is not Compressed.
    /// </summary>
    private FileFormat? _innerFileFormat = default;
    public FileFormat? InnerFileFormat { get => _innerFileFormat; set { EnsureCanMutate?.Invoke(); _innerFileFormat = value; } }

    /// <summary>
    /// For compressed archives, identifies the disc image entry inside the
    /// archive so save can extract just its file set. Null when FileFormat
    /// is not Compressed.
    /// </summary>
    private ArchiveEntryInfo? _selectedArchiveEntry = default;
    public ArchiveEntryInfo? SelectedArchiveEntry { get => _selectedArchiveEntry; set { EnsureCanMutate?.Invoke(); _selectedArchiveEntry = value; } }

    /// <summary>
    /// Set on archive rows whose metadata has not been read yet. Save
    /// resolves it from the extracted disc image after copying.
    /// </summary>
    private bool _isArchiveMetadataPending = default;
    public bool IsArchiveMetadataPending { get => _isArchiveMetadataPending; set { EnsureCanMutate?.Invoke(); _isArchiveMetadataPending = value; } }

    /// <summary>
    /// The provisional values a pending archive row was created with. At
    /// save time a field still matching its provisional value is replaced
    /// by the parsed one, while a field the user edited is kept.
    /// </summary>
    private ArchiveProvisionalValues? _pendingArchiveValues = default;
    public ArchiveProvisionalValues? PendingArchiveValues { get => _pendingArchiveValues; set { EnsureCanMutate?.Invoke(); _pendingArchiveValues = value; } }

    /// <summary>
    /// Parsed IP.BIN metadata. May be null if not yet loaded.
    /// </summary>
    private IpBin? _ip = default;
    public IpBin? Ip { get => _ip; set { EnsureCanMutate?.Invoke(); _ip = value; } }

    /// <summary>
    /// Full paths to all disc image files (e.g., .ccd + .img + .sub).
    /// </summary>
    private IList<string>? _imageFiles;
    public IList<string> ImageFiles
    {
        get => _imageFiles ??= new GuardedImageCollection(() => EnsureCanMutate?.Invoke());
        set { EnsureCanMutate?.Invoke(); _imageFiles = new GuardedImageCollection(() => EnsureCanMutate?.Invoke(), value); }
    }

    /// <summary>
    /// Full path to the game's folder on the SD card (e.g., "H:\02").
    /// Empty if the item is not on the SD card.
    /// </summary>
    private string _fullFolderPath = string.Empty;
    public string FullFolderPath { get => _fullFolderPath; set { EnsureCanMutate?.Invoke(); _fullFolderPath = value; } }

    /// <summary>
    /// Source path for items being added from PC (not yet on SD card).
    /// </summary>
    private string _sourcePath = string.Empty;
    public string SourcePath { get => _sourcePath; set { EnsureCanMutate?.Invoke(); _sourcePath = value; } }

    /// <summary>
    /// Total size of all disc image files in bytes.
    /// </summary>
    private long _length;
    public long Length
    {
        get => _length;
        set { EnsureCanMutate?.Invoke(); if (_length != value) { _length = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Region string from IP.BIN (e.g., "JTU", "E").
    /// </summary>
    private string _region = string.Empty;
    public string Region { get => _region; set { EnsureCanMutate?.Invoke(); _region = value; } }

    /// <summary>
    /// Version string from IP.BIN (e.g., "1.000").
    /// </summary>
    private string _version = string.Empty;
    public string Version { get => _version; set { EnsureCanMutate?.Invoke(); _version = value; } }

    /// <summary>
    /// Release date from IP.BIN (e.g., "19960308").
    /// </summary>
    private string _releaseDate = string.Empty;
    public string ReleaseDate { get => _releaseDate; set { EnsureCanMutate?.Invoke(); _releaseDate = value; } }

    /// <summary>
    /// Whether this item is the menu system (folder 01) and should be locked.
    /// </summary>
    public bool IsMenuItem => SdNumber == Constants.MenuFolderNumber && !IsLegacyRmenu;

    /// <summary>
    /// Whether this item is a legacy RMENU instance (movable, not locked to position 01).
    /// </summary>
    private bool _isLegacyRmenu = default;
    public bool IsLegacyRmenu { get => _isLegacyRmenu; set { EnsureCanMutate?.Invoke(); _isLegacyRmenu = value; } }

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
    private bool _needsMetadataScan = default;
    public bool NeedsMetadataScan { get => _needsMetadataScan; set { EnsureCanMutate?.Invoke(); _needsMetadataScan = value; } }

    /// <summary>
    /// Set when the user edits the Product ID through the UI.
    /// Only games with this flag get their disc images patched on save.
    /// </summary>
    private bool _productIdDirty = default;
    public bool ProductIdDirty { get => _productIdDirty; set { EnsureCanMutate?.Invoke(); _productIdDirty = value; } }

    /// <summary>
    /// Set when any sidecar-backed property is modified through the UI
    /// (Name, Folder, ProductId, Disc, AlternativeFolders).
    /// Only games with this flag get their sidecar files rewritten on save.
    /// </summary>
    private bool _sidecarsDirty = default;
    public bool SidecarsDirty { get => _sidecarsDirty; set { EnsureCanMutate?.Invoke(); _sidecarsDirty = value; } }

    /// <summary>
    /// Whether the current search text matches Name or ProductId.
    /// Transient row highlight state, never saved.
    /// </summary>
    public bool IsMatch
    {
        get => _isMatch;
        set { if (_isMatch != value) { _isMatch = value; OnPropertyChanged(); } }
    }

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
        if (NotifyObservers == null) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        else NotifyObservers(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)));
    }
}

/// <summary>
/// Snapshot of the values a pending archive row was created with.
/// </summary>
public sealed class ArchiveProvisionalValues
{
    public string Name { get; init; } = string.Empty;
    public string ProductId { get; init; } = string.Empty;
    public string Disc { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ReleaseDate { get; init; } = string.Empty;
}
