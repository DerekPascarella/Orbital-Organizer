namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// Result of the free space check that runs before saving to the SD card.
/// All sizes are in bytes.
/// </summary>
public class SpaceCheckResult
{
    public long TemporarySpaceNeeded { get; set; }
    public long ExternalTemporarySpaceNeeded { get; set; }
    public long TemporaryAvailableSpace { get; set; }
    public long TemporaryShortfall { get; set; }
    public string TemporaryFolderPath { get; set; } = "";
    public bool TemporarySharesCardVolume { get; set; }
    public long AvailableSpace { get; set; }
    public long SpaceToBeFreed { get; set; }
    public long NewItemsSize { get; set; }
    public long MenuSpaceNeeded { get; set; }
    public long MetadataBuffer { get; set; }
    public long TotalNeeded { get; set; }
    public long EffectiveAvailable { get; set; }
    public long Shortfall { get; set; }
    public bool HasSufficientSpace { get; set; }
    public bool ContainsEstimatedSizes { get; set; }
    public int NewItemCount { get; set; }
    public bool MenuFolderExists { get; set; }
}
