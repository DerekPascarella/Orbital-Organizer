using System.Text.Json.Serialization;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

internal sealed class CardSaveRow
{
    [JsonRequired]
    public string Name { get; set; } = "";
    [JsonRequired]
    public string Folder { get; set; } = "";
    [JsonRequired]
    public string ProductId { get; set; } = "";
    [JsonRequired]
    public string Disc { get; set; } = "";
    [JsonRequired]
    public string Region { get; set; } = "";
    [JsonRequired]
    public string Version { get; set; } = "";
    [JsonRequired]
    public string ReleaseDate { get; set; } = "";
    [JsonRequired]
    public List<string> AlternativeFolders { get; set; } = new();
    [JsonRequired]
    public int SdNumber { get; set; }
    [JsonRequired]
    public string FullFolderPath { get; set; } = "";
    [JsonRequired]
    public WorkMode WorkMode { get; set; }
    [JsonRequired]
    public string SourcePath { get; set; } = "";
    [JsonRequired]
    public List<string> ImageFiles { get; set; } = new();
    [JsonRequired]
    public FileFormat FileFormat { get; set; }
    [JsonRequired]
    public FileFormat? InnerFileFormat { get; set; }
    [JsonRequired]
    public ArchiveEntryInfo? SelectedArchiveEntry { get; set; }
    [JsonRequired]
    public bool IsArchiveMetadataPending { get; set; }
    [JsonRequired]
    public ArchiveProvisionalValues? PendingArchiveValues { get; set; }
    [JsonRequired]
    public IpBin? Ip { get; set; }
    [JsonRequired]
    public long Length { get; set; }
    [JsonRequired]
    public bool IsLegacyRmenu { get; set; }
    [JsonRequired]
    public bool NeedsMetadataScan { get; set; }
    [JsonRequired]
    public bool ProductIdDirty { get; set; }
    [JsonRequired]
    public bool SidecarsDirty { get; set; }

    internal static CardSaveRow Capture(SaturnGame game) => new()
    {
        Name = game.Name,
        Folder = game.Folder,
        ProductId = game.ProductId,
        Disc = game.Disc,
        Region = game.Region,
        Version = game.Version,
        ReleaseDate = game.ReleaseDate,
        AlternativeFolders = game.AlternativeFolders.ToList(),
        SdNumber = game.SdNumber,
        FullFolderPath = game.FullFolderPath,
        WorkMode = game.WorkMode,
        SourcePath = game.SourcePath,
        ImageFiles = game.ImageFiles.ToList(),
        FileFormat = game.FileFormat,
        InnerFileFormat = game.InnerFileFormat,
        SelectedArchiveEntry = game.SelectedArchiveEntry,
        IsArchiveMetadataPending = game.IsArchiveMetadataPending,
        PendingArchiveValues = game.PendingArchiveValues,
        Ip = CopyIp(game.Ip),
        Length = game.Length,
        IsLegacyRmenu = game.IsLegacyRmenu,
        NeedsMetadataScan = game.NeedsMetadataScan,
        ProductIdDirty = game.ProductIdDirty,
        SidecarsDirty = game.SidecarsDirty,
    };

    internal SaturnGame Create(string id) => new()
    {
        SaveId = id,
        Name = Name,
        Folder = Folder,
        ProductId = ProductId,
        Disc = Disc,
        Region = Region,
        Version = Version,
        ReleaseDate = ReleaseDate,
        AlternativeFolders = AlternativeFolders.ToList(),
        SdNumber = SdNumber,
        FullFolderPath = FullFolderPath,
        WorkMode = WorkMode,
        SourcePath = SourcePath,
        ImageFiles = ImageFiles.ToList(),
        FileFormat = FileFormat,
        InnerFileFormat = InnerFileFormat,
        SelectedArchiveEntry = SelectedArchiveEntry,
        IsArchiveMetadataPending = IsArchiveMetadataPending,
        PendingArchiveValues = PendingArchiveValues,
        Ip = CopyIp(Ip),
        Length = Length,
        IsLegacyRmenu = IsLegacyRmenu,
        NeedsMetadataScan = NeedsMetadataScan,
        ProductIdDirty = ProductIdDirty,
        SidecarsDirty = SidecarsDirty,
    };

    private static IpBin? CopyIp(IpBin? ip) => ip == null ? null : new()
    {
        Title = ip.Title,
        ProductId = ip.ProductId,
        Version = ip.Version,
        ReleaseDate = ip.ReleaseDate,
        Region = ip.Region,
        Disc = ip.Disc,
        HardwareId = ip.HardwareId,
        IsDefault = ip.IsDefault,
        HeaderOffset = ip.HeaderOffset,
    };

    internal bool SameSource(CardSaveRow other) => SourcePath == other.SourcePath && FileFormat == other.FileFormat &&
        InnerFileFormat == other.InnerFileFormat && ImageFiles.SequenceEqual(other.ImageFiles) &&
        SelectedArchiveEntry?.Ordinal == other.SelectedArchiveEntry?.Ordinal &&
        SelectedArchiveEntry?.FullName == other.SelectedArchiveEntry?.FullName &&
        SelectedArchiveEntry?.Size == other.SelectedArchiveEntry?.Size;
}
