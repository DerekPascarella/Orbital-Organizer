using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using OrbitalOrganizer.Core.Models;
using OrbitalOrganizer.Core.Services;

namespace OrbitalOrganizer.Core;

/// <summary>
/// Central orchestrator for all SD card operations.
/// Manages the game list, scanning, sorting, saving, and menu rebuilding.
/// </summary>
public class Manager : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _sdCardPath = string.Empty;
    private MenuKind _menuKindDetected = MenuKind.None;
    private MenuKind _menuKindSelected = MenuKind.RmenuKai;
    private bool _useVirtualFolderSubfolders = true;

    /// <summary>
    /// The list of all games currently loaded. Does not include the menu item (folder 01).
    /// </summary>
    public ObservableCollection<SaturnGame> ItemList { get; } = new();

    /// <summary>
    /// Manages undo/redo operations.
    /// </summary>
    public UndoManager UndoManager { get; } = new();

    /// <summary>
    /// Known virtual folder paths extracted from current items (for autocomplete).
    /// </summary>
    public ObservableCollection<string> KnownFolders { get; } = new();

    /// <summary>
    /// Path to the SD card root (e.g., "H:\").
    /// </summary>
    public string SdCardPath
    {
        get => _sdCardPath;
        set { _sdCardPath = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The menu type detected on the current SD card.
    /// </summary>
    public MenuKind MenuKindDetected
    {
        get => _menuKindDetected;
        set { _menuKindDetected = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The menu type the user has selected to use.
    /// </summary>
    public MenuKind MenuKindSelected
    {
        get => _menuKindSelected;
        set { _menuKindSelected = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether multi-disc games should use virtual folder subfolders in the menu.
    /// </summary>
    public bool UseVirtualFolderSubfolders
    {
        get => _useVirtualFolderSubfolders;
        set { _useVirtualFolderSubfolders = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Path to the application's tools/ directory.
    /// </summary>
    public string ToolsPath { get; set; } = string.Empty;

    /// <summary>
    /// Whether to run lock checks before save operations.
    /// </summary>
    public bool EnableLockCheck { get; set; } = true;

    /// <summary>
    /// Callback for when a folder move fails due to a file lock. The UI sets this
    /// to prompt the user. Return true to retry, false to abort.
    /// </summary>
    public Func<string, Task<bool>>? OnFolderLocked { get; set; }

    /// <summary>
    /// Region code to write into newly created INI files (e.g., "J", "U", "E").
    /// Set by the UI before SaveAsync when NeedsIniFiles() returns true.
    /// </summary>
    public string? PendingConsoleRegion { get; set; }

    /// <summary>
    /// Scans the SD card and populates ItemList.
    /// </summary>
    public async Task LoadItemsFromCardAsync()
    {
        if (string.IsNullOrEmpty(SdCardPath) || !Directory.Exists(SdCardPath))
            throw new InvalidOperationException("Invalid SD card path.");

        ItemList.Clear();
        KnownFolders.Clear();
        UndoManager.Clear();

        // Detect menu type (runs on thread pool to avoid blocking the UI)
        MenuKindDetected = await Task.Run(() => MenuDetector.Detect(SdCardPath));
        if (MenuKindDetected != MenuKind.None)
            MenuKindSelected = MenuKindDetected;

        // If RmenuKai is detected, default virtual folder subfolders to on
        if (MenuKindDetected == MenuKind.RmenuKai || MenuKindDetected == MenuKind.Both)
            UseVirtualFolderSubfolders = true;

        // Insert a synthetic menu entry for folder 01
        string menuName = (MenuKindSelected == MenuKind.RmenuKai || MenuKindSelected == MenuKind.Both)
            ? "RmenuKai" : "RMENU";
        string menuPath = Path.Combine(SdCardPath, Constants.MenuFolderName);
        var menuItem = new SaturnGame
        {
            Name = menuName,
            SdNumber = Constants.MenuFolderNumber,
            FullFolderPath = menuPath,
            WorkMode = WorkMode.None,
            Length = -1
        };
        ItemList.Add(menuItem);

        // Scan for games, adding each to the list as it's loaded
        int count = await CardScanner.ScanCardAsync(SdCardPath, game =>
        {
            if (string.Equals(game.Name, "RMENU", StringComparison.OrdinalIgnoreCase) &&
                game.SdNumber != Constants.MenuFolderNumber)
            {
                game.IsLegacyRmenu = true;
            }

            ItemList.Add(game);
        });

        // If the card has RmenuKai in folder 01 and a legacy RMENU was
        // found during the scan above, upgrade the detected type to Both.
        if (MenuKindDetected == MenuKind.RmenuKai && ItemList.Any(g => g.IsLegacyRmenu))
        {
            MenuKindDetected = MenuKind.Both;
            MenuKindSelected = MenuKind.Both;
        }

        // Build known folders list
        RefreshKnownFolders();
    }

    /// <summary>
    /// Sorts the item list alphabetically by Folder + Name + Disc.
    /// Strips [UNIQUE-] tags for sorting so duplicates sort together.
    /// </summary>
    public void SortList()
    {
        var oldOrder = ItemList.ToList();

        var menuEntry = ItemList.FirstOrDefault(g => g.IsMenuItem);
        var sorted = ItemList
            .Where(g => !g.IsMenuItem)
            .OrderByDescending(g => !string.IsNullOrWhiteSpace(g.Folder))
            .ThenBy(g => g.Folder, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => DuplicateDetector.StripUniqueTag(g.Name), StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Disc)
            .ToList();

        ItemList.Clear();
        if (menuEntry != null)
            ItemList.Add(menuEntry);
        foreach (var game in sorted)
            ItemList.Add(game);

        UndoManager.RecordChange(new ListReorderOperation("Sort List")
        {
            ItemList = ItemList,
            OldOrder = oldOrder,
            NewOrder = ItemList.ToList()
        });
    }

    /// <summary>
    /// Saves all changes to the SD card: renumber folders, rebuild menu, copy new items.
    /// </summary>
    public async Task SaveAsync(IProgress<string>? progress = null, IProgress<int>? itemProgress = null, string? tempFolderRoot = null)
    {
        if (string.IsNullOrEmpty(SdCardPath))
            throw new InvalidOperationException("No SD card path set.");

        string tempRoot = !string.IsNullOrEmpty(tempFolderRoot) && Directory.Exists(tempFolderRoot)
            ? tempFolderRoot
            : Path.GetTempPath();
        string tempDir = Path.Combine(tempRoot, "OrbitalOrganizer_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write default INI files if they don't exist
            EnsureIniFiles();

            // Renumber and move game folders
            await RenumberFoldersAsync(progress);

            // Build and write LIST.INI
            var gamesList = ItemList.Where(g => !g.IsMenuItem && (!g.IsLegacyRmenu || MenuKindSelected == MenuKind.Both)).ToList();
            string listIni = MenuBuilder.GenerateListIni(gamesList, UseVirtualFolderSubfolders, MenuKindSelected);

            // Build RMENU.iso for folder 01 (always the primary menu)
            bool primaryIsKai = MenuKindSelected == MenuKind.RmenuKai || MenuKindSelected == MenuKind.Both;
            await BuildMenuIsoAsync(Constants.MenuFolderName, listIni, tempDir, primaryIsKai, progress);

            // If "Both" mode, also rebuild the legacy RMENU instance with
            // a separate LIST.INI that has no folder paths (RMENU doesn't support them)
            if (MenuKindSelected == MenuKind.Both)
            {
                var legacyItem = ItemList.FirstOrDefault(g => g.IsLegacyRmenu);
                if (legacyItem != null)
                {
                    string legacyListIni = MenuBuilder.GenerateListIni(gamesList, UseVirtualFolderSubfolders, MenuKind.Rmenu);
                    string legacyFolder = legacyItem.FolderNumberFormatted;
                    await BuildMenuIsoAsync(legacyFolder, legacyListIni, tempDir, useRmenuKai: false, progress);

                    legacyItem.FullFolderPath = Path.Combine(SdCardPath, legacyFolder);
                    legacyItem.WorkMode = WorkMode.None;
                }
            }

            // Copy new items to the SD card (with CUE/BIN conversion if applicable)
            int processed = 0;
            await CopyNewItemsAsync(progress, itemProgress, processed, tempRoot);

            // Patch product IDs where changed
            await PatchProductIdsAsync(progress);

            // Write sidecar files for items with changes
            if (ItemList.Any(g => g.SidecarsDirty && !g.IsMenuItem))
            {
                progress?.Report("Writing metadata files...");
                await Task.Run(() => WriteSidecarFiles(progress));
            }

            // Generate GameList.txt
            string gameListContent = MenuBuilder.GenerateGameList(gamesList, MenuKindSelected);
            File.WriteAllText(Path.Combine(SdCardPath, "GameList.txt"), gameListContent, System.Text.Encoding.UTF8);

            progress?.Report("Done!");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Adds game(s) from file paths (disc images or folders containing them).
    /// New items are staged with SdNumber = 0 and WorkMode = New.
    /// </summary>
    public async Task<List<SaturnGame>> AddGamesAsync(string[] paths, IProgress<string>? progress = null, int insertIndex = -1, string? tempFolderRoot = null)
    {
        var added = new List<SaturnGame>();

        foreach (var path in paths)
        {
            progress?.Report($"Adding {Path.GetFileName(path)}...");

            SaturnGame? game = null;

            if (Directory.Exists(path))
            {
                game = await Task.Run(() => LoadGameFromSource(path, specificFile: null));
            }
            else if (File.Exists(path) && Services.ArchiveHelper.IsArchive(path))
            {
                game = await Task.Run(() => LoadGameFromArchive(path, tempFolderRoot));
            }
            else if (File.Exists(path))
            {
                string parentDir = Path.GetDirectoryName(path)!;
                game = await Task.Run(() => LoadGameFromSource(parentDir, specificFile: path));
            }

            if (game != null)
            {
                game.SdNumber = 0;
                game.WorkMode = WorkMode.New;
                game.SidecarsDirty = true;

                if (insertIndex >= 0 && insertIndex <= ItemList.Count)
                {
                    ItemList.Insert(insertIndex, game);
                    insertIndex++;
                }
                else
                {
                    ItemList.Add(game);
                }

                added.Add(game);
            }
        }

        RefreshKnownFolders();

        if (added.Count > 0)
        {
            var undoOp = new MultiItemAddOperation { ItemList = ItemList };
            foreach (var game in added)
                undoOp.Items.Add((game, ItemList.IndexOf(game)));
            UndoManager.RecordChange(undoOp);
        }

        return added;
    }

    /// <summary>
    /// Removes selected items from the list. Items already on the SD card
    /// will have their folders deleted on save.
    /// </summary>
    public void RemoveItems(IEnumerable<SaturnGame> items)
    {
        var toRemove = items.Where(i => !i.IsMenuItem).ToList();
        if (toRemove.Count == 0) return;

        var undoOp = new MultiItemRemoveOperation { ItemList = ItemList };
        foreach (var item in toRemove)
            undoOp.Items.Add((item, ItemList.IndexOf(item)));

        foreach (var item in toRemove)
            ItemList.Remove(item);

        UndoManager.RecordChange(undoOp);
    }

    /// <summary>
    /// Counts numbered game folders (02+) on the SD card.
    /// </summary>
    public int CountGameFolders()
    {
        if (string.IsNullOrEmpty(SdCardPath) || !Directory.Exists(SdCardPath))
            return 0;

        return Directory.GetDirectories(SdCardPath)
            .Count(d =>
            {
                string name = Path.GetFileName(d);
                return int.TryParse(name, out _) && name != Constants.MenuFolderName;
            });
    }

    /// <summary>
    /// Returns loaded items that need IP.BIN scanning. Uses the flag set
    /// during card scanning so this call involves no filesystem I/O.
    /// </summary>
    public List<SaturnGame> GetItemsNeedingMetadataScan()
    {
        return ItemList
            .Where(g => g.NeedsMetadataScan && g.SdNumber > 0 && !g.IsMenuItem)
            .ToList();
    }

    /// <summary>
    /// Fills missing metadata for items that need it. On OO-managed cards,
    /// tries LIST.INI first (preserves user-customized titles, folder paths,
    /// etc.) and falls back to IP.BIN for anything LIST.INI couldn't provide.
    /// On non-OO cards, goes straight to IP.BIN.
    /// </summary>
    public async Task PerformMetadataScanAsync(List<SaturnGame> items, IProgress<(int current, string name)>? progress = null)
    {
        // On OO cards, parse LIST.INI once upfront so individual items
        // can recover values before falling back to IP.BIN
        Dictionary<string, MigrationService.ListIniEntry>? listIniEntries = null;
        if (IsOOCard())
        {
            string? listIniPath = FindListIniPath();
            if (listIniPath != null)
                listIniEntries = await Task.Run(() => MigrationService.ParseListIni(listIniPath));
        }

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            progress?.Report((i + 1, item.Name));

            MigrationService.ListIniEntry? listIniEntry = null;
            if (listIniEntries != null)
            {
                string folderKey = item.SdNumber.ToString("D2");
                if (item.SdNumber >= 100)
                    folderKey = item.SdNumber.ToString();
                listIniEntries.TryGetValue(folderKey, out listIniEntry);
            }

            await Task.Run(() => CardScanner.ScanAndCacheMetadata(item, listIniEntry));
        }
    }

    /// <summary>
    /// Returns the path to LIST.INI if it exists on the card, null otherwise.
    /// </summary>
    public string? FindListIniPath()
    {
        if (string.IsNullOrEmpty(SdCardPath)) return null;
        string path = Path.Combine(SdCardPath, "01", "BIN", "RMENU", "LIST.INI");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Phase 1: Pre-populate sidecar files from LIST.INI data.
    /// Preserves existing sidecars (never overwrites).
    /// What gets written depends on the card origin:
    ///   NonOO:            Name.txt + Folder.txt only (everything else from IP.BIN scan)
    ///   PerlOO / CSharpOO: all LIST.INI fields (recovery for deleted sidecars)
    /// </summary>
    public async Task<int> PrePopulateFromListIniAsync(IProgress<string>? progress = null)
    {
        string? listIniPath = FindListIniPath();
        if (listIniPath == null) return 0;

        // OO-managed cards already have sidecar files from a previous session
        if (await Task.Run(() => IsOOCard()))
            return 0;

        var origin = CardOrigin.NonOO;

        progress?.Report("Reading LIST.INI...");
        var entries = await Task.Run(() => MigrationService.ParseListIni(listIniPath));
        if (entries.Count == 0) return 0;

        progress?.Report($"Writing metadata from LIST.INI for {entries.Count} game(s)...");
        return await Task.Run(() => MigrationService.GenerateSidecarFiles(SdCardPath, entries, origin, progress));
    }

    /// <summary>
    /// Returns true if GameList.txt exists on the SD card root, meaning
    /// Orbital Organizer (Perl or C# version) has already managed this card.
    /// </summary>
    private bool IsOOCard()
    {
        return File.Exists(Path.Combine(SdCardPath, "GameList.txt"));
    }

    /// <summary>
    /// Injects a legacy RMENU entry into the item list at position index 0
    /// (which becomes folder 02 on save, right after the menu system).
    /// </summary>
    public void InjectLegacyRmenu()
    {
        if (ItemList.Any(g => g.IsLegacyRmenu))
            return;

        var rmenu = new SaturnGame
        {
            Name = "RMENU",
            Disc = "1/1",
            Region = "JTUE",
            Version = "0.2.0",
            ReleaseDate = "20170228",
            IsLegacyRmenu = true,
            WorkMode = WorkMode.New,
            SidecarsDirty = true,
            SdNumber = 0,
            Length = -1
        };

        // Insert after the menu entry (folder 01)
        int insertIndex = ItemList.Any(g => g.IsMenuItem) ? 1 : 0;
        ItemList.Insert(insertIndex, rmenu);
    }

    /// <summary>
    /// Removes any legacy RMENU entries from the item list.
    /// </summary>
    public void RemoveLegacyRmenu()
    {
        var legacyItems = ItemList.Where(g => g.IsLegacyRmenu).ToList();
        foreach (var item in legacyItems)
            ItemList.Remove(item);
    }

    /// <summary>
    /// Collects all folder paths that will be read/written/moved during save.
    /// Used by the lock checker to verify accessibility before starting.
    /// </summary>
    public List<string> CollectPathsToModify()
    {
        var paths = new List<string>();

        // Menu folder (01) will always be written
        string menuPath = Path.Combine(SdCardPath, Constants.MenuFolderName);
        if (Directory.Exists(menuPath))
            paths.Add(menuPath);

        // All existing game folders
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in ItemList.Where(g => g.SdNumber > 0 && !g.IsMenuItem))
        {
            if (Directory.Exists(game.FullFolderPath))
            {
                paths.Add(game.FullFolderPath);
                knownPaths.Add(game.FullFolderPath);
            }
        }

        // Orphan folders (numbered folders on the card that aren't claimed by any item)
        foreach (var dir in Directory.GetDirectories(SdCardPath))
        {
            string folderName = Path.GetFileName(dir);
            if (!int.TryParse(folderName, out int num)) continue;
            if (num == Constants.MenuFolderNumber) continue;

            if (!knownPaths.Contains(dir))
                paths.Add(dir);
        }

        return paths;
    }

    // --- Private helpers ---

    /// <summary>
    /// Returns true if either Rhea.ini or Phoebe.ini is missing from the SD card.
    /// The UI should prompt for a console region before saving in this case.
    /// </summary>
    public bool NeedsIniFiles()
    {
        if (string.IsNullOrEmpty(SdCardPath))
            return false;

        return !File.Exists(Path.Combine(SdCardPath, "Rhea.ini")) ||
               !File.Exists(Path.Combine(SdCardPath, "Phoebe.ini"));
    }

    private void EnsureIniFiles()
    {
        string rheaIni = Path.Combine(SdCardPath, "Rhea.ini");
        string phoebeIni = Path.Combine(SdCardPath, "Phoebe.ini");

        if (!File.Exists(rheaIni))
        {
            string defaultRhea = Path.Combine(ToolsPath, "defaults", "Rhea.ini");
            if (File.Exists(defaultRhea))
                CopyIniWithRegion(defaultRhea, rheaIni);
        }

        if (!File.Exists(phoebeIni))
        {
            string defaultPhoebe = Path.Combine(ToolsPath, "defaults", "Phoebe.ini");
            if (File.Exists(defaultPhoebe))
                CopyIniWithRegion(defaultPhoebe, phoebeIni);
        }
    }

    private void CopyIniWithRegion(string source, string destination)
    {
        var lines = File.ReadAllLines(source);

        if (!string.IsNullOrEmpty(PendingConsoleRegion))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("auto_region"))
                    lines[i] = $"auto_region = {PendingConsoleRegion}";
            }
        }

        File.WriteAllLines(destination, lines);
    }

    private async Task RenumberFoldersAsync(IProgress<string>? progress)
    {
        // Calculate desired folder numbers (starting at 02)
        int folderNum = 1;
        foreach (var game in ItemList)
        {
            if (game.IsMenuItem) continue;
            folderNum++;

            if (game.WorkMode != WorkMode.New && game.SdNumber != folderNum)
                game.WorkMode = WorkMode.Move;

            game.SdNumber = folderNum;
        }

        // Delete orphaned numbered folders not in the item list.
        // This must happen BEFORE the GUID rename phase below, because
        // knownFolders is built from items' current FullFolderPath values
        // (which still point to their original numbered folders at this point).
        var knownFolders = new HashSet<string>(
            ItemList.Where(g => !g.IsMenuItem && g.WorkMode != WorkMode.New)
                    .Select(g => Path.GetFileName(g.FullFolderPath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(SdCardPath))
        {
            string folderName = Path.GetFileName(dir);
            if (!int.TryParse(folderName, out int num)) continue;
            if (num == Constants.MenuFolderNumber) continue;

            if (!knownFolders.Contains(folderName))
            {
                progress?.Report($"Deleting orphaned folder {folderName}...");
                Directory.Delete(dir, recursive: true);
            }
        }

        // Rename folders that need to move to GUID intermediates
        var itemsToMove = ItemList.Where(g => g.WorkMode == WorkMode.Move).ToList();

        foreach (var game in itemsToMove)
        {
            if (!Directory.Exists(game.FullFolderPath)) continue;

            string guidName = Guid.NewGuid().ToString("N");
            string guidPath = Path.Combine(SdCardPath, guidName);

            progress?.Report($"Staging {DuplicateDetector.FormatForDisplay(game.Name)}...");
            await FolderHelper.MoveDirectoryAsync(game.FullFolderPath, guidPath, OnFolderLocked);
            game.FullFolderPath = guidPath;
        }

        // Move from GUID intermediates to final folder numbers
        foreach (var game in itemsToMove)
        {
            if (!Directory.Exists(game.FullFolderPath)) continue;

            string newFolderName = game.FolderNumberFormatted;
            string newPath = Path.Combine(SdCardPath, newFolderName);

            progress?.Report($"Folder {newFolderName}: {DuplicateDetector.FormatForDisplay(game.Name)}");

            // Wrap the lock callback so the user sees the destination folder name
            // instead of the meaningless GUID intermediate path
            Func<string, Task<bool>>? lockCallback = OnFolderLocked != null
                ? _ => OnFolderLocked(newPath)
                : null;
            await FolderHelper.MoveDirectoryAsync(game.FullFolderPath, newPath, lockCallback);
            game.FullFolderPath = newPath;
            game.WorkMode = WorkMode.None;
        }
    }

    private async Task BuildMenuIsoAsync(string folderName, string listIni, string tempDir, bool useRmenuKai, IProgress<string>? progress)
    {
        progress?.Report($"Building {(useRmenuKai ? "RmenuKai" : "RMENU")} ISO for folder {folderName}...");

        string menuFolder = Path.Combine(SdCardPath, folderName);
        Directory.CreateDirectory(menuFolder);

        // Prepare the RMENU content in a temp directory
        string contentDir = IsoBuilder.PrepareRmenuContent(ToolsPath, listIni, tempDir, useRmenuKai);
        string ipBinPath = Path.Combine(ToolsPath, "shared", "IP.BIN");
        string isoPath = Path.Combine(menuFolder, "RMENU.iso");

        await Task.Run(() => IsoBuilder.BuildRmenuIso(contentDir, isoPath, ipBinPath));

        // Copy RMENU build assets into BIN/RMENU/ for compatibility
        string binRmenuDir = Path.Combine(menuFolder, "BIN", "RMENU");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(binRmenuDir);
            foreach (var file in Directory.GetFiles(contentDir))
            {
                string dest = Path.Combine(binRmenuDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }
        });
    }

    private async Task CopyNewItemsAsync(IProgress<string>? progress, IProgress<int>? itemProgress = null, int processedCount = 0, string? tempFolderRoot = null)
    {
        var newItems = ItemList.Where(g => g.WorkMode == WorkMode.New && !g.IsLegacyRmenu).ToList();

        foreach (var game in newItems)
        {
            processedCount++;
            itemProgress?.Report(processedCount);

            string destFolder = Path.Combine(SdCardPath, game.FolderNumberFormatted);

            if (game.FileFormat == FileFormat.Compressed)
            {
                // Extract archive to temp, detect format, convert if needed, copy to SD
                await UncompressAndCopyAsync(game, destFolder, progress, tempFolderRoot);
            }
            else if (game.FileFormat == FileFormat.Chd)
            {
                // Convert CHD to CCD/IMG/SUB via intermediate CUE/BIN
                await ConvertChdAndCopyAsync(game, destFolder, progress, tempFolderRoot);
            }
            else if (string.IsNullOrEmpty(game.SourcePath) || !Directory.Exists(game.SourcePath))
            {
                continue;
            }
            else if (game.FileFormat == FileFormat.CueBin)
            {
                progress?.Report($"Copying {game.Name} to folder {game.FolderNumberFormatted}...");

                string? cueFile = game.ImageFiles
                    .FirstOrDefault(f => Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase));

                if (cueFile != null)
                {
                    await Cue2CcdConverter.ConvertAsync(cueFile, destFolder, progress);
                }
            }
            else
            {
                progress?.Report($"Copying {game.Name} to folder {game.FolderNumberFormatted}...");

                Directory.CreateDirectory(destFolder);
                foreach (var file in game.ImageFiles)
                {
                    if (!File.Exists(file)) continue;
                    string destFile = Path.Combine(destFolder, Path.GetFileName(file));
                    await Task.Run(() => File.Copy(file, destFile, overwrite: true));
                }
            }

            game.FullFolderPath = destFolder;
            game.WorkMode = WorkMode.None;
            game.FileFormat = FileFormat.Uncompressed;

            // Re-populate image files from the new location
            game.ImageFiles.Clear();
            long totalSize = 0;
            foreach (var file in Directory.GetFiles(destFolder))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (Constants.AllImageExtensions.Contains(ext) || ext == ".img" || ext == ".sub" || ext == ".bin")
                {
                    game.ImageFiles.Add(file);
                    totalSize += new FileInfo(file).Length;
                }
            }
            game.Length = totalSize;
        }

        // Handle legacy RMENU new items (just need the folder created, ISO is built separately)
        var newLegacy = ItemList.Where(g => g.WorkMode == WorkMode.New && g.IsLegacyRmenu).ToList();
        foreach (var game in newLegacy)
        {
            string destFolder = Path.Combine(SdCardPath, game.FolderNumberFormatted);
            Directory.CreateDirectory(destFolder);
            game.FullFolderPath = destFolder;
            game.WorkMode = WorkMode.None;
        }
    }

    /// <summary>
    /// Extracts a compressed archive to a temp directory, detects the disc image
    /// format inside, converts CUE/BIN if needed, then copies files to the SD card.
    /// </summary>
    private async Task UncompressAndCopyAsync(SaturnGame game, string destFolder, IProgress<string>? progress, string? tempFolderRoot = null)
    {
        string archivePath = game.SourcePath;
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
            throw new FileNotFoundException(
                $"Archive file not found: {Path.GetFileName(archivePath)}");

        string tempRoot = !string.IsNullOrEmpty(tempFolderRoot) && Directory.Exists(tempFolderRoot)
            ? tempFolderRoot
            : Path.GetTempPath();
        string tempExtractDir = Path.Combine(tempRoot,
            "OrbitalOrganizer_ext_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            progress?.Report($"Extracting {Path.GetFileName(archivePath)}...");
            await Task.Run(() => Services.ArchiveHelper.ExtractArchive(archivePath, tempExtractDir));

            Directory.CreateDirectory(destFolder);

            var extractedFiles = Directory.GetFiles(tempExtractDir);

            // Look for CHD or CUE files that need format conversion
            string? chdFile = extractedFiles
                .FirstOrDefault(f => Path.GetExtension(f).Equals(".chd", StringComparison.OrdinalIgnoreCase));

            string? cueFile = extractedFiles
                .FirstOrDefault(f => Path.GetExtension(f).Equals(".cue", StringComparison.OrdinalIgnoreCase));

            if (chdFile != null)
            {
                // Convert CHD to CCD/IMG/SUB via intermediate CUE/BIN
                string tempCueDir = Path.Combine(tempExtractDir, "cue_temp");
                var (success, message, cuePath) = await ChdConverter.ConvertToCueBinAsync(
                    chdFile, tempCueDir, progress, gameName: game.Name);

                if (success && cuePath != null)
                {
                    progress?.Report($"Converting {game.Name} (CUE/BIN to CCD/IMG/SUB)...");
                    await Cue2CcdConverter.ConvertAsync(cuePath, destFolder, progress);
                }
            }
            else if (cueFile != null)
            {
                progress?.Report($"Converting {game.Name} (CUE/BIN to CCD)...");
                await Cue2CcdConverter.ConvertAsync(cueFile, destFolder, progress);
            }
            else
            {
                // Copy extracted disc image files directly
                progress?.Report($"Copying {game.Name} to folder {game.FolderNumberFormatted}...");

                foreach (var file in extractedFiles)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Constants.AllImageExtensions.Contains(ext) ||
                        ext == ".img" || ext == ".sub" || ext == ".bin")
                    {
                        string destFile = Path.Combine(destFolder, Path.GetFileName(file));
                        await Task.Run(() => File.Copy(file, destFile, overwrite: true));
                    }
                }
            }
        }
        finally
        {
            try { Directory.Delete(tempExtractDir, recursive: true); } catch { }
        }
    }

    private async Task ConvertChdAndCopyAsync(SaturnGame game, string destFolder, IProgress<string>? progress, string? tempFolderRoot = null)
    {
        string? chdPath = game.ImageFiles
            .FirstOrDefault(f => Path.GetExtension(f).Equals(".chd", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(chdPath) || !File.Exists(chdPath))
            throw new FileNotFoundException($"CHD file not found: {Path.GetFileName(chdPath)}");

        string tempRoot = !string.IsNullOrEmpty(tempFolderRoot) && Directory.Exists(tempFolderRoot)
            ? tempFolderRoot
            : Path.GetTempPath();
        string tempCueDir = Path.Combine(tempRoot,
            "OrbitalOrganizer_chd_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            var (success, message, cuePath) = await ChdConverter.ConvertToCueBinAsync(
                chdPath, tempCueDir, progress, gameName: game.Name);

            if (!success || cuePath == null)
                throw new InvalidOperationException(
                    $"CHD conversion failed for {game.Name}: {message}");

            // Convert CUE/BIN to CCD/IMG/SUB
            progress?.Report($"Converting {game.Name} (CUE/BIN to CCD/IMG/SUB)...");
            await Cue2CcdConverter.ConvertAsync(cuePath, destFolder, progress);
        }
        finally
        {
            try { Directory.Delete(tempCueDir, recursive: true); } catch { }
        }
    }

    private async Task PatchProductIdsAsync(IProgress<string>? progress)
    {
        var dirtyGames = ItemList.Where(g => g.ProductIdDirty &&
            !string.IsNullOrWhiteSpace(g.ProductId) &&
            !g.IsMenuItem && !g.IsLegacyRmenu &&
            !string.IsNullOrEmpty(g.FullFolderPath)).ToList();

        foreach (var game in dirtyGames)
        {
            var (offset, filePath) = IpBinParser.FindIpBinInFolder(game.FullFolderPath);
            if (offset < 0 || filePath == null) continue;

            // Read the current product ID from the disc image, using the
            // same space-split parsing as IpBinParser to avoid false mismatches
            string currentId = System.Text.Encoding.ASCII
                .GetString(IpBinParser.ReadBytesAtOffset(filePath, offset + Constants.IpOffsetProductId, Constants.IpLengthProductId))
                .Trim();
            int sp = currentId.IndexOf(' ');
            if (sp >= 0) currentId = currentId[..sp];

            if (currentId != game.ProductId.Trim())
            {
                progress?.Report($"Patching modified Product ID: {game.Name}...");
                await Task.Run(() => ProductIdPatcher.PatchProductId(filePath, offset, game.ProductId));
            }

            game.ProductIdDirty = false;
        }
    }

    private void WriteSidecarFiles(IProgress<string>? progress)
    {
        foreach (var game in ItemList)
        {
            if (game.IsMenuItem) continue;
            if (!game.SidecarsDirty) continue;
            if (string.IsNullOrEmpty(game.FullFolderPath)) continue;

            MetadataManager.WriteToFolder(game.FullFolderPath, game);
            game.SidecarsDirty = false;
        }
    }

    private SaturnGame? LoadGameFromSource(string sourcePath, string? specificFile)
    {
        if (!CardScanner.HasDiscImage(sourcePath))
            return null;

        // CHD files need special handling via libchdr
        bool isChdFile = specificFile != null &&
            Path.GetExtension(specificFile).Equals(".chd", StringComparison.OrdinalIgnoreCase);

        if (isChdFile)
            return LoadGameFromChd(specificFile!, sourcePath);

        // If a specific CUE file was selected, parse it to find related BIN files
        bool isCueFile = specificFile != null &&
            Path.GetExtension(specificFile).Equals(".cue", StringComparison.OrdinalIgnoreCase);

        // If a specific CCD file was selected, find its companion .img and .sub files
        bool isCcdFile = specificFile != null &&
            Path.GetExtension(specificFile).Equals(".ccd", StringComparison.OrdinalIgnoreCase);

        // If a specific MDS file was selected, find its companion .mdf file
        bool isMdsFile = specificFile != null &&
            Path.GetExtension(specificFile).Equals(".mds", StringComparison.OrdinalIgnoreCase);

        List<string>? cueRelatedFiles = null;
        if (isCueFile)
        {
            cueRelatedFiles = CueSheetParser.GetAllRelatedFiles(specificFile!);
        }

        List<string>? companionFiles = null;
        if (isCcdFile)
        {
            companionFiles = new List<string> { specificFile! };
            string basePath = Path.Combine(Path.GetDirectoryName(specificFile!)!,
                                           Path.GetFileNameWithoutExtension(specificFile!));

            string imgFile = basePath + ".img";
            if (File.Exists(imgFile))
                companionFiles.Add(imgFile);

            string subFile = basePath + ".sub";
            if (File.Exists(subFile))
                companionFiles.Add(subFile);
        }
        else if (isMdsFile)
        {
            companionFiles = new List<string> { specificFile! };
            string mdfFile = Path.ChangeExtension(specificFile!, ".mdf");
            if (File.Exists(mdfFile))
                companionFiles.Add(mdfFile);
        }

        // Try to parse IP.BIN from the relevant files
        var (offset, filePath) = isCueFile && cueRelatedFiles != null
            ? FindIpBinInFiles(cueRelatedFiles)
            : companionFiles != null
                ? FindIpBinInFiles(companionFiles)
                : IpBinParser.FindIpBinInFolder(sourcePath);

        SaturnGame game;

        if (offset >= 0 && filePath != null)
        {
            var ip = IpBinParser.ParseHeader(filePath, offset);
            game = new SaturnGame
            {
                Name = ip.Title,
                ProductId = ip.ProductId,
                Disc = ip.Disc,
                Region = ip.Region,
                Version = ip.Version,
                ReleaseDate = ip.ReleaseDate,
                Ip = ip,
                SourcePath = sourcePath
            };
        }
        else
        {
            string displayName = specificFile != null
                ? Path.GetFileNameWithoutExtension(specificFile)
                : Path.GetFileName(sourcePath);

            game = new SaturnGame
            {
                Name = displayName,
                ProductId = "NA",
                Disc = "1/1",
                Region = "NA",
                Version = "NA",
                ReleaseDate = "NA",
                SourcePath = sourcePath
            };
        }

        // If sidecar text files exist alongside the disc image, use them as overrides
        var existing = MetadataManager.ReadFromFolder(sourcePath);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(existing.Name)) game.Name = existing.Name;
            if (!string.IsNullOrWhiteSpace(existing.Folder)) game.Folder = existing.Folder;
            if (!string.IsNullOrWhiteSpace(existing.ProductId)) game.ProductId = existing.ProductId;

            if (File.Exists(Path.Combine(sourcePath, Constants.DiscFile)))
                game.Disc = existing.Disc;
            if (File.Exists(Path.Combine(sourcePath, Constants.RegionFile)))
                game.Region = existing.Region;
            if (File.Exists(Path.Combine(sourcePath, Constants.VersionFile)))
                game.Version = existing.Version;
            if (File.Exists(Path.Combine(sourcePath, Constants.DateFile)))
                game.ReleaseDate = existing.ReleaseDate;
        }

        // Track only the relevant files
        long totalSize = 0;

        if (isCueFile && cueRelatedFiles != null)
        {
            // CUE file: only track the CUE and its referenced BINs
            foreach (var file in cueRelatedFiles)
            {
                game.ImageFiles.Add(file);
                totalSize += new FileInfo(file).Length;
            }
            game.FileFormat = FileFormat.CueBin;
        }
        else if (companionFiles != null)
        {
            // CCD or MDS file: track the main file and its companions
            foreach (var file in companionFiles)
            {
                game.ImageFiles.Add(file);
                totalSize += new FileInfo(file).Length;
            }
        }
        else if (specificFile != null)
        {
            // Specific non-CUE/non-CCD file selected
            game.ImageFiles.Add(specificFile);
            totalSize += new FileInfo(specificFile).Length;
        }
        else
        {
            // Directory: collect all image files
            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (Constants.AllImageExtensions.Contains(ext) || ext == ".img" || ext == ".sub" || ext == ".bin" ||
                    ext == ".chd")
                {
                    game.ImageFiles.Add(file);
                    totalSize += new FileInfo(file).Length;
                }
            }

            if (game.ImageFiles.Any(f => Path.GetExtension(f).Equals(".chd", StringComparison.OrdinalIgnoreCase)))
                game.FileFormat = FileFormat.Chd;
        }

        game.Length = totalSize;
        return game;
    }

    /// <summary>
    /// Peeks inside an archive to find a disc image, then creates a staged
    /// SaturnGame entry. The archive itself is stored as the source and
    /// extracted later during save.
    /// </summary>
    private SaturnGame? LoadGameFromArchive(string archivePath, string? tempFolderRoot = null)
    {
        var discImage = Services.ArchiveHelper.FindDiscImageInArchive(archivePath);
        if (discImage == null)
            return null;

        // Extract to a temp directory so we can parse IP.BIN
        string tempRoot = !string.IsNullOrEmpty(tempFolderRoot) && Directory.Exists(tempFolderRoot)
            ? tempFolderRoot
            : Path.GetTempPath();
        string tempDir = Path.Combine(tempRoot,
            "OrbitalOrganizer_peek_" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Services.ArchiveHelper.ExtractArchive(archivePath, tempDir);

            // Try to load game metadata from extracted files
            var game = LoadGameFromSource(tempDir, specificFile: null);
            if (game == null)
                return null;

            // Grab the uncompressed size from the extracted files before cleanup
            long uncompressedSize = game.Length;

            // Point source back to the archive file (not the temp dir)
            game.SourcePath = archivePath;
            game.InnerFileFormat = game.FileFormat;
            game.FileFormat = FileFormat.Compressed;

            // Store the archive path as the sole image file reference for now
            game.ImageFiles.Clear();
            game.ImageFiles.Add(archivePath);

            // Show the uncompressed size so the user knows actual SD card usage
            game.Length = uncompressedSize;

            return game;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private SaturnGame? LoadGameFromChd(string chdPath, string sourcePath)
    {
        var ip = IpBinParser.ParseHeaderFromChd(chdPath);

        SaturnGame game;

        if (ip != null)
        {
            game = new SaturnGame
            {
                Name = ip.Title,
                ProductId = ip.ProductId,
                Disc = ip.Disc,
                Region = ip.Region,
                Version = ip.Version,
                ReleaseDate = ip.ReleaseDate,
                Ip = ip,
                SourcePath = sourcePath
            };
        }
        else
        {
            game = new SaturnGame
            {
                Name = Path.GetFileNameWithoutExtension(chdPath),
                ProductId = "NA",
                Disc = "1/1",
                Region = "NA",
                Version = "NA",
                ReleaseDate = "NA",
                SourcePath = sourcePath
            };
        }

        game.ImageFiles.Add(chdPath);
        game.Length = new FileInfo(chdPath).Length;
        game.FileFormat = FileFormat.Chd;

        return game;
    }

    private static (long offset, string? filePath) FindIpBinInFiles(List<string> files)
    {
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".cue" || ext == ".ccd" || ext == ".sub" || ext == ".mds") continue;

            long offset = IpBinParser.FindIpBinStart(file);
            if (offset >= 0)
                return (offset, file);
        }
        return (-1, null);
    }

    public bool SearchInItem(SaturnGame item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return false;

        return (item.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
               (item.ProductId?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public void RefreshKnownFolders()
    {
        KnownFolders.Clear();

        var allFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in ItemList)
        {
            if (!string.IsNullOrWhiteSpace(item.Folder))
                allFolders.Add(item.Folder);

            foreach (var altFolder in item.AlternativeFolders)
            {
                if (!string.IsNullOrWhiteSpace(altFolder))
                    allFolders.Add(altFolder);
            }
        }

        foreach (var folder in allFolders.OrderBy(f => f))
            KnownFolders.Add(folder);
    }

    /// <summary>
    /// Returns a dictionary of folder paths to the number of games in each folder.
    /// Used by the batch folder rename feature.
    /// </summary>
    public Dictionary<string, int> GetFolderCounts()
    {
        var folderCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in ItemList)
        {
            // Count each item once per unique folder path it appears in
            var itemFolders = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(item.Folder))
                itemFolders.Add(item.Folder);

            foreach (var altFolder in item.AlternativeFolders)
            {
                if (!string.IsNullOrWhiteSpace(altFolder))
                    itemFolders.Add(altFolder);
            }

            foreach (var folder in itemFolders)
            {
                if (folderCounts.ContainsKey(folder))
                    folderCounts[folder]++;
                else
                    folderCounts[folder] = 1;
            }
        }

        return folderCounts;
    }

    /// <summary>
    /// Applies folder path mappings from the batch rename dialog.
    /// Handles both exact matches and prefix-based child folder inheritance.
    /// </summary>
    public (int updatedCount, int conflictsRemoved) ApplyFolderMappings(Dictionary<string, string> mappings)
    {
        if (mappings == null || mappings.Count == 0)
            return (0, 0);

        int updatedCount = 0;
        int conflictsRemoved = 0;

        foreach (var item in ItemList)
        {
            // Remap primary folder
            if (!string.IsNullOrWhiteSpace(item.Folder))
            {
                if (mappings.ContainsKey(item.Folder))
                {
                    item.Folder = mappings[item.Folder];
                    item.SidecarsDirty = true;
                    updatedCount++;
                }
                else
                {
                    foreach (var mapping in mappings)
                    {
                        if (item.Folder.StartsWith(mapping.Key + "\\", StringComparison.Ordinal))
                        {
                            item.Folder = mapping.Value + item.Folder.Substring(mapping.Key.Length);
                            item.SidecarsDirty = true;
                            updatedCount++;
                            break;
                        }
                    }
                }
            }

            // Remap alternative folders
            for (int i = 0; i < item.AlternativeFolders.Count; i++)
            {
                var altFolder = item.AlternativeFolders[i];
                if (string.IsNullOrWhiteSpace(altFolder)) continue;

                if (mappings.ContainsKey(altFolder))
                {
                    item.AlternativeFolders[i] = mappings[altFolder];
                    item.SidecarsDirty = true;
                    updatedCount++;
                }
                else
                {
                    foreach (var mapping in mappings)
                    {
                        if (altFolder.StartsWith(mapping.Key + "\\", StringComparison.Ordinal))
                        {
                            item.AlternativeFolders[i] = mapping.Value + altFolder.Substring(mapping.Key.Length);
                            item.SidecarsDirty = true;
                            updatedCount++;
                            break;
                        }
                    }
                }
            }

            // Post-remap conflict scrub: remove alt folders that now match primary
            if (item.AlternativeFolders.Count > 0)
            {
                int removed = 0;
                if (!string.IsNullOrWhiteSpace(item.Folder))
                    removed += item.AlternativeFolders.RemoveAll(af => af == item.Folder);

                // Deduplicate
                var distinct = item.AlternativeFolders.Distinct(StringComparer.Ordinal).ToList();
                if (distinct.Count < item.AlternativeFolders.Count)
                {
                    removed += item.AlternativeFolders.Count - distinct.Count;
                    item.AlternativeFolders = distinct;
                }

                if (removed > 0)
                {
                    item.SidecarsDirty = true;
                    conflictsRemoved += removed;
                }
            }
        }

        RefreshKnownFolders();

        return (updatedCount, conflictsRemoved);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
