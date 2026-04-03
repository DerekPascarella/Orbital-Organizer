using System.Text;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Patches the Product ID field in a disc image's IP.BIN header.
/// </summary>
public static class ProductIdPatcher
{
    /// <summary>
    /// Writes a new Product ID at the IP.BIN header location.
    /// Product ID is 10 bytes at offset +32 from the "SEGA SEGASATURN " magic.
    /// </summary>
    public static void PatchProductId(string imageFilePath, long headerOffset, string newProductId)
    {
        string padded = newProductId.PadRight(Constants.IpLengthProductId)[..Constants.IpLengthProductId];
        byte[] productIdBytes = Encoding.ASCII.GetBytes(padded);

        long patchOffset = headerOffset + Constants.IpOffsetProductId;

        using var fs = new FileStream(imageFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        if (fs.Length < patchOffset + productIdBytes.Length)
            throw new InvalidOperationException("Offset is outside valid range for patching.");

        fs.Position = patchOffset;
        fs.Write(productIdBytes, 0, productIdBytes.Length);
    }

    public static void PatchBytes(string filePath, long offset, byte[] data)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        if (fs.Length < offset + data.Length)
            throw new InvalidOperationException("Offset is outside valid range for patching.");

        fs.Position = offset;
        fs.Write(data, 0, data.Length);
    }
}
