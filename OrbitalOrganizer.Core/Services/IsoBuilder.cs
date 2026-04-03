using System.Text;
using DiscUtils.Iso9660;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Builds RMENU.iso disc images with a standard ISO 9660 layout
/// and the SEGA Saturn system area / boot sector.
/// </summary>
public static class IsoBuilder
{
    public static void BuildRmenuIso(string contentDirectory, string outputIsoPath, string ipBinPath)
    {
        var builder = new CDBuilder
        {
            UseJoliet = false,
            VolumeIdentifier = Constants.IsoVolumeId
        };

        foreach (var file in Directory.GetFiles(contentDirectory))
        {
            string fileName = Path.GetFileName(file).ToUpperInvariant();
            if (fileName == "IP.BIN") continue;
            builder.AddFile(fileName, file);
        }

        using var isoStream = builder.Build();

        byte[] ipBinData = File.ReadAllBytes(ipBinPath);

        // System area = first 16 sectors (32768 bytes). DiscUtils leaves it zeroed,
        // so we overwrite it with the actual IP.BIN boot sector data.
        byte[] isoData = new byte[isoStream.Length];
        isoStream.Position = 0;
        isoStream.ReadExactly(isoData, 0, isoData.Length);

        int copyLength = Math.Min(ipBinData.Length, 32768);
        Array.Copy(ipBinData, 0, isoData, 0, copyLength);

        // Patch the Primary Volume Descriptor fields that CDBuilder doesn't expose.
        // PVD starts at sector 16 (offset 32768).
        PatchVolumeDescriptor(isoData);

        // Patch DiscUtils quirks (missing ";1" suffixes, bogus Joliet SVD).
        PostProcessIso(isoData);

        using var outputStream = new FileStream(outputIsoPath, FileMode.Create, FileAccess.Write);
        outputStream.Write(isoData, 0, isoData.Length);
    }

    /// <summary>
    /// Patches ISO 9660 Primary Volume Descriptor fields at sector 16.
    /// CDBuilder only sets Volume Identifier; we fill in the rest.
    /// </summary>
    private static void PatchVolumeDescriptor(byte[] isoData)
    {
        const int pvdOffset = 32768; // Sector 16

        // System Identifier: bytes 8-39 (32 bytes, padded with spaces)
        WriteAsciiField(isoData, pvdOffset + 8, 32, Constants.IsoSystemId);

        // Volume Set Identifier: bytes 190-317 (128 bytes)
        WriteAsciiField(isoData, pvdOffset + 190, 128, Constants.IsoVolumeSetId);

        // Publisher Identifier: bytes 318-445 (128 bytes)
        WriteAsciiField(isoData, pvdOffset + 318, 128, Constants.IsoPublisher);

        // Data Preparer Identifier: bytes 446-573 (128 bytes)
        WriteAsciiField(isoData, pvdOffset + 446, 128, Constants.IsoPreparer);

        // Application Identifier: bytes 574-701 (128 bytes)
        WriteAsciiField(isoData, pvdOffset + 574, 128, Constants.IsoApplicationId);

        // Copyright File Identifier: bytes 702-738 (37 bytes)
        WriteAsciiField(isoData, pvdOffset + 702, 37, "CPY.TXT");

        // Abstract File Identifier: bytes 739-775 (37 bytes)
        WriteAsciiField(isoData, pvdOffset + 739, 37, "ABS.TXT");

        // Bibliographic File Identifier: bytes 776-812 (37 bytes)
        WriteAsciiField(isoData, pvdOffset + 776, 37, "BIB.TXT");
    }

    private static void WriteAsciiField(byte[] data, int offset, int fieldLength, string value)
    {
        byte[] padded = new byte[fieldLength];
        Array.Fill(padded, (byte)0x20); // Space-fill per ISO 9660 spec
        byte[] ascii = Encoding.ASCII.GetBytes(value);
        Array.Copy(ascii, 0, padded, 0, Math.Min(ascii.Length, fieldLength));
        Array.Copy(padded, 0, data, offset, fieldLength);
    }

    /// <summary>
    /// DiscUtils 0.16.13 CDBuilder has two bugs that trip up rmenu_legacy:
    /// filenames lack the ";1" version suffix, and a broken Joliet SVD gets
    /// written even with UseJoliet = false. We patch both in the raw bytes.
    /// </summary>
    private static void PostProcessIso(byte[] isoData)
    {
        AddVersionSuffixes(isoData);
        RemoveSupplementaryVolumeDescriptor(isoData);
    }

    /// <summary>
    /// Appends ";1" to every file identifier in the PVD root directory.
    /// ISO 9660 requires it and rmenu_legacy won't find files without it.
    /// </summary>
    private static void AddVersionSuffixes(byte[] isoData)
    {
        const int pvdOffset = 32768;

        // Root dir record at PVD+156. Extent location at bytes 2-5 (LE).
        int rootExtent = BitConverter.ToInt32(isoData, pvdOffset + 156 + 2);
        int rootSize = BitConverter.ToInt32(isoData, pvdOffset + 156 + 10);
        int dirOffset = rootExtent * 2048;

        var records = new List<byte[]>();
        int pos = dirOffset;

        while (pos < dirOffset + rootSize)
        {
            byte recLen = isoData[pos];
            if (recLen == 0)
                break;

            byte[] rec = new byte[recLen];
            Array.Copy(isoData, pos, rec, 0, recLen);
            pos += recLen;

            byte idLen = rec[32];

            // Skip "." (0x00) and ".." (0x01)
            if (idLen == 1 && (rec[33] == 0x00 || rec[33] == 0x01))
            {
                records.Add(rec);
                continue;
            }

            string name = Encoding.ASCII.GetString(rec, 33, idLen);
            if (name.EndsWith(";1"))
            {
                records.Add(rec);
                continue;
            }

            // Append ";1" and rebuild the directory record.
            string newName = name + ";1";
            byte[] newId = Encoding.ASCII.GetBytes(newName);
            byte newIdLen = (byte)newId.Length;

            // 33 fixed header bytes + identifier + pad byte if id length is even
            int newRecLen = 33 + newIdLen + (newIdLen % 2 == 0 ? 1 : 0);
            byte[] newRec = new byte[newRecLen];

            Array.Copy(rec, 0, newRec, 0, 32);
            newRec[0] = (byte)newRecLen;
            newRec[32] = newIdLen;
            Array.Copy(newId, 0, newRec, 33, newIdLen);

            records.Add(newRec);
        }

        // Clear the directory and write updated records back.
        Array.Clear(isoData, dirOffset, rootSize);
        int writePos = dirOffset;
        foreach (byte[] rec in records)
        {
            Array.Copy(rec, 0, isoData, writePos, rec.Length);
            writePos += rec.Length;
        }
    }

    /// <summary>
    /// DiscUtils 0.16.13 writes a Supplementary Volume Descriptor at sector 17
    /// even with UseJoliet = false. It puts ASCII where UCS-2 belongs, so Windows
    /// shows garbled CJK characters when browsing the ISO. Nuke it.
    /// </summary>
    private static void RemoveSupplementaryVolumeDescriptor(byte[] isoData)
    {
        const int svdOffset = 34816; // Sector 17

        if (isoData.Length <= svdOffset + 6)
            return;

        // Only nuke if it really is an SVD (type 0x02, "CD001" signature).
        if (isoData[svdOffset] != 0x02)
            return;
        if (isoData[svdOffset + 1] != (byte)'C' || isoData[svdOffset + 2] != (byte)'D' ||
            isoData[svdOffset + 3] != (byte)'0' || isoData[svdOffset + 4] != (byte)'0' ||
            isoData[svdOffset + 5] != (byte)'1')
            return;

        // Replace with a Volume Descriptor Set Terminator.
        Array.Clear(isoData, svdOffset, 2048);
        isoData[svdOffset] = 0xFF;
        isoData[svdOffset + 1] = (byte)'C';
        isoData[svdOffset + 2] = (byte)'D';
        isoData[svdOffset + 3] = (byte)'0';
        isoData[svdOffset + 4] = (byte)'0';
        isoData[svdOffset + 5] = (byte)'1';
        isoData[svdOffset + 6] = 0x01;
    }

    public static string PrepareRmenuContent(string toolsPath, string listIniContent, string tempDirectory, bool useRmenuKai)
    {
        string buildName = useRmenuKai ? "RMENU_BUILD_KAI" : "RMENU_BUILD_LEGACY";
        string contentDir = Path.Combine(tempDirectory, buildName);
        if (Directory.Exists(contentDir))
            Directory.Delete(contentDir, recursive: true);
        Directory.CreateDirectory(contentDir);

        string sharedDir = Path.Combine(toolsPath, "shared");
        foreach (var file in Directory.GetFiles(sharedDir))
        {
            File.Copy(file, Path.Combine(contentDir, Path.GetFileName(file)), overwrite: true);
        }

        string zeroBinSource = useRmenuKai
            ? Path.Combine(toolsPath, "rmenukai", "0.BIN")
            : Path.Combine(toolsPath, "rmenu_legacy", "0.BIN");
        File.Copy(zeroBinSource, Path.Combine(contentDir, "0.BIN"), overwrite: true);

        // No BOM; rmenu_legacy chokes on the 3 leading bytes.
        File.WriteAllText(Path.Combine(contentDir, "LIST.INI"), listIniContent, new UTF8Encoding(false));

        return contentDir;
    }
}
