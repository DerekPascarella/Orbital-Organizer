namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Keeps the card root's physical directory order matching numeric folder order.
/// The Rhea/Phoebe firmware resolves a game number by scanning the root and taking
/// the first entry whose name begins with those digits, so a folder named "420"
/// stored ahead of "42" makes game 42 boot game 420 instead.
/// </summary>
public static class CardOrder
{
    /// <summary>
    /// Folder that games are staged in while the root is rebuilt in order.
    /// </summary>
    public static string StagingPath(string cardPath) =>
        Path.Combine(cardPath, Constants.TempFolderName);

    /// <summary>
    /// Top level folders with numeric names, in the order the filesystem stores
    /// them. The result is never sorted, since that order is what needs checking.
    /// </summary>
    public static List<string> NumberedFolders(string cardPath)
    {
        var names = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(cardPath))
        {
            string name = Path.GetFileName(dir);
            if (int.TryParse(name, out _))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// Whether the names are stored in strictly ascending numeric order. A rotated
    /// sequence fails here, which is the case the reorder retry exists to catch.
    /// </summary>
    public static bool IsAscending(IReadOnlyList<string> names)
    {
        for (int i = 1; i < names.Count; i++)
        {
            if (int.Parse(names[i]) <= int.Parse(names[i - 1]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the card root is already stored in ascending order.
    /// </summary>
    public static bool IsCardOrdered(string cardPath) =>
        IsAscending(NumberedFolders(cardPath));

    /// <summary>
    /// Returns folders left in the staging area by an interrupted save. Each folder
    /// was staged under its final number, so it goes back under the name it carries
    /// and no manifest is needed.
    /// </summary>
    /// <returns>The number of folders moved back.</returns>
    public static int RecoverStaged(string cardPath)
    {
        string staging = StagingPath(cardPath);

        if (!Directory.Exists(staging))
            return 0;

        int moved = 0;

        foreach (var dir in Directory.EnumerateDirectories(staging).ToList())
        {
            string destination = Path.Combine(cardPath, Path.GetFileName(dir));

            if (Directory.Exists(destination))
                continue;

            Directory.Move(dir, destination);
            moved++;
        }

        if (!Directory.EnumerateFileSystemEntries(staging).Any())
            Directory.Delete(staging);

        return moved;
    }
}
