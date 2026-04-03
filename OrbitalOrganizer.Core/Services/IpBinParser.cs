using System.Runtime.InteropServices;
using System.Text;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Parses SEGA Saturn IP.BIN headers from disc image files.
/// </summary>
public static class IpBinParser
{
    /// <summary>
    /// Searches for the "SEGA SEGASATURN " magic pattern in a disc image file
    /// and returns the byte offset where it was found, or -1 if not found.
    /// </summary>
    // Saturn IP.BIN is always near the start of the disc image (within the
    // first few sectors for IMG/ISO/MDF, or after CDI metadata for DiscJuggler
    // images). The highest observed offset is ~345KB for CDI files. Capping
    // the search at 1MB avoids scanning hundreds of megabytes on non-Saturn
    // disc images (audio CDs, CD+G, VCDs, etc.).
    private const int MaxSearchBytes = 1024 * 1024;

    public static long FindIpBinStart(string filePath)
    {
        if (!File.Exists(filePath))
            return -1;

        var pattern = Constants.SaturnMagic;
        int patternLength = pattern.Length;
        byte[] buffer = new byte[MaxSearchBytes];

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int bytesRead = fs.Read(buffer, 0, MaxSearchBytes);
        if (bytesRead == 0) return -1;

        int pos = FindPattern(buffer, bytesRead, pattern);
        return pos >= 0 ? pos : -1;
    }

    /// <summary>
    /// Searches for IP.BIN across all disc image files in a folder.
    /// Returns the offset and file path where found, or (-1, null) if not found.
    /// </summary>
    public static (long offset, string? filePath) FindIpBinInFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return (-1, null);

        foreach (var file in Directory.GetFiles(folderPath))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();

            // CHD support is Windows-only
            if (ext == ".chd" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var ip = ParseHeaderFromChd(file);
                if (ip != null)
                    return (0, file);
                continue;
            }

            if (Constants.DiscImageExtensions.Contains(ext) ||
                ext == ".ccd")
            {
                // For CCD format, the IP.BIN data lives in the .img file
                string targetFile = file;
                if (ext == ".ccd")
                {
                    targetFile = Path.ChangeExtension(file, ".img");
                    if (!File.Exists(targetFile)) continue;
                }

                long offset = FindIpBinStart(targetFile);
                if (offset >= 0)
                    return (offset, targetFile);
            }
        }

        return (-1, null);
    }

    /// <summary>
    /// Parses IP.BIN metadata from a CHD file via libchdr.
    /// </summary>
    public static IpBin? ParseHeaderFromChd(string chdPath)
    {
        try
        {
            using var chd = new ChdReader(chdPath);
            byte[] sectorData = chd.GetIpBin();

            // Find the "SEGA SEGASATURN" magic string in the raw sector data.
            int magicOffset = FindPattern(sectorData, sectorData.Length, Constants.SaturnMagic);
            if (magicOffset < 0)
                return null;

            return ParseHeaderFromBytes(sectorData, magicOffset);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses IP.BIN metadata from a raw byte array at the given magic offset.
    /// </summary>
    public static IpBin ParseHeaderFromBytes(byte[] data, int magicOffset)
    {
        var ip = new IpBin { HeaderOffset = magicOffset };

        ip.Title = ReadAsciiFieldFromBytes(data, magicOffset + Constants.IpOffsetTitle, Constants.IpLengthTitle);

        // Combined Product ID + Version parsing (same logic as ParseHeader)
        (ip.ProductId, ip.Version) = ParseProductIdAndVersionFromBytes(data, magicOffset + Constants.IpOffsetProductId);

        ip.ReleaseDate = ReadAsciiFieldFromBytes(data, magicOffset + Constants.IpOffsetReleaseDate, Constants.IpLengthReleaseDate);

        string deviceInfo = ReadAsciiFieldFromBytes(data, magicOffset + Constants.IpOffsetDeviceInfo, Constants.IpLengthDeviceInfo);
        ip.Disc = ExtractDiscNumber(deviceInfo);

        ip.Region = ReadAsciiFieldFromBytes(data, magicOffset + Constants.IpOffsetRegion, Constants.IpLengthRegion);
        ip.HardwareId = ReadAsciiFieldFromBytes(data, magicOffset + Constants.IpOffsetHardwareId, Constants.IpLengthHardwareId);

        return ip;
    }

    private static string ReadAsciiFieldFromBytes(byte[] data, int offset, int length)
    {
        if (data.Length < offset + length)
            return string.Empty;

        return Encoding.ASCII.GetString(data, offset, length).Trim();
    }

    private static (string ProductId, string Version) ParseProductIdAndVersionFromBytes(byte[] data, int offset)
    {
        const int combinedLength = 16;

        if (data.Length < offset + combinedLength)
            return (string.Empty, "NA");

        string combined = Encoding.ASCII.GetString(data, offset, combinedLength);

        for (int i = 0; i < combined.Length - 1; i++)
        {
            if (combined[i] == 'V' && char.IsAsciiDigit(combined[i + 1]))
            {
                string pid = combined[..i].Trim();
                string ver = combined[i..].Trim();
                return (pid, string.IsNullOrEmpty(ver) ? "NA" : ver);
            }
        }

        string pidFallback = combined[..Constants.IpLengthProductId].Trim();
        string verFallback = combined[Constants.IpLengthProductId..].Trim();

        if (!string.IsNullOrEmpty(verFallback) && !verFallback.Contains('.'))
        {
            return (combined.Trim(), "NA");
        }

        return (pidFallback, string.IsNullOrEmpty(verFallback) ? "NA" : verFallback);
    }

    /// <summary>
    /// Parses IP.BIN metadata from a disc image at a known header offset.
    /// </summary>
    public static IpBin ParseHeader(string filePath, long headerOffset)
    {
        var ip = new IpBin { HeaderOffset = headerOffset };

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        ip.Title = ReadAsciiField(fs, headerOffset + Constants.IpOffsetTitle, Constants.IpLengthTitle);

        // The Product ID (10 bytes at +32) and Version (6 bytes at +42)
        // fields often bleed into each other. Some discs have short
        // Product IDs where the version's "V" prefix lands inside the
        // Product ID field (e.g., "MK-81204V1.004  "), and one disc
        // has an 11-char Product ID that overflows into the version
        // field ("T-11308H-50 V1.0"). To handle all cases, read the
        // combined 16 bytes and split on the first "V" followed by a
        // digit, which never appears in a legitimate Product ID.
        (ip.ProductId, ip.Version) = ParseProductIdAndVersion(fs, headerOffset + Constants.IpOffsetProductId);

        ip.ReleaseDate = ReadAsciiField(fs, headerOffset + Constants.IpOffsetReleaseDate, Constants.IpLengthReleaseDate);

        string deviceInfo = ReadAsciiField(fs, headerOffset + Constants.IpOffsetDeviceInfo, Constants.IpLengthDeviceInfo);
        ip.Disc = ExtractDiscNumber(deviceInfo);

        ip.Region = ReadAsciiField(fs, headerOffset + Constants.IpOffsetRegion, Constants.IpLengthRegion);
        ip.HardwareId = ReadAsciiField(fs, headerOffset + Constants.IpOffsetHardwareId, Constants.IpLengthHardwareId);

        return ip;
    }

    /// <summary>
    /// Reads raw bytes at a given offset in a file.
    /// </summary>
    public static byte[] ReadBytesAtOffset(string filePath, long offset, int count)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < offset + count)
            throw new InvalidOperationException("Offset is outside valid range.");

        fs.Position = offset;
        byte[] buffer = new byte[count];
        fs.ReadExactly(buffer, 0, count);
        return buffer;
    }

    /// <summary>
    /// Reads the combined Product ID + Version span (16 bytes) and splits
    /// them using "V" + digit as the version boundary.
    /// </summary>
    private static (string ProductId, string Version) ParseProductIdAndVersion(FileStream fs, long offset)
    {
        const int combinedLength = 16; // 10 (PID) + 6 (Version)

        if (fs.Length < offset + combinedLength)
            return (string.Empty, "NA");

        fs.Position = offset;
        byte[] buffer = new byte[combinedLength];
        fs.ReadExactly(buffer, 0, combinedLength);
        string combined = Encoding.ASCII.GetString(buffer);

        // Find the first "V" followed by a digit in the combined span.
        for (int i = 0; i < combined.Length - 1; i++)
        {
            if (combined[i] == 'V' && char.IsAsciiDigit(combined[i + 1]))
            {
                string pid = combined[..i].Trim();
                string ver = combined[i..].Trim();
                return (pid, string.IsNullOrEmpty(ver) ? "NA" : ver);
            }
        }

        // No "V" + digit found. Fall back to spec boundaries, but guard
        // against product IDs that overflow into the version field. A real
        // version string always contains a dot (e.g., "1.000", "V.001").
        // If the positional split produces a "version" without a dot, it's
        // actually the tail end of the product ID, not a version.
        string pidFallback = combined[..Constants.IpLengthProductId].Trim();
        string verFallback = combined[Constants.IpLengthProductId..].Trim();

        if (!string.IsNullOrEmpty(verFallback) && !verFallback.Contains('.'))
        {
            // Not a real version string. Treat the full span as Product ID.
            return (combined.Trim(), "NA");
        }

        return (pidFallback, string.IsNullOrEmpty(verFallback) ? "NA" : verFallback);
    }

    private static string ReadAsciiField(FileStream fs, long offset, int length)
    {
        if (fs.Length < offset + length)
            return string.Empty;

        fs.Position = offset;
        byte[] buffer = new byte[length];
        fs.ReadExactly(buffer, 0, length);
        return Encoding.ASCII.GetString(buffer).Trim();
    }

    /// <summary>
    /// Extracts disc number from the device info field. "CD-1/1 " becomes "1/1".
    /// </summary>
    private static string ExtractDiscNumber(string deviceInfo)
    {
        int cdIndex = deviceInfo.IndexOf("CD-", StringComparison.OrdinalIgnoreCase);
        if (cdIndex >= 0)
        {
            string discPart = deviceInfo[(cdIndex + 3)..].Trim();
            if (!string.IsNullOrEmpty(discPart) && !discPart.Contains("CART", StringComparison.OrdinalIgnoreCase))
                return discPart;
        }

        return "1/1";
    }

    private static int FindPattern(byte[] buffer, int bufferLength, byte[] pattern)
    {
        int patternLength = pattern.Length;
        int limit = bufferLength - patternLength;

        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < patternLength; j++)
            {
                if (buffer[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }

        return -1;
    }
}
