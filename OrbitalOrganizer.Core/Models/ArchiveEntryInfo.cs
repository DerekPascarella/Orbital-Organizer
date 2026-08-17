namespace OrbitalOrganizer.Core.Models;

/// <summary>
/// Carries the identity needed to reopen the same archive entry later.
/// </summary>
public sealed class ArchiveEntryInfo
{
    public ArchiveEntryInfo(int ordinal, string fullName, long size)
    {
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Archive entry name cannot be empty.", nameof(fullName));
        if (size < 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        Ordinal = ordinal;
        FullName = fullName;
        Size = size;
    }

    /// <summary>
    /// Index into the archive's non-directory entry list, in enumeration order.
    /// </summary>
    public int Ordinal { get; }

    /// <summary>
    /// Raw entry key as the archive library reported it.
    /// </summary>
    public string FullName { get; }

    /// <summary>
    /// Uncompressed size in bytes.
    /// </summary>
    public long Size { get; }
}

/// <summary>
/// Applies consistent path rules to raw keys reported by archive libraries.
/// </summary>
public static class ArchiveEntryPath
{
    /// <summary>
    /// Returns a canonical archive key without changing its case.
    /// </summary>
    public static string NormalizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Archive entry name cannot be empty.", nameof(key));

        var replaced = key.Replace('\\', '/');
        if (replaced.StartsWith("/", StringComparison.Ordinal) ||
            (replaced.Length >= 2 && replaced[1] == ':'))
            throw new ArgumentException("Archive entry name must be relative.", nameof(key));

        var segments = new List<string>();
        foreach (var segment in replaced.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;

            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new ArgumentException("Archive entry name escapes the archive root.", nameof(key));

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
            throw new ArgumentException("Archive entry name cannot be empty.", nameof(key));

        return string.Join("/", segments);
    }

    /// <summary>
    /// Returns the canonical parent key, or an empty string for a root entry.
    /// </summary>
    public static string GetDirectoryKey(string key)
    {
        var normalized = NormalizeKey(key);
        int separator = normalized.LastIndexOf('/');
        return separator < 0 ? string.Empty : normalized[..separator];
    }

    /// <summary>
    /// Returns the final component of a canonical archive key.
    /// </summary>
    public static string GetLeafName(string key)
    {
        var normalized = NormalizeKey(key);
        int separator = normalized.LastIndexOf('/');
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }

    /// <summary>
    /// Compares entry keys while allowing archive-library separator differences.
    /// </summary>
    public static bool HasSameIdentityKey(string? first, string? second)
    {
        if (first == null || second == null)
            return false;

        return string.Equals(
            first.Replace('\\', '/'),
            second.Replace('\\', '/'),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves a name referenced by a manifest entry (e.g., a CUE FILE line)
    /// against the entry list, relative to the owning entry's directory.
    /// </summary>
    public static ArchiveEntryInfo? FindRelativeEntry(
        IReadOnlyList<ArchiveEntryInfo> entries,
        ArchiveEntryInfo owner,
        string? referencedName)
    {
        if (entries == null)
            throw new ArgumentNullException(nameof(entries));
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (string.IsNullOrWhiteSpace(referencedName))
            return null;

        var reference = referencedName.Replace('\\', '/');
        if (reference.StartsWith("/", StringComparison.Ordinal) ||
            (reference.Length >= 2 && reference[1] == ':'))
            return null;

        var ownerDirectory = GetDirectoryKey(owner.FullName);
        string combined = string.IsNullOrEmpty(ownerDirectory)
            ? reference
            : ownerDirectory + "/" + reference;

        string expected;
        try
        {
            expected = NormalizeKey(combined);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return entries.FirstOrDefault(entry => string.Equals(
            NormalizeKey(entry.FullName),
            expected,
            StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Chooses the entries needed to flatten one disc image out of an archive.
/// </summary>
public static class ArchiveEntrySelection
{
    // Formats that act as the primary file of a disc image set. Data files
    // (.img, .iso, .mdf, .bin, .sub, .wav) are never excluded outright
    // because they can be companions of the selected image.
    private static readonly string[] ManifestExtensions = { ".cdi", ".ccd", ".cue", ".chd", ".mds" };

    // Self-contained image formats. When the selected image is one of
    // these, sibling standalone images belong to other games.
    private static readonly string[] StandaloneImageExtensions = { ".cdi", ".iso", ".img", ".mdf" };

    /// <summary>
    /// Returns root files and files beside the selected image, excluding
    /// other image manifests so a second game cannot leak into the output.
    /// </summary>
    public static IReadOnlyList<ArchiveEntryInfo> SelectForFlatExtraction(
        IReadOnlyList<ArchiveEntryInfo> entries,
        ArchiveEntryInfo selectedEntry)
    {
        if (entries == null)
            throw new ArgumentNullException(nameof(entries));
        if (selectedEntry == null)
            throw new ArgumentNullException(nameof(selectedEntry));

        var selected = entries.ElementAtOrDefault(selectedEntry.Ordinal);
        if (selected == null || !HasSameIdentity(selected, selectedEntry))
            throw new InvalidDataException("The selected archive entry is no longer available.");

        string selectedDirectory = ArchiveEntryPath.GetDirectoryKey(selected.FullName);
        string selectedLeaf = ArchiveEntryPath.GetLeafName(selected.FullName);
        string selectedExtension = Path.GetExtension(selectedLeaf).ToLowerInvariant();
        bool selectedIsStandalone = StandaloneImageExtensions.Contains(selectedExtension);

        var selectedByLeaf = new Dictionary<string, SelectedEntry>(
            StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry == null || entry.Ordinal != index)
                throw new InvalidDataException("The archive entry order is invalid.");

            // Entries whose keys cannot be normalized are skipped, the
            // same way the add flow skips them.
            string directory;
            try
            {
                directory = ArchiveEntryPath.GetDirectoryKey(entry.FullName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            bool isRoot = directory.Length == 0;
            bool isSelectedDirectory = selectedDirectory.Length > 0 &&
                string.Equals(directory, selectedDirectory, StringComparison.OrdinalIgnoreCase);
            if (!isRoot && !isSelectedDirectory)
                continue;

            bool isSelected = HasSameIdentity(entry, selectedEntry);
            string leaf = ArchiveEntryPath.GetLeafName(entry.FullName);
            if (!isSelected && IsExcludedBesideSelection(leaf, selectedExtension, selectedIsStandalone))
                continue;

            int priority = isSelectedDirectory ? 1 : 0;
            if (selectedByLeaf.TryGetValue(leaf, out var existing) &&
                (existing.Priority > priority || HasSameIdentity(existing.Entry, selectedEntry)))
                continue;

            selectedByLeaf[leaf] = new SelectedEntry(entry, priority);
        }

        if (!selectedByLeaf.Values.Any(value => HasSameIdentity(value.Entry, selectedEntry)))
            throw new InvalidDataException("The selected archive entry is no longer available.");

        AddCompanionsFromOtherDirectories(entries, selectedLeaf, selectedExtension, selectedByLeaf);

        return selectedByLeaf.Values
            .Select(value => value.Entry)
            .OrderBy(entry => entry.Ordinal)
            .ToArray();
    }

    private static bool IsExcludedBesideSelection(string leaf, string selectedExtension, bool selectedIsStandalone)
    {
        string extension = Path.GetExtension(leaf).ToLowerInvariant();

        if (ManifestExtensions.Contains(extension))
            return true;

        if (selectedIsStandalone && StandaloneImageExtensions.Contains(extension))
            return true;

        // A .bin is CUE track data. Beside any other selection it is
        // another game's data or junk that the save-time copy would put
        // on the card.
        if (extension == ".bin" && selectedExtension != ".cue")
            return true;

        return false;
    }

    /// <summary>
    /// Pulls same-basename data files in from other directories for set
    /// formats whose companions are stored away from their manifest. The
    /// old whole-archive flat extraction used to catch those.
    /// </summary>
    private static void AddCompanionsFromOtherDirectories(
        IReadOnlyList<ArchiveEntryInfo> entries,
        string selectedLeaf,
        string selectedExtension,
        Dictionary<string, SelectedEntry> selectedByLeaf)
    {
        string[] companionExtensions = selectedExtension switch
        {
            ".ccd" => new[] { ".img", ".sub" },
            ".mds" => new[] { ".mdf" },
            _ => Array.Empty<string>()
        };
        if (companionExtensions.Length == 0)
            return;

        string baseName = Path.GetFileNameWithoutExtension(selectedLeaf);

        foreach (var extension in companionExtensions)
        {
            string wantedLeaf = baseName + extension;
            if (selectedByLeaf.ContainsKey(wantedLeaf))
                continue;

            foreach (var entry in entries)
            {
                string leaf;
                try
                {
                    leaf = ArchiveEntryPath.GetLeafName(entry.FullName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (string.Equals(leaf, wantedLeaf, StringComparison.OrdinalIgnoreCase))
                {
                    selectedByLeaf[leaf] = new SelectedEntry(entry, 0);
                    break;
                }
            }
        }
    }

    private static bool HasSameIdentity(ArchiveEntryInfo? first, ArchiveEntryInfo? second)
    {
        return first != null &&
            second != null &&
            first.Ordinal == second.Ordinal &&
            first.Size == second.Size &&
            ArchiveEntryPath.HasSameIdentityKey(first.FullName, second.FullName);
    }

    private sealed class SelectedEntry
    {
        internal SelectedEntry(ArchiveEntryInfo entry, int priority)
        {
            Entry = entry;
            Priority = priority;
        }

        internal ArchiveEntryInfo Entry { get; }
        internal int Priority { get; }
    }
}
