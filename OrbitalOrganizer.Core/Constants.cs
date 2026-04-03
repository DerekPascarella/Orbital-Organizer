namespace OrbitalOrganizer.Core;

public static class Constants
{
    public const string Version = "2.0.0";
    public const string AppName = "Orbital Organizer";
    public const string AppDescription = "A tool to manage a Rhea/Phoebe SD card and its contents";
    public const string AppUrl = "https://github.com/DerekPascarella/Orbital-Organizer";

    // SEGA Saturn IP.BIN magic bytes: "SEGA SEGASATURN "
    public static readonly byte[] SaturnMagic = {
        0x53, 0x45, 0x47, 0x41, 0x20, 0x53, 0x45, 0x47,
        0x41, 0x53, 0x41, 0x54, 0x55, 0x52, 0x4E, 0x20
    };

    // RmenuKai detection bytes: "pskai"
    public static readonly byte[] RmenuKaiSignature = { 0x70, 0x73, 0x6B, 0x61, 0x69 };

    // Sidecar text file names
    public const string NameFile = "Name.txt";
    public const string DiscFile = "Disc.txt";
    public const string RegionFile = "Region.txt";
    public const string VersionFile = "Version.txt";
    public const string DateFile = "Date.txt";
    public const string FolderFile = "Folder.txt";
    public const string FolderAlt1File = "Folder_Alt1.txt";
    public const string FolderAlt2File = "Folder_Alt2.txt";
    public const string FolderAlt3File = "Folder_Alt3.txt";
    public const string FolderAlt4File = "Folder_Alt4.txt";
    public const string FolderAlt5File = "Folder_Alt5.txt";
    public static readonly string[] FolderAltFiles = {
        FolderAlt1File, FolderAlt2File, FolderAlt3File,
        FolderAlt4File, FolderAlt5File
    };
    public const string ProductIdFile = "ProductID.txt";

    // RMENU ISO volume descriptor fields
    public const string IsoSystemId = "SEGA SATURN";
    public const string IsoVolumeId = "RMENU";
    public const string IsoVolumeSetId = "RMENU";
    public const string IsoPublisher = "SEGA ENTERPRISES, LTD.";
    public const string IsoPreparer = "SEGA ENTERPRISES, LTD.";
    public const string IsoApplicationId = "RMENU";

    // Supported disc image extensions
    public static readonly string[] DiscImageExtensions = { ".cdi", ".mdf", ".img", ".iso" };
    public static readonly string[] CloneCdExtensions = { ".ccd" };
    public static readonly string[] CueBinExtensions = { ".cue" };
    public static readonly string[] ChdExtensions = { ".chd" };
    public static readonly string[] AllImageExtensions = { ".cdi", ".mdf", ".img", ".iso", ".ccd", ".cue" };
    public static readonly string[] ArchiveExtensions = { ".7z", ".rar", ".zip" };

    // Folder numbering
    public const int MenuFolderNumber = 1;
    public const string MenuFolderName = "01";
    public const string TempFolderName = "orbital_organizer_temp";

    // IP.BIN header offsets (relative to "SEGA SEGASATURN " magic start)
    public const int IpOffsetHardwareId = 16;
    public const int IpOffsetProductId = 32;
    public const int IpOffsetVersion = 42;
    public const int IpOffsetReleaseDate = 48;
    public const int IpOffsetDeviceInfo = 56;
    public const int IpOffsetRegion = 64;
    public const int IpOffsetTitle = 96;

    public const int IpLengthHardwareId = 16;
    public const int IpLengthProductId = 10;
    public const int IpLengthVersion = 6;
    public const int IpLengthReleaseDate = 8;
    public const int IpLengthDeviceInfo = 8;
    public const int IpLengthRegion = 10;
    public const int IpLengthTitle = 112;
}
