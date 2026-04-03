using System.Globalization;
using System.Text;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Strips non-printable and non-ASCII characters from strings so that
/// titles, product IDs, and folder paths stay within the 0x20-0x7E range
/// that rmenu/rmenukai and FAT32 can handle safely.
/// </summary>
public static class AsciiSanitizer
{
    /// <summary>
    /// Decomposes accented characters into base + combining mark, then
    /// drops the combining marks. e.g. "cafe\u0301" -> "cafe".
    /// </summary>
    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);

        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Keeps only printable ASCII (0x20 space through 0x7E tilde).
    /// Everything else is silently dropped.
    /// </summary>
    public static string StripNonPrintableAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return new string(text.Where(c => c >= 0x20 && c <= 0x7E).ToArray());
    }

    /// <summary>
    /// Full sanitization pass for display names: diacritics removed,
    /// underscores turned to spaces, non-printable ASCII stripped,
    /// then truncated to 256 characters.
    /// </summary>
    public static string SanitizeName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.Length > 256)
            name = name[..256];

        name = RemoveDiacritics(name).Replace("_", " ").Trim();
        return StripNonPrintableAscii(name);
    }

    /// <summary>
    /// Strips non-printable ASCII from a product ID and truncates to 16 chars.
    /// Unlike GDMCM we keep hyphens since Saturn serials use them (e.g. "T-14302G").
    /// </summary>
    public static string SanitizeProductId(string productId)
    {
        if (string.IsNullOrEmpty(productId))
            return productId;

        productId = StripNonPrintableAscii(productId.Trim());
        if (productId.Length > 16)
            productId = productId[..16];

        return productId;
    }

    /// <summary>
    /// Cleans a virtual folder path: strips non-printable ASCII from each
    /// segment, enforces per-segment and total length limits.
    /// </summary>
    public static string CleanFolderPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        const int segmentMax = 256;
        const int totalMax = 512;

        var segments = path.Split('\\');

        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = StripNonPrintableAscii(segments[i].Trim());
            if (segments[i].Length > segmentMax)
                segments[i] = segments[i][..segmentMax];
        }

        var result = string.Join("\\", segments.Where(s => !string.IsNullOrEmpty(s)));

        if (result.Length > totalMax)
            result = result[..totalMax];

        return result;
    }
}
