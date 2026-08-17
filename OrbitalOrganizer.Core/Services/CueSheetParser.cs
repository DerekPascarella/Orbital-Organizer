using System.Text.RegularExpressions;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Parses CUE sheet files to extract referenced BIN/data file names.
/// </summary>
public static partial class CueSheetParser
{
    [GeneratedRegex(@"^\s*FILE\s+""([^""]+)""\s+", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FileDirectiveRegex();

    /// <summary>
    /// Reads a CUE file and returns the full paths of all referenced data files.
    /// Paths are resolved relative to the CUE file's directory.
    /// </summary>
    public static List<string> GetReferencedFiles(string cueFilePath)
    {
        var result = new List<string>();
        if (!File.Exists(cueFilePath))
            return result;

        string cueDir = Path.GetDirectoryName(cueFilePath)!;
        string cueContent = File.ReadAllText(cueFilePath);

        foreach (string referencedFile in GetReferencedFileNames(cueContent))
        {
            string fullPath = Path.Combine(cueDir, referencedFile);

            if (File.Exists(fullPath))
                result.Add(fullPath);
        }

        return result;
    }

    /// <summary>
    /// Returns the file names referenced by CUE sheet text, as written
    /// (no path resolution or existence checks).
    /// </summary>
    public static List<string> GetReferencedFileNames(string cueContent)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(cueContent))
            return result;

        foreach (Match match in FileDirectiveRegex().Matches(cueContent))
            result.Add(match.Groups[1].Value);

        return result;
    }

    /// <summary>
    /// Returns the CUE file path plus all files it references (BIN tracks).
    /// </summary>
    public static List<string> GetAllRelatedFiles(string cueFilePath)
    {
        var result = new List<string> { cueFilePath };
        result.AddRange(GetReferencedFiles(cueFilePath));
        return result;
    }
}
