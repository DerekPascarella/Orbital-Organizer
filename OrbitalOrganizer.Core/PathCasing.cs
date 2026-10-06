namespace OrbitalOrganizer.Core;

public static class PathCasing
{
    // Maps a path to the file's real spelling on disk, so "Rhea.ini" also finds "rhea.ini"
    // on a case-sensitive filesystem. The exact name always wins. The folder is only
    // scanned when it is absent, and an ambiguous or unreadable folder returns the path as is.
    public static string ResolveActualPath(string path)
    {
        if (File.Exists(path))
            return path;

        try
        {
            var folder = Path.GetDirectoryName(path);
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || !Directory.Exists(folder))
                return path;

            string? match = null;
            foreach (var candidate in Directory.EnumerateFiles(folder))
            {
                if (!Path.GetFileName(candidate).Equals(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match != null)
                    return path;
                match = candidate;
            }

            return match ?? path;
        }
        catch (IOException)
        {
            return path;
        }
        catch (UnauthorizedAccessException)
        {
            return path;
        }
    }

    public static bool FileExistsIgnoreCase(string path) => File.Exists(ResolveActualPath(path));
}
