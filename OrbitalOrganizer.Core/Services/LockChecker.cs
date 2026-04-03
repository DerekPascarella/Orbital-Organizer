namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Checks if files and folders on the SD card are accessible (not locked by other processes)
/// before performing save operations.
/// </summary>
public static class LockChecker
{
    /// <summary>
    /// Tests if a file can be opened with write access.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    public static string? CheckFileAccessibility(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (info.IsReadOnly)
                info.IsReadOnly = false;

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Tests if a directory can be renamed by doing a temporary rename and reverting.
    /// This catches cases where File Explorer or another process has the folder open.
    /// </summary>
    public static string? CheckDirectoryCanBeRenamed(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            return null;

        string tempName = dirPath + "_lockcheck_" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            Directory.Move(dirPath, tempName);
            Directory.Move(tempName, dirPath);
            return null;
        }
        catch (Exception ex)
        {
            // Try to move it back if the first move succeeded
            if (Directory.Exists(tempName) && !Directory.Exists(dirPath))
            {
                try { Directory.Move(tempName, dirPath); } catch { }
            }
            return ex.Message;
        }
    }

    /// <summary>
    /// Recursively checks all files in a directory for write accessibility.
    /// </summary>
    public static Dictionary<string, string> CheckDirectoryAccessibility(string dirPath)
    {
        var locked = new Dictionary<string, string>();
        if (!Directory.Exists(dirPath))
            return locked;

        foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            var error = CheckFileAccessibility(file);
            if (error != null)
                locked[file] = error;
        }

        return locked;
    }

    /// <summary>
    /// Checks all paths that will be modified during a save operation.
    /// Returns a dictionary of locked paths and their error messages, or empty if all clear.
    /// </summary>
    public static async Task<Dictionary<string, string>> CheckPathsAsync(
        IEnumerable<string> paths, IProgress<(int current, int total, string name)>? progress = null)
    {
        var locked = new Dictionary<string, string>();
        var pathList = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        int processed = 0;

        foreach (var path in pathList)
        {
            progress?.Report((processed, pathList.Count, Path.GetFileName(path)));

            await Task.Run(() =>
            {
                if (File.Exists(path))
                {
                    var error = CheckFileAccessibility(path);
                    if (error != null)
                        locked[path] = error;
                }
                else if (Directory.Exists(path))
                {
                    var dirError = CheckDirectoryCanBeRenamed(path);
                    if (dirError != null)
                    {
                        locked[path] = dirError;
                    }
                    else
                    {
                        var fileErrors = CheckDirectoryAccessibility(path);
                        foreach (var kvp in fileErrors)
                            locked[kvp.Key] = kvp.Value;
                    }
                }
            });

            processed++;
        }

        return locked;
    }
}
