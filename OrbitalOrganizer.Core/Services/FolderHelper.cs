using System.Runtime.InteropServices;

namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Handles safe folder move operations with retry logic for locked files.
/// </summary>
public static class FolderHelper
{
    /// <summary>
    /// Moves a folder from source to destination. If the operation fails due to a file
    /// being locked by another process, calls the onLocked callback for user notification
    /// and retries. Throws on non-recoverable errors.
    /// </summary>
    /// <param name="source">Full path of source folder.</param>
    /// <param name="destination">Full path of destination folder.</param>
    /// <param name="onLocked">Callback invoked when a lock is detected. Return true to retry, false to abort.</param>
    public static async Task MoveDirectoryAsync(string source, string destination, Func<string, Task<bool>>? onLocked = null)
    {
        while (true)
        {
            try
            {
                await Task.Run(() => Directory.Move(source, destination));
                return;
            }
            catch (IOException ex) when (IsLockError(ex))
            {
                if (onLocked == null)
                    throw;

                bool retry = await onLocked(source);
                if (!retry)
                    throw;
            }
            catch (UnauthorizedAccessException)
            {
                if (onLocked == null)
                    throw;

                bool retry = await onLocked(source);
                if (!retry)
                    throw;
            }
        }
    }

    /// <summary>
    /// Generates a random 6-character alphanumeric ID for duplicate tagging.
    /// </summary>
    public static string GenerateUniqueTag()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = Random.Shared;
        return new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }

    /// <summary>
    /// Sanitizes a string for use as a folder name by replacing illegal characters.
    /// </summary>
    public static string SanitizeFolderName(string name)
    {
        // Strip characters that aren't valid in FAT32 folder names
        name = name.Replace(":", " -");
        name = name.Replace('<', '(');
        name = name.Replace('>', ')');
        name = name.Replace('\\', '-');
        name = name.Replace('/', '-');
        name = name.Replace('"', '\'');
        name = name.Replace("|", "");
        name = name.Replace("?", "");
        name = name.Replace("*", "");
        return name.Trim();
    }

    private static bool IsLockError(IOException ex)
    {
        // HResult 0x80070020 = ERROR_SHARING_VIOLATION (32)
        // HResult 0x80070005 = ERROR_ACCESS_DENIED (5)
        int hr = ex.HResult & 0xFFFF;
        return hr == 32 || hr == 5;
    }
}
