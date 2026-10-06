using System.Text;
using OrbitalOrganizer.Core.Models;
using OrbitalOrganizer.Core.Services;

namespace OrbitalOrganizer.Core;

public partial class Manager
{
    private readonly List<SaturnGame> _removedItems = new();
    public Func<string, Task<bool>>? OnDiscardIncompletePreparation { get; set; }
    public string? LastSaveOutcome { get; private set; }

    private CardSaveTransaction? OpenSave() => CardSaveTransaction.Open(SdCardPath,
        (from, to) => FolderHelper.MoveDirectoryAsync(from, to, OnFolderLocked));

    public Task RecoverCardAsync() => RunOperationAsync(RecoverCardCoreAsync);

    private async Task RecoverCardCoreAsync()
    {
        CardSaveTransaction? tx;
        try { tx = OpenSave(); }
        catch { _needsRecovery = true; throw; }
        if (tx == null) { _needsRecovery = false; return; }
        bool committed = tx.HasCommitIntent;
        try { await tx.RecoverAsync(); _needsRecovery = false; }
        catch { _needsRecovery = true; throw; }
        if (committed)
        {
            Mutate(() => ApplyPublishedRows(tx.Items));
            Mutate(() => UndoManager.Clear());
            _removedItems.Clear();
            LastSaveOutcome = "Completed the interrupted save.";
        }
        else
        {
            Mutate(() => ApplyReadyRows(tx.Items));
            LastSaveOutcome = "Original folders restored. Completed imports are retained for the next save.";
        }
    }

    public Task SaveAsync(IProgress<string>? progress = null, IProgress<int>? itemProgress = null, string? tempFolderRoot = null) => RunOperationAsync(async () =>
    {
        try { await SaveCoreAsync(progress, itemProgress, tempFolderRoot); }
        catch
        {
            try { await RecoverCardCoreAsync(); }
            catch { LastSaveOutcome = "Recovery needs review. Save data has been preserved."; }
            throw;
        }
    });

    private async Task SaveCoreAsync(IProgress<string>? progress = null, IProgress<int>? itemProgress = null, string? tempFolderRoot = null)
    {
        if (string.IsNullOrEmpty(SdCardPath)) throw new InvalidOperationException("No SD card path set.");
        LastSaveOutcome = null;
        await RecoverCardCoreAsync();
        var tx = OpenSave();
        Mutate(() => ReconcilePreparing(tx, false));
        string root = Path.GetFullPath(SdCardPath);
        string tempRoot = Path.GetFullPath(!string.IsNullOrEmpty(tempFolderRoot) && Directory.Exists(tempFolderRoot) ? tempFolderRoot : Path.GetTempPath());
        var rows = ItemList.ToList();
        if (rows.Count == 0 || !rows[0].IsMenuItem || rows.Count(g => g.IsMenuItem) != 1)
            throw new InvalidDataException("The primary menu must be the first and only primary row.");
        var planned = rows.Select((g, i) =>
        {
            var copy = CardSaveRow.Capture(g).Create(g.SaveId);
            copy.SdNumber = i + 1;
            return copy;
        }).ToList();
        var removed = _removedItems.Where(g => !rows.Contains(g)).ToList();
        ValidateSavePaths(root, tempRoot, rows, removed);
        var mappings = planned.Select((g, i) => new CardSaveItem(g.SaveId,
            rows[i].WorkMode == WorkMode.New || (i == 0 && !Directory.Exists(rows[i].FullFolderPath)) ? null : Path.GetFileName(rows[i].FullFolderPath),
            g.FolderNumberFormatted)
        { Kind = i == 0 || g.IsLegacyRmenu ? CardSaveOutputKind.Menu : CardSaveOutputKind.Game, Row = CaptureInput(rows[i]) }).ToList();
        if (tx == null)
        {
            tx = CardSaveTransaction.Begin(root, MenuKindSelected.ToString(), mappings,
                (from, to) => FolderHelper.MoveDirectoryAsync(from, to, OnFolderLocked),
                removed.Where(g => g.SdNumber > 0).Select(g => Path.GetFileName(g.FullFolderPath)));
            await tx.RecoverAsync();
        }
        else
        {
            foreach (var incomplete in tx.GetIncompletePreparations())
            {
                if (incomplete.Value.Count != 0 && (OnDiscardIncompletePreparation == null || !await OnDiscardIncompletePreparation(incomplete.Key)))
                    throw new OperationCanceledException("Incomplete preparation preserved: " + incomplete.Key);
                tx.DiscardIncompletePreparation(incomplete.Key, incomplete.Value);
            }
            tx.Replan(MenuKindSelected.ToString(), mappings, removed.Select(g => g.SaveId).ToArray());
        }
        int processed = 0;
        foreach (var game in planned.Where(g => !g.IsMenuItem && !g.IsLegacyRmenu).ToList())
        {
            var item = tx.Items.Single(x => x.Id == game.SaveId);
            if (item.Ready)
            {
                var merged = MergeReady(rows.Single(g => g.SaveId == item.Id), item);
                merged.SdNumber = game.SdNumber;
                planned[planned.IndexOf(game)] = merged;
            }
            else if (item.OriginalName == null)
            {
                string destination = tx.GetImportPath(item.Id);
                var (expected, converted) = await PrepareImportAsync(game, destination, tx, tempRoot, progress);
                tx.MarkReady(item.Id, expected, converted, CardSaveRow.Capture(game));
            }
            itemProgress?.Report(++processed);
        }
        var metadata = new List<(string Target, string? Source, string? Text)>();
        var games = planned.Where(g => !g.IsMenuItem).ToList();
        foreach (var game in planned.Where(g => g.IsMenuItem || g.IsLegacyRmenu))
        {
            var item = tx.Items.Single(x => x.Id == game.SaveId);
            bool kai = game.IsMenuItem && MenuKindSelected != MenuKind.Rmenu;
            string list = MenuBuilder.GenerateListIni(games, UseVirtualFolderSubfolders, kai ? MenuKindSelected : MenuKind.Rmenu);
            string work = tx.GetWorkDirectory("Menu-" + item.Id);
            string output = item.OriginalName == null ? tx.GetImportPath(item.Id) : Path.Combine(work, "Output");
            Directory.CreateDirectory(output);
            string content = IsoBuilder.PrepareRmenuContent(ToolsPath, list, work, kai);
            progress?.Report("Building " + (kai ? "RmenuKai" : "RMENU") + " menu...");
            await Task.Run(() => IsoBuilder.BuildRmenuIso(content, Path.Combine(output, "RMENU.iso"), Path.Combine(ToolsPath, "shared", "IP.BIN")));
            string compatibility = Path.Combine(output, "BIN", "RMENU");
            Directory.CreateDirectory(compatibility);
            foreach (string file in Directory.GetFiles(content)) File.Copy(file, Path.Combine(compatibility, Path.GetFileName(file)), true);
            if (item.OriginalName == null) tx.MarkReady(item.Id, InventoryOutput(output), resolvedRow: CardSaveRow.Capture(game));
            else foreach (string file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
                    metadata.Add((item.FinalName + "/" + Path.GetRelativePath(output, file).Replace('\\', '/'), file, null));
        }
        foreach (var game in games.Where(g => g.SidecarsDirty)) AddSidecarActions(game, metadata);
        GameDatabase? db = null;
        if (_gameDb != null)
        {
            db = new GameDatabase();
            foreach (var game in games)
            {
                var entry = CreateDbEntry(game);
                if (entry.IsUsable) db.Items[game.FolderNumberFormatted] = entry;
            }
            metadata.Add((Constants.GameDatabaseFile, null, db.Serialize()));
        }
        metadata.Add(("GameList.txt", null, MenuBuilder.GenerateGameList(games, MenuKindSelected)));
        foreach (string name in new[] { "Rhea.ini", "Phoebe.ini" })
        {
            string source = Path.Combine(ToolsPath, "defaults", name);
            if (PathCasing.FileExistsIgnoreCase(Path.Combine(root, name)) || !File.Exists(source)) continue;
            var lines = File.ReadAllLines(source);
            if (!string.IsNullOrEmpty(PendingConsoleRegion))
                for (int i = 0; i < lines.Length; i++) if (lines[i].TrimStart().StartsWith("auto_region")) lines[i] = "auto_region = " + PendingConsoleRegion;
            metadata.Add((name, null, string.Join(Environment.NewLine, lines) + Environment.NewLine));
        }
        var paths = tx.AllocateMetadata(metadata.Select((_, i) => "Metadata-" + i).ToArray());
        var actions = new List<(string Target, string? Prepared)>();
        for (int i = 0; i < metadata.Count; i++)
        {
            var action = metadata[i];
            if (action.Source != null) File.Move(action.Source, paths[i]);
            else if (action.Text != null) File.WriteAllText(paths[i], action.Text, (action.Target == Constants.GameDatabaseFile || action.Target.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) ? new UTF8Encoding(false) : Encoding.UTF8);
            actions.Add((action.Target, action.Source == null && action.Text == null ? null : paths[i]));
        }
        tx.RecordMetadataBatch(actions, paths.Where((_, i) => metadata[i].Source == null && metadata[i].Text == null).ToArray());
        foreach (var menu in planned.Where(g => g.IsMenuItem || g.IsLegacyRmenu)) tx.SealWorkDirectory("Menu-" + menu.SaveId);
        foreach (var game in games.Where(g => g.ProductIdDirty && !string.IsNullOrWhiteSpace(g.ProductId) && !g.IsLegacyRmenu))
        {
            var item = tx.Items.Single(x => x.Id == game.SaveId);
            string location = item.OriginalName == null ? tx.GetImportPath(item.Id) : Path.Combine(root, item.OriginalName);
            var (offset, file) = IpBinParser.FindIpBinInFolder(location);
            if (offset < 0 || file == null) continue;
            byte[] desired = Encoding.ASCII.GetBytes(game.ProductId.PadRight(10)[..10]);
            tx.RecordProductIdPatch(item.FinalName + "/" + Path.GetRelativePath(location, file).Replace('\\', '/'), offset + Constants.IpOffsetProductId, desired);
            game.ProductIdDirty = false;
        }
        foreach (var game in planned) game.SidecarsDirty = false;
        tx.SetRows(planned.ToDictionary(g => g.SaveId, CardSaveRow.Capture));
        progress?.Report("Publishing folders in menu order...");
        await tx.PublishAsync();
        await tx.CommitAsync();
        Mutate(() => ApplyPublishedRows(tx.Items));
        _gameDb = db;
        _removedItems.Clear();
        Mutate(() => UndoManager.Clear());
        LastSaveOutcome = "Done!";
        progress?.Report(LastSaveOutcome);
    }

    private static Dictionary<string, long> InventoryOutput(string path)
    {
        var result = Directory.GetFiles(path, "*", SearchOption.AllDirectories).ToDictionary(p => Path.GetRelativePath(path, p).Replace('\\', '/'), p => new FileInfo(p).Length, StringComparer.OrdinalIgnoreCase);
        foreach (string dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)) result.Add(Path.GetRelativePath(path, dir).Replace('\\', '/') + "/", -1);
        return result;
    }

    private static void AddSidecarActions(SaturnGame game, List<(string Target, string? Source, string? Text)> actions)
    {
        var values = new Dictionary<string, string?>
        {
            [Constants.NameFile] = game.Name,
            [Constants.DiscFile] = game.Disc,
            [Constants.RegionFile] = game.Region,
            [Constants.VersionFile] = game.Version,
            [Constants.DateFile] = game.ReleaseDate,
            [Constants.ProductIdFile] = game.ProductId,
            [Constants.FolderFile] = string.IsNullOrWhiteSpace(game.Folder) ? null : game.Folder.Replace('\\', '/')
        };
        for (int i = 0; i < Constants.FolderAltFiles.Length; i++)
            values[Constants.FolderAltFiles[i]] = i < game.AlternativeFolders.Count ? game.AlternativeFolders[i].Replace('\\', '/') : null;
        foreach (var value in values) actions.Add((game.FolderNumberFormatted + "/" + value.Key, null, value.Value));
    }

    private async Task<(Dictionary<string, long> Files, bool Converted)> PrepareImportAsync(SaturnGame game, string destination, CardSaveTransaction tx, string tempRoot, IProgress<string>? progress)
    {
        bool usesWork = game.FileFormat is FileFormat.Compressed or FileFormat.Chd;
        bool owned = IsWithin(tempRoot, SdCardPath);
        string? work = usesWork ? (owned ? tx.GetWorkDirectory("Import-" + game.SaveId) : Path.Combine(tempRoot, "OrbitalOrganizer_" + Guid.NewGuid().ToString("N"))) : null;
        bool completed = false;
        try
        {
            var inputs = game.ImageFiles.ToList();
            string? extracted = null;
            if (game.FileFormat == FileFormat.Compressed)
            {
                if (!File.Exists(game.SourcePath)) throw new FileNotFoundException("Archive file not found.", game.SourcePath);
                extracted = Path.Combine(work!, "Extracted");
                await Task.Run(() =>
                {
                    if (game.SelectedArchiveEntry != null) ArchiveHelper.ExtractArchiveForEntry(game.SourcePath, extracted, game.SelectedArchiveEntry);
                    else ArchiveHelper.ExtractArchive(game.SourcePath, extracted);
                });
                inputs = Directory.GetFiles(extracted).Where(IsImagePath).ToList();
            }
            if (game.FileFormat == FileFormat.CloneCd || game.InnerFileFormat == FileFormat.CloneCd)
            {
                string ccd = inputs.FirstOrDefault(p => Path.GetExtension(p).Equals(".ccd", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("No source CCD for " + game.Name);
                var companions = new[] { ccd, Path.ChangeExtension(ccd, ".img"), Path.ChangeExtension(ccd, ".sub") };
                inputs = inputs.Where(p => companions.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            }
            else if (game.FileFormat == FileFormat.CueBin)
            {
                string cueFile = inputs.FirstOrDefault(p => Path.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("No source CUE for " + game.Name);
                var related = CueSheetParser.GetReferencedFileNames(File.ReadAllText(cueFile))
                    .Select(p => Path.Combine(Path.GetDirectoryName(cueFile)!, p)).Prepend(cueFile).ToArray();
                inputs = inputs.Where(p => related.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            }
            else if (game.FileFormat == FileFormat.Chd)
                inputs = inputs.Where(p => Path.GetExtension(p).Equals(".chd", StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
            if (inputs.Count == 0) throw new InvalidDataException("No source images for " + game.Name);
            foreach (string file in inputs) if (!File.Exists(file)) throw new FileNotFoundException("Source image is missing.", file);
            string? chd = game.FileFormat is FileFormat.Chd or FileFormat.Compressed
                ? inputs.FirstOrDefault(p => Path.GetExtension(p).Equals(".chd", StringComparison.OrdinalIgnoreCase)) : null;
            string? cue = game.FileFormat is FileFormat.CueBin or FileFormat.Compressed
                ? inputs.FirstOrDefault(p => Path.GetExtension(p).Equals(".cue", StringComparison.OrdinalIgnoreCase)) : null;
            if (chd != null)
            {
                var converted = await ChdConverter.ConvertToCueBinAsync(chd, Path.Combine(work!, "Cue"), progress, gameName: game.Name);
                if (!converted.Success || converted.CuePath == null) throw new InvalidOperationException("CHD conversion failed for " + game.Name + ": " + converted.Message);
                cue = converted.CuePath;
            }
            Dictionary<string, long> expected;
            if (cue != null)
            {
                string name = Path.GetFileNameWithoutExtension(cue);
                if (!await Cue2CcdConverter.ConvertAsync(cue, destination, progress)) throw new InvalidOperationException("CUE conversion failed for " + game.Name);
                expected = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (string ext in new[] { ".ccd", ".img", ".sub" })
                {
                    string path = Path.Combine(destination, name + ext);
                    if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException("Converted companion is missing: " + path);
                    expected[name + ext] = new FileInfo(path).Length;
                }
            }
            else
            {
                expected = inputs.ToDictionary(p => Path.GetFileName(p), p => new FileInfo(p).Length, StringComparer.OrdinalIgnoreCase)!;
                foreach (string file in inputs)
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    string? companion = ext == ".ccd" ? ".img" : ext == ".mds" ? ".mdf" : null;
                    if (companion != null && !expected.ContainsKey(Path.GetFileNameWithoutExtension(file) + companion)) throw new InvalidDataException("Missing source companion for " + file);
                }
                foreach (string file in inputs) await Task.Run(() => File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false));
            }
            if (game.IsArchiveMetadataPending && extracted != null) ResolveDeferredArchiveMetadata(game, destination, extracted);
            game.FullFolderPath = destination;
            game.WorkMode = WorkMode.New;
            game.FileFormat = FileFormat.Uncompressed;
            game.InnerFileFormat = null;
            game.SelectedArchiveEntry = null;
            game.ImageFiles = expected.Keys.Select(p => Path.Combine(destination, p)).ToList();
            game.Length = expected.Values.Sum();
            if (owned && work != null) tx.SealWorkDirectory("Import-" + game.SaveId);
            completed = true;
            return (expected, cue != null);
        }
        finally
        {
            if (!owned && work != null) { try { Directory.Delete(work, true); } catch { } }
            if (!completed) progress?.Report("Preparation is incomplete. Existing folders have not been published.");
        }
    }

    private static bool IsImagePath(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return Constants.AllImageExtensions.Contains(ext) || ext is ".img" or ".sub" or ".bin" or ".mdf" or ".chd";
    }

    private static bool IsWithin(string path, string parent) => string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void ValidateSavePaths(string root, string temp, List<SaturnGame> rows, List<SaturnGame> removed)
    {
        var originals = rows.Concat(removed).Where(g => g.WorkMode != WorkMode.New && !string.IsNullOrEmpty(g.FullFolderPath)).Distinct().ToList();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in originals)
        {
            string path = Path.GetFullPath(game.FullFolderPath);
            if (!string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new InvalidDataException("Overlapping or non-root game folder: " + path);
        }
        foreach (string directory in Directory.EnumerateDirectories(root))
            if (int.TryParse(Path.GetFileName(directory), out _) && !paths.Contains(directory))
                throw new InvalidDataException("Unowned numbered folder requires review: " + directory);
        var affected = paths.Append(Path.Combine(root, CardSaveTransaction.WorkName)).ToArray();
        foreach (var game in rows.Where(g => g.WorkMode == WorkMode.New && !g.IsLegacyRmenu && g.PreparedSource == null))
            foreach (string source in game.ImageFiles.Append(game.SourcePath).Where(p => !string.IsNullOrEmpty(p)))
                if (affected.Any(p => IsWithin(source, p))) throw new InvalidDataException("Import source overlaps card data: " + source);
        foreach (string path in affected.Concat(rows.SelectMany(g => g.ImageFiles)).Concat(rows.Select(g => g.SourcePath).Where(p => !string.IsNullOrEmpty(p))))
            if (IsWithin(temp, path)) throw new InvalidDataException("Temporary directory overlaps card or source data: " + temp);
    }

    private static CardSaveRow CaptureInput(SaturnGame game)
    {
        var row = CardSaveRow.Capture(game);
        var source = game.PreparedSource;
        if (source != null && game.SourcePath == source.SourcePath && game.ImageFiles.All(p => IsWithin(p, game.FullFolderPath)))
        {
            row.FileFormat = source.FileFormat;
            row.InnerFileFormat = source.InnerFileFormat;
            row.SelectedArchiveEntry = source.SelectedArchiveEntry;
            row.ImageFiles = source.ImageFiles.ToList();
        }
        return row;
    }

    private static SaturnGame MergeReady(SaturnGame live, CardSaveItem item)
    {
        if (item.InputRow == null || item.ResolvedRow == null) throw new InvalidDataException("Prepared import has no saved row: " + item.Id);
        var input = item.InputRow;
        var resolved = item.ResolvedRow;
        var row = CardSaveRow.Capture(live).Create(item.Id);
        if (live.PreparedSource == null)
        {
            if (live.Name == input.Name) row.Name = resolved.Name;
            if (live.Folder == input.Folder) row.Folder = resolved.Folder;
            if (live.ProductId == input.ProductId) row.ProductId = resolved.ProductId;
            if (live.Disc == input.Disc) row.Disc = resolved.Disc;
            if (live.Region == input.Region) row.Region = resolved.Region;
            if (live.Version == input.Version) row.Version = resolved.Version;
            if (live.ReleaseDate == input.ReleaseDate) row.ReleaseDate = resolved.ReleaseDate;
            if (live.AlternativeFolders.SequenceEqual(input.AlternativeFolders)) row.AlternativeFolders = resolved.AlternativeFolders.ToList();
        }
        row.Ip = resolved.Create(item.Id).Ip;
        row.FileFormat = resolved.FileFormat;
        row.InnerFileFormat = null;
        row.SelectedArchiveEntry = null;
        row.ImageFiles = resolved.ImageFiles.ToList();
        row.Length = resolved.Length;
        row.NeedsMetadataScan = resolved.NeedsMetadataScan;
        row.IsArchiveMetadataPending = false;
        row.PendingArchiveValues = null;
        return row;
    }

    private void ReconcilePreparing(CardSaveTransaction? tx, bool loading)
    {
        if (tx == null || tx.HasCommitIntent) return;
        var order = new List<SaturnGame>();
        foreach (var item in tx.Items)
        {
            if (_removedItems.Any(g => g.SaveId == item.Id)) continue;
            var current = ItemList.FirstOrDefault(g => g.SaveId == item.Id || (item.FinalName == "01" && g.IsMenuItem)) ??
                ItemList.FirstOrDefault(g => item.OriginalName != null && Path.GetFileName(g.FullFolderPath) == item.OriginalName);
            if (current != null)
            {
                if (loading && item.OriginalName != null && item.Row != null)
                {
                    current = item.Row.Create(item.Id);
                    current.FullFolderPath = Path.Combine(SdCardPath, item.OriginalName);
                    current.SdNumber = int.Parse(item.OriginalName);
                    current.WorkMode = WorkMode.None;
                    current.ImageFiles = item.Row.ImageFiles.Select(p => Path.Combine(current.FullFolderPath, Path.GetFileName(p))).ToList();
                    current.SidecarsDirty = !current.IsMenuItem;
                    current.ProductIdDirty |= item.InputRow?.ProductIdDirty == true || tx.HasProductIdPatch(item.Id);
                }
                current.SaveId = item.Id;
                order.Add(current);
            }
            else if (loading && item.Row != null)
            {
                current = item.Row.Create(item.Id);

                current.SdNumber = item.FinalName == "01" ? 1 : 0;
                current.WorkMode = WorkMode.New;
                current.FullFolderPath = Path.Combine(SdCardPath, CardSaveTransaction.WorkName, item.StageName);
                if (item.Ready && item.Kind == CardSaveOutputKind.Game)
                {
                    current.PreparedSource = item.InputRow;
                    current.SidecarsDirty = true;
                    current.ProductIdDirty |= item.InputRow?.ProductIdDirty == true || tx.HasProductIdPatch(item.Id);
                    current.ImageFiles = item.ResolvedRow!.ImageFiles.Select(p => Path.Combine(current.FullFolderPath, Path.GetFileName(p))).ToList();
                }
                order.Add(current);
            }
        }
        if (loading)
        {
            foreach (var game in ItemList.Where(g => !order.Any(x => x.SaveId == g.SaveId || (x.SdNumber > 0 && x.FullFolderPath == g.FullFolderPath)) &&
                !_removedItems.Any(x => x.SaveId == g.SaveId || (x.SdNumber > 0 && x.FullFolderPath == g.FullFolderPath)))) order.Add(game);
            ItemList.Clear();
            foreach (var game in order) ItemList.Add(game);
            MenuKindSelected = Enum.Parse<MenuKind>(tx.SelectedMenuKind);
        }
    }

    private void ApplyReadyRows(IReadOnlyList<CardSaveItem> items)
    {
        foreach (var item in items.Where(x => x.Ready && x.Kind == CardSaveOutputKind.Game))
        {
            var live = ItemList.FirstOrDefault(g => g.SaveId == item.Id);
            if (live == null) continue;
            var ready = MergeReady(live, item);
            live.Name = ready.Name; live.Folder = ready.Folder; live.ProductId = ready.ProductId; live.Disc = ready.Disc;
            live.Region = ready.Region; live.Version = ready.Version; live.ReleaseDate = ready.ReleaseDate;
            live.AlternativeFolders = ready.AlternativeFolders.ToList();
            live.Ip = ready.Ip; live.Length = ready.Length; live.NeedsMetadataScan = ready.NeedsMetadataScan;
            live.SidecarsDirty = true; live.ProductIdDirty = ready.ProductIdDirty;
            live.PreparedSource = item.InputRow;
            live.FullFolderPath = Path.Combine(SdCardPath, CardSaveTransaction.WorkName, item.StageName);
            live.ImageFiles = item.ResolvedRow!.ImageFiles.Select(p => Path.Combine(live.FullFolderPath, Path.GetFileName(p))).ToList();
            live.FileFormat = item.ResolvedRow.FileFormat; live.InnerFileFormat = null; live.SelectedArchiveEntry = null;
            live.IsArchiveMetadataPending = false; live.PendingArchiveValues = null;
            live.SdNumber = 0; live.WorkMode = WorkMode.New;
        }
    }

    private void ApplyPublishedRows(IReadOnlyList<CardSaveItem> items)
    {
        foreach (var item in items)
        {
            var live = ItemList.FirstOrDefault(g => g.SaveId == item.Id);
            if (live == null || item.Row == null) continue;
            var row = item.Row;
            live.Name = row.Name; live.Folder = row.Folder; live.ProductId = row.ProductId; live.Disc = row.Disc;
            live.Region = row.Region; live.Version = row.Version; live.ReleaseDate = row.ReleaseDate;
            live.AlternativeFolders = row.AlternativeFolders.ToList();
            live.Ip = row.Create(item.Id).Ip; live.NeedsMetadataScan = row.NeedsMetadataScan;
            live.ProductIdDirty = row.ProductIdDirty; live.SidecarsDirty = row.SidecarsDirty;
            live.SdNumber = int.Parse(item.FinalName); live.FullFolderPath = Path.Combine(SdCardPath, item.FinalName);
            live.PreparedSource = null;
            live.WorkMode = WorkMode.None; live.IsArchiveMetadataPending = false; live.PendingArchiveValues = null;
            live.InnerFileFormat = null; live.SelectedArchiveEntry = null;
            live.ImageFiles = row.ImageFiles.Select(p => Path.Combine(live.FullFolderPath, Path.GetFileName(p))).ToList();
            live.FileFormat = row.FileFormat; live.Length = row.Length;
        }
    }
}
