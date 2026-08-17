using OrbitalOrganizer.Core.Models;
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

    // Every method that assigns or resolves ordinals must count entries
    // with this exact predicate, or stored ordinals point at the wrong
    // entry when the lists diverge.
    private static bool IsCountedEntry(IArchiveEntry entry)
    {
        return !entry.IsDirectory && !string.IsNullOrWhiteSpace(entry.Key) && entry.Size >= 0;
    }

    /// <summary>
    /// Lists the archive's file entries without extracting anything.
    /// Ordinals index the counted entries in enumeration order.
    /// </summary>
    public static IReadOnlyList<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
    {
        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.Open(stream);
        return archive.Entries
            .Where(IsCountedEntry)
            .Select((entry, ordinal) => new ArchiveEntryInfo(
                ordinal,
                entry.Key!,
                entry.Size))
            .ToArray();
    }

    // Decompression-work ceiling for one bounded read inside a solid
    // archive (bytes stored before the entry plus the prefix itself).
    private const long MaxSolidReadWorkBytes = 128L * 1024 * 1024;

    /// <summary>
    /// Reads the first min(entry size, maxBytes) bytes of a single entry
    /// without extracting it. Returns null when the entry cannot be read
    /// in a bounded way (solid RAR) or no longer matches its identity.
    /// </summary>
    public static byte[]? ReadArchiveEntryBytes(
        string archivePath,
        ArchiveEntryInfo requestedEntry,
        long maxBytes)
    {
        if (string.IsNullOrEmpty(archivePath) || requestedEntry == null || maxBytes <= 0)
            return null;

        try
        {
            using var stream = File.OpenRead(archivePath);
            using var archive = ArchiveFactory.Open(stream);

            // Reaching one entry of a solid RAR decompresses the whole stream.
            if (archive.Type == ArchiveType.Rar && archive.IsSolid)
                return null;

            var entries = archive.Entries.Where(IsCountedEntry).ToList();
            var entry = entries.ElementAtOrDefault(requestedEntry.Ordinal);

            if (entry == null ||
                entry.Key == null ||
                entry.Size != requestedEntry.Size ||
                !ArchiveEntryPath.HasSameIdentityKey(entry.Key, requestedEntry.FullName))
                return null;

            long expectedBytes = Math.Min(requestedEntry.Size, maxBytes);

            // Reaching an entry inside a solid block first decompresses
            // everything stored before it. The byte cap alone does not
            // bound that work, so the read is skipped when it would exceed
            // the budget. SharpCompress only reports IsSolid for RAR, so
            // 7z is treated as solid outright (7-Zip archives normally are).
            if (archive.Type == ArchiveType.SevenZip || archive.IsSolid)
            {
                long precedingBytes = entries.Take(requestedEntry.Ordinal).Sum(e => e.Size);
                if (precedingBytes + expectedBytes > MaxSolidReadWorkBytes)
                    return null;
            }

            using var entryStream = entry.OpenEntryStream();
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (output.Length < expectedBytes)
            {
                int chunk = (int)Math.Min(
                    buffer.Length,
                    expectedBytes - output.Length);
                int read = entryStream.Read(buffer, 0, chunk);
                if (read <= 0)
                    break;

                output.Write(buffer, 0, read);
            }

            return output.Length == expectedBytes
                ? output.ToArray()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts one disc image's file set flat into the target directory,
    /// validating each entry's identity against the list captured at add
    /// time. Returns the extracted path of the selected entry.
    /// </summary>
    public static string ExtractArchiveForEntry(
        string archivePath,
        string extractTo,
        ArchiveEntryInfo selectedEntry)
    {
        if (selectedEntry == null)
            throw new ArgumentNullException(nameof(selectedEntry));

        using var stream = File.OpenRead(archivePath);
        using var archive = ArchiveFactory.Open(stream);
        var descriptors = archive.Entries
            .Where(IsCountedEntry)
            .Select((entry, ordinal) => new ArchiveEntryInfo(
                ordinal,
                entry.Key!,
                entry.Size))
            .ToArray();
        var extractionEntries = ArchiveEntrySelection.SelectForFlatExtraction(descriptors, selectedEntry);

        Directory.CreateDirectory(extractTo);

        // The reader does not replay archive.Entries in order (SharpCompress
        // regroups 7z entries by solid block), so wanted entries are matched
        // by identity instead of position.
        var remaining = extractionEntries.ToList();
        using (var reader = archive.ExtractAllEntries())
        {
            while (remaining.Count > 0 && reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || string.IsNullOrWhiteSpace(reader.Entry.Key))
                    continue;

                // A streamed zip's local header can report size 0 (the real
                // size arrives in the trailing data descriptor), so the size
                // only disqualifies a match when the reader knows it.
                int index = remaining.FindIndex(expected =>
                    ArchiveEntryPath.HasSameIdentityKey(reader.Entry.Key, expected.FullName) &&
                    (reader.Entry.Size == expected.Size || reader.Entry.Size <= 0));
                if (index < 0)
                    continue;

                var expected = remaining[index];
                remaining.RemoveAt(index);

                string outputPath = Path.Combine(
                    extractTo,
                    ArchiveEntryPath.GetLeafName(expected.FullName));
                using (var output = new FileStream(
                    outputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                {
                    reader.WriteEntryTo(output);
                }

                if (new FileInfo(outputPath).Length != expected.Size)
                    throw new InvalidDataException("An archive entry was not extracted completely.");
            }
        }

        if (remaining.Count > 0)
            throw new InvalidDataException("One or more archive entries were not extracted.");

        return Path.Combine(extractTo, ArchiveEntryPath.GetLeafName(selectedEntry.FullName));
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
