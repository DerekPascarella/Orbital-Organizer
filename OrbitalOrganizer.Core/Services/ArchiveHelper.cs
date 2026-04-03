using System.Runtime.InteropServices;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Handles peeking into and extracting compressed archives (.7z, .rar, .zip).
/// </summary>
public static class ArchiveHelper
{
    /// <summary>
    /// Returns true if the file extension is a supported archive format.
    /// </summary>
    public static bool IsArchive(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return Constants.ArchiveExtensions.Contains(ext);
    }

    /// <summary>
    /// Lists all files inside an archive with their uncompressed sizes.
    /// </summary>
    public static Dictionary<string, long> GetArchiveFiles(string archivePath)
    {
        var result = new Dictionary<string, long>();

        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.Open(stream);

        foreach (var entry in archive.Entries)
        {
            if (!entry.IsDirectory && entry.Key != null && !result.ContainsKey(entry.Key))
                result.Add(entry.Key, entry.Size);
        }

        return result;
    }

    /// <summary>
    /// Checks whether an archive contains at least one recognized disc image.
    /// Returns the filename of the first match, or null if none found.
    /// </summary>
    public static string? FindDiscImageInArchive(string archivePath)
    {
        var files = GetArchiveFiles(archivePath);

        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        foreach (var fileName in files.Keys)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (Constants.AllImageExtensions.Contains(ext))
                return fileName;
            if (isWindows && ext == ".chd")
                return fileName;
        }

        return null;
    }

    /// <summary>
    /// Extracts all files from an archive into the target directory.
    /// Files are extracted flat (no subdirectory structure preserved).
    /// </summary>
    public static void ExtractArchive(string archivePath, string extractTo)
    {
        Directory.CreateDirectory(extractTo);

        var options = new ExtractionOptions
        {
            ExtractFullPath = false,
            Overwrite = true
        };

        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.Open(stream);
        using var reader = archive.ExtractAllEntries();

        reader.WriteAllToDirectory(extractTo, options);
    }
}
