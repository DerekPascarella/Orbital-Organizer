using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

[assembly: InternalsVisibleTo("OrbitalOrganizer.Core.Tests")]

namespace OrbitalOrganizer.Core.Services;

internal enum CardSaveOutputKind { Game, Menu }

internal sealed record CardSaveItem([property: JsonRequired] string Id, [property: JsonRequired] string? OriginalName, [property: JsonRequired] string FinalName)
{
    [JsonRequired]
    public string StageName { get; set; } = "";
    [JsonRequired]
    public bool Ready { get; set; }
    [JsonRequired]
    public CardSaveOutputKind Kind { get; set; }
    [JsonRequired]
    public Dictionary<string, long>? Files { get; set; }
    public CardSaveRow? Row { get; set; }
    public CardSaveRow? InputRow { get; set; }
    public CardSaveRow? ResolvedRow { get; set; }
}

internal sealed class CardSaveTransaction
{
    internal const string JournalName = "OrbitalSave.json";
    internal const string WorkName = "OrbitalSave";
    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    private bool deferPersist;
    private readonly string root;
    private readonly Record record;
    private readonly Func<string, string, Task> moveDirectory;

    internal enum Phase { Preparing, Staging, Publishing, Published, Restaging, Restoring, RestoreStaging, OrderStaging, OrderPublishing, Committing, Cleaning }

    internal sealed class Record
    {
        [JsonRequired]
        public int Version { get; set; }
        [JsonRequired]
        public string Id { get; set; } = "";
        [JsonRequired]
        public int Revision { get; set; }
        [JsonRequired]
        public string MenuKind { get; set; } = "";
        [JsonRequired]
        public string Owner { get; set; } = "OrbitalOrganizer";
        [JsonRequired]
        public bool MovesRequired { get; set; }
        [JsonRequired]
        public int OrderPass { get; set; }
        [JsonRequired]
        public int RestorePass { get; set; }
        [JsonRequired]
        public List<ProductIdPatch> Patches { get; set; } = new();
        [JsonRequired]
        public Phase Phase { get; set; }
        [JsonRequired]
        public List<CardSaveItem> Items { get; set; } = new();
        [JsonRequired]
        public List<string> OriginalOrder { get; set; } = new();
        [JsonRequired]
        public List<Metadata> Metadata { get; set; } = new();
        [JsonRequired]
        public List<string> Artifacts { get; set; } = new();
        [JsonRequired]
        public Dictionary<string, Dictionary<string, long>?> WorkDirectories { get; set; } = new();
        [JsonRequired]
        public Dictionary<string, Dictionary<string, long>> Discarding { get; set; } = new();
        [JsonRequired]
        public Dictionary<string, Dictionary<string, long>> Retiring { get; set; } = new();
        [JsonRequired]
        public Dictionary<string, string> RetiringHashes { get; set; } = new();
    }

    internal sealed class Metadata
    {
        [JsonRequired]
        public string Target { get; set; } = "";
        [JsonRequired]
        public string? Prepared { get; set; }
        [JsonRequired]
        public string? BeforeHash { get; set; }
        [JsonRequired]
        public string? AfterHash { get; set; }
    }

    internal sealed class ProductIdPatch
    {
        [JsonRequired]
        public string Target { get; set; } = "";
        [JsonRequired]
        public long Offset { get; set; }
        [JsonRequired]
        public byte[] Before { get; set; } = Array.Empty<byte>();
        [JsonRequired]
        public byte[] After { get; set; } = Array.Empty<byte>();
    }

    private CardSaveTransaction(string root, Record record, Func<string, string, Task>? moveDirectory)
    {
        this.root = Path.GetFullPath(root);
        this.record = record;
        this.moveDirectory = moveDirectory ?? ((from, to) => FolderHelper.MoveDirectoryAsync(from, to));
    }

    internal int InventoryReadCount { get; private set; }
    internal int MetadataHashReadCount { get; private set; }
    internal IReadOnlyList<CardSaveItem> Items => record.Items;
    internal string SelectedMenuKind => record.MenuKind;
    internal bool MovesRequired => record.MovesRequired;
    internal bool HasProductIdPatch(string id) => record.Patches.Any(x => x.Target.StartsWith(Item(id).FinalName + "/", StringComparison.OrdinalIgnoreCase));
    internal bool HasCommitIntent => record.Phase is Phase.Committing or Phase.Cleaning;

    internal static CardSaveTransaction Begin(string root, string menuKind, IEnumerable<CardSaveItem> items, Func<string, string, Task>? moveDirectory = null, IEnumerable<string>? removals = null)
    {
        var record = new Record { Version = 1, Id = Guid.NewGuid().ToString("N"), MenuKind = menuKind };
        record.Items = items.Select(x => new CardSaveItem(x.Id, x.OriginalName, x.FinalName)
        {
            StageName = "Item-" + Guid.NewGuid().ToString("N"),
            Kind = x.Kind,
            Row = x.Row,
            InputRow = x.Row
        }).OrderBy(x => int.TryParse(x.FinalName, out int n) ? n : 0).ToList();
        record.MovesRequired = record.Items.Any(x => !Names.Equals(x.OriginalName, x.FinalName)) || !CardOrder.IsCardOrdered(root);
        var tx = new CardSaveTransaction(root, record, moveDirectory);
        tx.ValidateRecord();
        tx.CheckPath("");
        if (!Directory.Exists(tx.root)) throw new DirectoryNotFoundException(tx.root);
        if (Path.Exists(tx.At(JournalName)) || Path.Exists(tx.At(WorkName)))
            throw new IOException("Reserved save paths are occupied. Recover or review them first.");
        tx.CheckLegacyPaths();
        foreach (string name in removals ?? Array.Empty<string>())
        {
            ValidateNumberedName(name);
            if (Reserved(name) || record.Items.Any(x => Names.Equals(x.OriginalName, name)))
                throw new InvalidDataException("Removal overlaps a retained folder: " + name);
            record.Retiring.Add(name, tx.Inventory(name));
        }
        var unknown = Directory.EnumerateDirectories(tx.root).Where(path =>
            int.TryParse(Path.GetFileName(path), out _) &&
            !record.Items.Any(x => Names.Equals(x.OriginalName, Path.GetFileName(path))) &&
            !record.Retiring.ContainsKey(Path.GetFileName(path))).ToArray();
        if (unknown.Length != 0) throw new InvalidDataException("Unowned numbered folders require review: " + string.Join(", ", unknown));
        foreach (var item in record.Items.Where(x => x.OriginalName != null))
        {
            tx.CheckExistingDirectory(item.OriginalName!);
            if (record.MovesRequired) item.Files = tx.Inventory(item.OriginalName!);
        }
        foreach (var item in record.Items)
        {
            if (Path.Exists(tx.At(item.FinalName)) && !record.Items.Any(x => Names.Equals(x.OriginalName, item.FinalName)) && !record.Retiring.ContainsKey(item.FinalName))
                throw new IOException("Destination is occupied: " + tx.At(item.FinalName));
        }
        record.OriginalOrder = Directory.EnumerateDirectories(tx.root).Select(Path.GetFileName)
            .Where(name => record.Items.Any(x => Names.Equals(x.OriginalName, name))).Cast<string>().ToList();
        tx.ValidateRecord();
        tx.WriteInitial();
        Directory.CreateDirectory(tx.At(WorkName));
        return tx;
    }

    internal static CardSaveTransaction? Open(string root, Func<string, string, Task>? moveDirectory = null)
    {
        var probe = new CardSaveTransaction(root, new Record(), moveDirectory);
        probe.CheckPath("");
        probe.CheckPath(JournalName);
        if (!File.Exists(probe.At(JournalName)))
        {
            if (Path.Exists(probe.At(JournalName)) || Path.Exists(probe.At(WorkName)))
                throw new InvalidDataException("Save data has no readable journal: " + probe.At(WorkName));
            probe.CheckLegacyPaths();
            return null;
        }
        probe.CheckLegacyPaths();
        Record record = ReadRecord(probe.At(JournalName));
        var tx = new CardSaveTransaction(root, record, moveDirectory);
        tx.ValidateRecord();
        if (record.OriginalOrder.Count != record.Items.Count(x => x.OriginalName != null) ||
            record.Items.Any(x => (record.MovesRequired && x.OriginalName != null || x.Ready) && x.Files == null))
            throw new InvalidDataException("Incomplete folder ownership records.");
        tx.ValidateWork();
        return tx;
    }

    internal void Replan(string menuKind, IEnumerable<CardSaveItem> desired, IReadOnlyCollection<string> removedIds)
    {
        Require(Phase.Preparing);
        ValidateWork();
        FinishRetiring();
        var next = desired.OrderBy(x => int.Parse(x.FinalName)).ToList();
        foreach (var old in record.Items)
        {
            var replacement = next.SingleOrDefault(x => Names.Equals(x.Id, old.Id));
            if (replacement == null && !removedIds.Contains(old.Id))
                throw new InvalidDataException("Recorded item is absent without explicit removal: " + old.Id);
            if (replacement != null && (replacement.OriginalName != old.OriginalName ||
                (old.Ready && old.Kind == CardSaveOutputKind.Game && old.InputRow != null &&
                 (replacement.Row == null || !old.InputRow.SameSource(replacement.Row)))))
                throw new InvalidDataException("Prepared source selection changed: " + old.Id);
            if (old.Files != null) MatchFiles(Inventory(old.OriginalName ?? Stage(old)), old.Files, old.Id);
        }
        if (record.WorkDirectories.Any(x => x.Value == null) ||
            record.Artifacts.Any(x => !record.Metadata.Any(m => Names.Equals(m.Prepared, x))))
            throw new InvalidOperationException("Confirm incomplete preparation cleanup before replanning.");
        var retiring = new Dictionary<string, Dictionary<string, long>>(Names);
        var hashes = new Dictionary<string, string>(Names);
        foreach (var action in record.Metadata.Where(x => x.Prepared != null))
        {
            if (!Names.Equals(Hash(action.Prepared!), action.AfterHash))
                throw new InvalidDataException("Prepared metadata changed: " + action.Prepared);
            retiring.Add(action.Prepared!, PreparationInventory(action.Prepared!));
            hashes.Add(action.Prepared!, action.AfterHash!);
        }
        foreach (var work in record.WorkDirectories) retiring.Add(work.Key, work.Value!);
        bool moves = next.Any(x => !Names.Equals(x.OriginalName, x.FinalName)) || !CardOrder.IsCardOrdered(root);
        var planned = next.Select(x =>
        {
            var old = record.Items.SingleOrDefault(o => Names.Equals(o.Id, x.Id));
            bool resetMenu = old?.Kind == CardSaveOutputKind.Menu && old.OriginalName == null;
            if (resetMenu) retiring.Add(Stage(old!), old!.Files ?? PreparationInventory(Stage(old)));
            return x with
            {
                StageName = resetMenu ? "Item-" + Guid.NewGuid().ToString("N") : old?.StageName ?? "Item-" + Guid.NewGuid().ToString("N"),
                Ready = !resetMenu && (old?.Ready ?? false),
                Files = x.OriginalName == null ? (resetMenu ? null : old?.Files) : (moves ? old?.Files ?? Inventory(x.OriginalName) : null),
                InputRow = old?.InputRow ?? x.Row,
                ResolvedRow = old?.ResolvedRow
            };
        }).ToList();
        foreach (var old in record.Items.Where(x => !planned.Any(n => Names.Equals(n.Id, x.Id))))
            retiring.Add(old.OriginalName ?? Stage(old), old.Files ?? PreparationInventory(old.OriginalName ?? Stage(old)));
        var originals = planned.Where(x => x.OriginalName != null).Select(x => x.OriginalName!).ToHashSet(Names);
        foreach (string path in Directory.EnumerateDirectories(root))
            if (int.TryParse(Path.GetFileName(path), out _) && !originals.Contains(Path.GetFileName(path)) && !retiring.ContainsKey(Path.GetFileName(path)))
                throw new InvalidDataException("Unowned numbered folder requires review: " + path);
        record.Items = planned;
        record.MenuKind = menuKind;
        record.MovesRequired = moves;
        record.OrderPass = 0;
        record.RestorePass = 0;
        record.OriginalOrder = Directory.EnumerateDirectories(root).Select(Path.GetFileName).Where(x => originals.Contains(x!)).Cast<string>().ToList();
        record.Metadata.Clear();
        record.Artifacts.Clear();
        record.WorkDirectories.Clear();
        record.Patches.Clear();
        record.Retiring = retiring;
        record.RetiringHashes = hashes;
        ValidateRecord();
        Persist();
        FinishRetiring();
    }

    internal void SetRows(IReadOnlyDictionary<string, CardSaveRow> rows)
    {
        Require(Phase.Preparing);
        foreach (var item in record.Items) item.Row = rows[item.Id];
        Persist();
    }

    private void FinishRetiring()
    {
        if (record.Retiring.Count == 0) return;
        foreach (var entry in record.Retiring)
        {
            CheckPath(entry.Key);
            if (entry.Key.StartsWith(WorkName + "/File-", StringComparison.Ordinal))
            {
                if (File.Exists(At(entry.Key)))
                {
                    if (entry.Value.Count != 1 || entry.Value[""] != new FileInfo(At(entry.Key)).Length ||
                        !record.RetiringHashes.TryGetValue(entry.Key, out string? hash) || !Names.Equals(hash, Hash(entry.Key)))
                        throw new InvalidDataException("Retired metadata changed: " + entry.Key);
                    File.Delete(At(entry.Key));
                }
            }
            else DeleteRecordedTree(entry.Key, entry.Value);
        }
        record.Retiring.Clear();
        record.RetiringHashes.Clear();
        Persist();
    }

    internal string GetImportPath(string id)
    {
        var item = Item(id);
        if (item.OriginalName != null) throw new InvalidOperationException("An existing game is not an import.");
        if (record.Discarding.Count != 0) throw new InvalidOperationException("Recover an interrupted discard first.");
        if (record.Phase != Phase.Preparing) throw new InvalidOperationException("Recover before preparing imports.");
        string path = Stage(item);
        CheckPath(path);
        Directory.CreateDirectory(At(path));
        return At(path);
    }

    internal void MarkReady(string id, IReadOnlyDictionary<string, long> expectedFiles, bool requireSub = false, CardSaveRow? resolvedRow = null)
    {
        Require(Phase.Preparing);
        var item = Item(id);
        if (item.OriginalName != null || item.Ready) throw new InvalidOperationException("Import cannot be prepared again.");
        var files = new Dictionary<string, long>(expectedFiles, Names);
        ValidateFiles(files);
        var actual = Inventory(Stage(item));
        MatchFiles(actual, files, Stage(item));
        if (item.Kind == CardSaveOutputKind.Menu)
        {
            if (!files.TryGetValue("RMENU.iso", out long isoSize) || isoSize <= 0 ||
                !files.TryGetValue("BIN/RMENU/LIST.INI", out long listSize) || listSize <= 0)
                throw new InvalidDataException("Generated menu output is incomplete.");
        }
        else
        {
            if (!files.Any(x => Constants.DiscImageExtensions.Contains(Path.GetExtension(x.Key).ToLowerInvariant()) && x.Value > 0))
                throw new InvalidDataException("A prepared game has no complete disc image.");
            foreach (string file in files.Keys)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".ccd")
                {
                    RequireCompanion(files, file, ".img");
                    if (requireSub) RequireCompanion(files, file, ".sub");
                }
                if (ext == ".mds") RequireCompanion(files, file, ".mdf");
                if (ext is ".cue" or ".chd") throw new InvalidDataException("Import conversion is incomplete: " + file);
            }
        }
        item.Files = files;
        item.Ready = true;
        item.ResolvedRow = resolvedRow;
        if (resolvedRow != null) item.Row = resolvedRow;
        Persist();
    }

    internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> GetIncompletePreparations()
    {
        Require(Phase.Preparing);
        ValidateWork();
        var paths = record.Items.Where(x => x.OriginalName == null && !x.Ready).Select(Stage)
            .Concat(record.WorkDirectories.Where(x => x.Value == null).Select(x => x.Key))
            .Concat(record.Artifacts.Where(x => !record.Metadata.Any(m => Names.Equals(m.Prepared, x))));
        return paths.ToDictionary(At, x => (IReadOnlyDictionary<string, long>)PreparationInventory(x), Names);
    }

    internal void DiscardIncompletePreparation(string path, IReadOnlyDictionary<string, long> confirmedFiles)
    {
        Require(Phase.Preparing);
        string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        if (!IsIncomplete(relative)) throw new InvalidOperationException("Only recorded incomplete preparation can be discarded.");
        var files = new Dictionary<string, long>(confirmedFiles, Names);
        MatchFiles(PreparationInventory(relative), files, relative);
        record.Discarding[relative] = files;
        Persist();
        FinishDiscards();
    }

    private bool IsIncomplete(string path) => record.Items.Any(x => Stage(x) == path && x.OriginalName == null && !x.Ready) ||
        (record.WorkDirectories.TryGetValue(path, out var files) && files == null) ||
        (record.Artifacts.Contains(path, Names) && !record.Metadata.Any(x => Names.Equals(x.Prepared, path)));

    private Dictionary<string, long> PreparationInventory(string path)
    {
        CheckPath(path);
        if (!Path.Exists(At(path))) return new Dictionary<string, long>(Names);
        return record.Artifacts.Contains(path, Names)
            ? new Dictionary<string, long> { [""] = new FileInfo(At(path)).Length }
            : Inventory(path);
    }

    internal string GetWorkDirectory(string id)
    {
        Require(Phase.Preparing);
        ValidateName(id);
        string path = WorkName + "/Work-" + id;
        CheckPath(path);
        if (!record.WorkDirectories.ContainsKey(path))
        {
            if (Path.Exists(At(path))) throw new InvalidDataException("Work path is occupied: " + At(path));
            record.WorkDirectories.Add(path, null);
            Persist();
        }
        Directory.CreateDirectory(At(path));
        return At(path);
    }

    internal void SealWorkDirectory(string id)
    {
        Require(Phase.Preparing);
        ValidateName(id);
        string path = WorkName + "/Work-" + id;
        if (!record.WorkDirectories.TryGetValue(path, out var files) || files != null)
            throw new InvalidOperationException("Work directory is not awaiting completion.");
        record.WorkDirectories[path] = Inventory(path);
        Persist();
    }

    internal void DiscardIncompleteImport(string id, IReadOnlyDictionary<string, long> confirmedFiles)
    {
        var item = Item(id);
        if (item.OriginalName != null || item.Ready) throw new InvalidOperationException("Only incomplete imports can be discarded.");
        DiscardIncompletePreparation(At(Stage(item)), confirmedFiles);
    }

    private void FinishDiscards()
    {
        if (record.Discarding.Count == 0) return;
        foreach (var discard in record.Discarding)
        {
            if (record.Artifacts.Contains(discard.Key, Names))
            {
                CheckPath(discard.Key);
                if (Path.Exists(At(discard.Key)))
                {
                    MatchFiles(PreparationInventory(discard.Key), discard.Value, discard.Key);
                    File.Delete(At(discard.Key));
                }
                record.Artifacts.Remove(discard.Key);
            }
            else
            {
                DeleteRecordedTree(discard.Key, discard.Value);
                record.WorkDirectories.Remove(discard.Key);
            }
        }
        record.Discarding.Clear();
        Persist();
    }

    private void DeleteRecordedTree(string path, Dictionary<string, long> files)
    {
        CheckPath(path);
        if (!Directory.Exists(At(path))) return;
        var actual = Inventory(path);
        if (actual.Any(x => !files.TryGetValue(x.Key, out long size) || size != x.Value))
            throw new InvalidDataException("Unrecorded contents prevent cleanup: " + At(path));
        foreach (string name in actual.Keys.Where(x => !x.EndsWith('/'))) File.Delete(At(path + "/" + name));
        foreach (string name in actual.Keys.Where(x => x.EndsWith('/')).OrderByDescending(x => x.Length)) Directory.Delete(At(path + "/" + name));
        Directory.Delete(At(path));
    }

    internal IReadOnlyList<string> AllocateMetadata(IReadOnlyList<string> ids)
    {
        deferPersist = true;
        List<string> paths;
        try { paths = ids.Select(GetMetadataPath).ToList(); }
        finally { deferPersist = false; }
        Persist();
        return paths;
    }

    internal void RecordMetadataBatch(IEnumerable<(string Target, string? Prepared)> actions, IReadOnlyList<string> unused)
    {
        deferPersist = true;
        try
        {
            foreach (string path in unused)
                record.Artifacts.Remove(Path.GetRelativePath(root, path).Replace('\\', '/'));
            foreach (var action in actions) RecordMetadata(action.Target, action.Prepared);
        }
        finally { deferPersist = false; }
        Persist();
    }

    internal string GetMetadataPath(string id)
    {
        Require(Phase.Preparing);
        ValidateName(id);
        string path = WorkName + "/File-" + id;
        if (record.Artifacts.Contains(path, Names)) throw new InvalidOperationException("Metadata artifact already exists: " + id);
        if (CheckPath(path) != null) throw new IOException("Metadata path is occupied: " + At(path));
        record.Artifacts.Add(path);
        Persist();
        return At(path);
    }

    internal void RecordMetadata(string target, string? prepared, string? originalTarget = null)
    {
        Require(Phase.Preparing);
        ValidateTarget(target);
        if (record.Metadata.Any(x => Names.Equals(x.Target, target))) throw new InvalidDataException("Duplicate metadata target: " + target);
        string? relative = prepared == null ? null : Path.GetRelativePath(root, prepared).Replace('\\', '/');
        if (relative != null && !record.Artifacts.Contains(relative, Names)) throw new InvalidDataException("Unowned metadata source: " + prepared);
        if (relative != null && record.Metadata.Any(x => Names.Equals(x.Prepared, relative))) throw new InvalidDataException("Metadata source is already used.");
        string expectedBefore = OriginalTarget(target);
        string before = originalTarget ?? expectedBefore;
        ValidateRelative(before);
        if (!Names.Equals(before, expectedBefore)) throw new InvalidDataException("Metadata original does not match the folder mapping: " + before);
        string? beforeHash = Hash(before);
        string? afterHash = relative == null ? null : Hash(relative);
        if (relative != null && afterHash == null) throw new FileNotFoundException("Prepared metadata is missing.", prepared);
        record.Metadata.Add(new Metadata { Target = target, Prepared = relative, BeforeHash = beforeHash, AfterHash = afterHash });
        Persist();
    }

    private string OriginalTarget(string target)
    {
        int slash = target.IndexOf('/');
        var item = slash < 0 ? null : record.Items.SingleOrDefault(x => Names.Equals(x.FinalName, target[..slash]));
        return item == null ? target : (item.OriginalName ?? Stage(item)) + target[slash..];
    }

    internal void RecordProductIdPatch(string target, long offset, byte[] desired)
    {
        Require(Phase.Preparing);
        ValidatePatchTarget(target, offset, desired);
        if (record.Patches.Any(x => Names.Equals(x.Target, target))) throw new InvalidDataException("Duplicate image patch.");
        byte[] before = ReadPatchBytes(OriginalTarget(target), offset);
        record.Patches.Add(new ProductIdPatch { Target = target, Offset = offset, Before = before, After = desired.ToArray() });
        Persist();
    }

    private void ValidatePatchTarget(string target, long offset, byte[] desired)
    {
        ValidateRelative(target);
        string[] parts = target.Split('/');
        var item = record.Items.SingleOrDefault(x => Names.Equals(x.FinalName, parts[0]));
        if (parts.Length < 2 || item == null || item.Kind != CardSaveOutputKind.Game || offset < 32 || offset > long.MaxValue - 10 ||
            desired == null || desired.Length != 10 || desired.Any(x => x > 127) ||
            !Constants.DiscImageExtensions.Contains(Path.GetExtension(target).ToLowerInvariant()))
            throw new InvalidDataException("Invalid product ID patch.");
    }

    private byte[] ReadPatchBytes(string target, long offset)
    {
        CheckPath(target);
        using var stream = File.OpenRead(At(target));
        return ReadPatchBytes(stream, offset);
    }

    private static byte[] ReadPatchBytes(FileStream stream, long offset)
    {
        if (stream.Length < offset + Constants.IpLengthProductId) throw new InvalidDataException("Product ID patch is outside the image.");
        stream.Position = offset - Constants.IpOffsetProductId;
        byte[] magic = new byte[Constants.SaturnMagic.Length];
        stream.ReadExactly(magic);
        if (!magic.SequenceEqual(Constants.SaturnMagic)) throw new InvalidDataException("Product ID patch has no matching Saturn header.");
        stream.Position = offset;
        byte[] bytes = new byte[10];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private void ValidatePatches(bool requireAfter = false)
    {
        foreach (var patch in record.Patches)
        {
            var current = ReadPatchBytes(patch.Target, patch.Offset);
            if (!current.SequenceEqual(patch.After) && (requireAfter || record.Phase == Phase.Cleaning || !current.SequenceEqual(patch.Before)))
                throw new InvalidDataException("Image product ID changed: " + patch.Target);
        }
    }

    private void ApplyPatch(ProductIdPatch patch)
    {
        CheckPath(patch.Target);
        using var stream = new FileStream(At(patch.Target), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        byte[] current = ReadPatchBytes(stream, patch.Offset);
        if (current.SequenceEqual(patch.After)) return;
        if (!current.SequenceEqual(patch.Before)) throw new InvalidDataException("Image product ID changed: " + patch.Target);
        stream.Position = patch.Offset;
        stream.Write(patch.After);
        stream.Flush(flushToDisk: true);
    }

    internal async Task PublishAsync()
    {
        Require(Phase.Preparing);
        ValidateWork();
        FinishRetiring();
        if (record.WorkDirectories.Any(x => x.Value == null) || record.Discarding.Count != 0)
            throw new InvalidOperationException("Work preparation or cleanup is incomplete.");
        if (record.Items.Any(x => x.OriginalName == null && !x.Ready)) throw new InvalidOperationException("Some imports are not READY.");
        if (record.Artifacts.Any(x => !record.Metadata.Any(m => Names.Equals(m.Prepared, x))))
            throw new InvalidOperationException("Some metadata is not prepared.");
        foreach (var item in record.Items)
        {
            CheckExistingDirectory(item.OriginalName ?? Stage(item));
            if (item.Files != null) MatchFiles(Inventory(item.OriginalName ?? Stage(item)), item.Files, item.Id);
            if (item.OriginalName != null && Path.Exists(At(Stage(item))))
                throw new InvalidDataException("Original and staging paths are both occupied: " + item.Id);
        }
        ValidateNumericFolders(record.Items.Where(x => x.OriginalName != null).Select(x => x.OriginalName!));
        if (!record.MovesRequired)
        {
            if (!FinalOrderMatches()) throw new IOException("Card order changed before publication.");
            SetPhase(Phase.Published);
            return;
        }
        record.OrderPass = 1;
        SetPhase(Phase.Staging);
        foreach (var item in record.Items.Where(x => x.OriginalName != null)) await MoveAsync(item.OriginalName!, Stage(item), item.Files!, validateContents: false);
        SetPhase(Phase.Publishing);
        foreach (var item in record.Items) await MoveAsync(Stage(item), item.FinalName, item.Files!, validateContents: false);
        await VerifyFinalOrderAsync();
        SetPhase(Phase.Published);
    }

    internal async Task RecoverAsync()
    {
        ValidateWork();
        if (HasCommitIntent) { await FinishCommitAsync(); return; }
        if (record.Phase == Phase.Preparing)
        {
            FinishDiscards();
            FinishRetiring();
            ValidateNumericFolders(record.OriginalOrder);
            foreach (var item in record.Items)
            {
                string? path = item.OriginalName ?? (item.Ready ? Stage(item) : null);
                if (path == null) continue;
                CheckExistingDirectory(path);
                if (item.Files != null) MatchFiles(Inventory(path), item.Files, item.Id);
                if (item.OriginalName != null && Path.Exists(At(Stage(item))))
                    throw new InvalidDataException("Original and staging paths are both occupied: " + item.Id);
            }
            return;
        }
        if (!record.MovesRequired)
        {
            if (!FinalOrderMatches()) throw new IOException("Card order changed during save.");
            SetPhase(Phase.Preparing);
            return;
        }
        if (record.Phase == Phase.Staging)
        {
            foreach (var item in record.Items.Where(x => x.OriginalName != null)) await MoveAsync(item.OriginalName!, Stage(item), item.Files!);
            record.RestorePass = 1;
            SetPhase(Phase.Restoring);
        }
        if (record.Phase is Phase.Publishing or Phase.Published or Phase.Restaging or Phase.OrderStaging or Phase.OrderPublishing)
        {
            if (record.Phase != Phase.Restaging) SetPhase(Phase.Restaging);
            foreach (var item in record.Items) await MoveAsync(item.FinalName, Stage(item), item.Files!);
            record.RestorePass = 1;
            SetPhase(Phase.Restoring);
        }
        while (record.Phase is Phase.Restoring or Phase.RestoreStaging)
        {
            if (record.Phase == Phase.RestoreStaging)
            {
                foreach (string original in record.OriginalOrder)
                {
                    var item = record.Items.Single(x => Names.Equals(x.OriginalName, original));
                    await MoveAsync(original, Stage(item), item.Files!);
                }
                SetPhase(Phase.Restoring);
            }
            foreach (string original in record.OriginalOrder)
            {
                var item = record.Items.Single(x => Names.Equals(x.OriginalName, original));
                await MoveAsync(Stage(item), original, item.Files!);
            }
            ValidateNumericFolders(record.OriginalOrder);
            var actual = CardOrder.NumberedFolders(root);
            if (actual.SequenceEqual(record.OriginalOrder, Names))
            {
                SetPhase(Phase.Preparing);
                return;
            }
            if (record.RestorePass >= 3) throw new IOException("Original folder order could not be restored.");
            record.RestorePass++;
            SetPhase(Phase.RestoreStaging);
        }
    }

    internal void RecordCommitIntent() => PrepareCommit();

    private IReadOnlyList<Metadata> PrepareCommit()
    {
        Require(Phase.Published);
        ValidateWork();
        if (!FinalOrderMatches()) throw new IOException("Folder order changed before commit.");
        foreach (var item in record.Items)
        {
            CheckExistingDirectory(item.FinalName);
            if (item.Files != null) MatchFiles(Inventory(item.FinalName), item.Files, item.Id);
        }
        ValidatePatches();
        var actions = ValidateMetadata();
        SetPhase(Phase.Committing);
        return actions;
    }

    internal Task CommitAsync()
    {
        var actions = HasCommitIntent ? null : PrepareCommit();
        return FinishCommitAsync(actions);
    }

    private Task FinishCommitAsync(IReadOnlyList<Metadata>? validatedActions = null)
    {
        IReadOnlyList<Metadata> actions;
        if (validatedActions == null)
        {
            ValidateWork();
            if (!FinalOrderMatches()) throw new IOException("Published folder order changed. Preserve the journal for review.");
            actions = ValidateMetadata();
            ValidatePublishedFiles();
            ValidatePatches();
        }
        else actions = validatedActions;
        if (record.Phase == Phase.Committing)
        {
            foreach (var patch in record.Patches) ApplyPatch(patch);
            foreach (var action in actions)
            {
                if (action.Prepared == null) File.Delete(At(action.Target));
                else
                {
                    CheckPath(action.Target);
                    Directory.CreateDirectory(Path.GetDirectoryName(At(action.Target))!);
                    File.Move(At(action.Prepared), At(action.Target), overwrite: true);
                }
            }
            // Only recorded metadata changes between game validation and this check.
            if (ValidateMetadata().Count != 0) throw new InvalidDataException("Metadata commit is incomplete.");
            ValidatePatches(requireAfter: true);
            SetPhase(Phase.Cleaning);
        }
        ValidateWork();
        foreach (var work in record.WorkDirectories) DeleteRecordedTree(work.Key, work.Value!);
        if (File.Exists(At(WorkName + "/Journal.next"))) File.Delete(At(WorkName + "/Journal.next"));
        if (Directory.Exists(At(WorkName))) Directory.Delete(At(WorkName), recursive: false);
        File.Delete(At(JournalName));
        return Task.CompletedTask;
    }

    private void ValidatePublishedFiles()
    {
        foreach (var item in record.Items)
        {
            CheckExistingDirectory(item.FinalName);
            if (item.Files == null) continue;
            var expected = new Dictionary<string, long>(item.Files, Names);
            foreach (var action in record.Metadata.Where(x => x.Target.StartsWith(item.FinalName + "/", StringComparison.OrdinalIgnoreCase)))
            {
                string name = action.Target[(item.FinalName.Length + 1)..];
                if (File.Exists(At(action.Target))) expected[name] = new FileInfo(At(action.Target)).Length;
                else expected.Remove(name);
                if (action.Prepared != null)
                {
                    for (string? parent = Path.GetDirectoryName(name); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
                    {
                        string relative = parent.Replace('\\', '/');
                        if (Directory.Exists(At(item.FinalName + "/" + relative))) expected[relative + "/"] = -1;
                    }
                }
            }
            MatchFiles(Inventory(item.FinalName), expected, item.FinalName);
        }
    }

    private IReadOnlyList<Metadata> ValidateMetadata()
    {
        var pending = new List<Metadata>();
        foreach (var action in record.Metadata)
        {
            string? current = Hash(action.Target);
            bool preparedExists = action.Prepared != null && File.Exists(At(action.Prepared));
            if (record.Phase == Phase.Cleaning && (!Names.Equals(current, action.AfterHash) || preparedExists))
                throw new InvalidDataException("Committed metadata changed: " + At(action.Target));
            if (preparedExists)
            {
                if (!Names.Equals(Hash(action.Prepared!), action.AfterHash) || !Names.Equals(current, action.BeforeHash))
                    throw new InvalidDataException("Metadata changed: " + At(action.Target));
                pending.Add(action);
            }
            else if (!Names.Equals(current, action.AfterHash))
            {
                if (action.Prepared != null || !Names.Equals(current, action.BeforeHash))
                    throw new InvalidDataException("Metadata cannot be recovered: " + At(action.Target));
                pending.Add(action);
            }
        }
        return pending;
    }

    private async Task VerifyFinalOrderAsync()
    {
        while (!FinalOrderMatches())
        {
            if (record.OrderPass >= 3) throw new IOException("The card did not retain the requested directory order.");
            record.OrderPass++;
            SetPhase(Phase.OrderStaging);
            foreach (var item in record.Items) await MoveAsync(item.FinalName, Stage(item), item.Files!);
            SetPhase(Phase.OrderPublishing);
            foreach (var item in record.Items) await MoveAsync(Stage(item), item.FinalName, item.Files!);
        }
    }

    private bool FinalOrderMatches()
    {
        ValidateNumericFolders(record.Items.Select(x => x.FinalName));
        return CardOrder.IsCardOrdered(root);
    }

    private void ValidateNumericFolders(IEnumerable<string> expected)
    {
        var actual = CardOrder.NumberedFolders(root);
        if (!new HashSet<string>(actual, Names).SetEquals(expected))
            throw new InvalidDataException("Numbered folders no longer match the recorded mapping.");
    }

    private void CheckExistingDirectory(string relative)
    {
        var status = CheckPath(relative);
        if (status == null || (status & FileAttributes.Directory) == 0) throw new InvalidDataException("Recorded directory is missing: " + relative);
    }

    private async Task MoveAsync(string source, string destination, Dictionary<string, long> files, bool validateContents = true)
    {
        var sourceStatus = CheckPath(source);
        var destinationStatus = CheckPath(destination);
        bool from = sourceStatus != null && (sourceStatus & FileAttributes.Directory) != 0;
        bool to = destinationStatus != null && (destinationStatus & FileAttributes.Directory) != 0;
        if (from == to || (sourceStatus != null && !from) || (destinationStatus != null && !to))
            throw new InvalidDataException("Ambiguous or missing folder mapping: " + At(source) + " -> " + At(destination));
        if (validateContents) MatchFiles(Inventory(from ? source : destination), files, from ? source : destination);
        if (from) await moveDirectory(At(source), At(destination));
    }

    private static Record ReadRecord(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            void Check(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in element.EnumerateObject())
                    {
                        if (!names.Add(property.Name)) throw new JsonException("Duplicate journal member.");
                        Check(property.Value);
                    }
                }
                else if (element.ValueKind == JsonValueKind.Array)
                    foreach (var item in element.EnumerateArray()) Check(item);
            }
            Check(document.RootElement);
            return document.RootElement.Deserialize<Record>(Json) ?? throw new JsonException("Missing record.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Corrupt save journal: " + path, ex); }
    }

    private void ValidateRecord()
    {
        if (record.Version != 1 || !Guid.TryParseExact(record.Id, "N", out _) || record.Revision < 0 ||
            record.Owner != "OrbitalOrganizer" || record.MenuKind is not ("Rmenu" or "RmenuKai" or "Both") ||
            record.OrderPass is < 0 or > 3 || record.RestorePass is < 0 or > 3 || record.Patches == null || !Enum.IsDefined(record.Phase) ||
            record.Items == null || record.OriginalOrder == null || record.Metadata == null || record.Artifacts == null || record.WorkDirectories == null || record.Discarding == null || record.Retiring == null || record.RetiringHashes == null)
            throw new InvalidDataException("Unsupported or incomplete save record.");
        if (record.Items.Any(x => x == null) || record.Metadata.Any(x => x == null) || record.Patches.Any(x => x == null) ||
            record.Retiring.Any(x => x.Value == null) || record.Discarding.Any(x => x.Value == null))
            throw new InvalidDataException("Missing save record entry.");
        if (record.Phase != Phase.Preparing && (record.Items.Any(x => x.OriginalName == null && !x.Ready) ||
            record.WorkDirectories.Any(x => x.Value == null) || record.Artifacts.Any(x => !record.Metadata.Any(m => Names.Equals(m.Prepared, x)))))
            throw new InvalidDataException("Publication includes unfinished preparation.");
        foreach (var retired in record.Retiring)
        {
            ValidateRelative(retired.Key);
            bool work = new[] { "File-", "Work-", "Item-" }.Any(prefix => retired.Key.StartsWith(WorkName + "/" + prefix, StringComparison.Ordinal)) &&
                retired.Key.Count(c => c == '/') == 1;
            if (record.Phase != Phase.Preparing || (!work && (retired.Key.Contains('/') || Reserved(retired.Key))) ||
                record.Items.Any(x => Names.Equals(x.OriginalName, retired.Key) || Names.Equals(Stage(x), retired.Key)))
                throw new InvalidDataException("Invalid retired save data: " + retired.Key);
            if (!work) ValidateNumberedName(retired.Key);
            if (retired.Key.StartsWith(WorkName + "/File-", StringComparison.Ordinal))
            {
                if (retired.Value.Count > 1 || (retired.Value.Count == 1 && (!retired.Value.TryGetValue("", out long n) || n < 0)))
                    throw new InvalidDataException("Invalid retired metadata.");
            }
            else ValidateFiles(retired.Value);
        }
        foreach (var hash in record.RetiringHashes)
            if (!record.Retiring.ContainsKey(hash.Key) || !hash.Key.StartsWith(WorkName + "/File-", StringComparison.Ordinal) || !ValidHash(hash.Value))
                throw new InvalidDataException("Invalid retired metadata hash.");
        var ids = new HashSet<string>(Names);
        var originals = new HashSet<string>(Names);
        var finals = new HashSet<string>(Names);
        var stages = new HashSet<string>(Names);
        foreach (var item in record.Items)
        {
            if (item == null) throw new InvalidDataException("Missing item record.");
            ValidateName(item.Id);
            ValidateName(item.FinalName);
            if (!int.TryParse(item.FinalName, out int number) || number < 1 || number.ToString("D2") != item.FinalName)
                throw new InvalidDataException("Invalid final folder: " + item.FinalName);
            if (item.StageName == null || !item.StageName.StartsWith("Item-", StringComparison.Ordinal) || !Guid.TryParseExact(item.StageName[5..], "N", out _))
                throw new InvalidDataException("Invalid staging name.");
            if (!ids.Add(item.Id) || !finals.Add(item.FinalName) || !stages.Add(item.StageName)) throw new InvalidDataException("Duplicate item mapping.");
            if (item.OriginalName != null)
            {
                ValidateNumberedName(item.OriginalName);
                if (Reserved(item.OriginalName) || !originals.Add(item.OriginalName)) throw new InvalidDataException("Invalid original folder: " + item.OriginalName);
            }
            if (!Enum.IsDefined(item.Kind) || (item.FinalName == "01" && item.Kind != CardSaveOutputKind.Menu))
                throw new InvalidDataException("Invalid output kind: " + item.Id);
            if (item.Files != null) ValidateFiles(item.Files);
            if (item.Ready && (item.OriginalName != null || item.Files == null)) throw new InvalidDataException("Invalid READY record.");
        }
        if (!record.Items.Any(x => x.FinalName == "01") ||
            !record.Items.Select(x => int.Parse(x.FinalName)).SequenceEqual(record.Items.Select(x => int.Parse(x.FinalName)).OrderBy(x => x)) ||
            (!record.MovesRequired && record.Items.Any(x => !Names.Equals(x.OriginalName, x.FinalName) || x.Files != null)))
            throw new InvalidDataException("Invalid ordered mapping.");
        if (record.OriginalOrder.Distinct(Names).Count() != record.OriginalOrder.Count || record.OriginalOrder.Any(x => !originals.Contains(x)))
            throw new InvalidDataException("Invalid original folder order.");
        foreach (var work in record.WorkDirectories)
        {
            ValidateRelative(work.Key);
            if (!work.Key.StartsWith(WorkName + "/Work-", StringComparison.Ordinal) || work.Key.Count(x => x == '/') != 1)
                throw new InvalidDataException("Invalid owned work directory.");
            if (work.Value != null) ValidateFiles(work.Value);
        }
        foreach (var discard in record.Discarding)
        {
            if (record.Phase != Phase.Preparing || !IsIncomplete(discard.Key))
                throw new InvalidDataException("Invalid incomplete import discard.");
            if (record.Artifacts.Contains(discard.Key, Names))
            {
                if (discard.Value.Count > 1 || (discard.Value.Count == 1 && (!discard.Value.TryGetValue("", out long size) || size < 0)))
                    throw new InvalidDataException("Invalid incomplete metadata inventory.");
            }
            else ValidateFiles(discard.Value);
        }
        var artifacts = new HashSet<string>(Names);
        foreach (string artifact in record.Artifacts)
        {
            ValidateRelative(artifact);
            if (!artifact.StartsWith(WorkName + "/File-", StringComparison.Ordinal) || artifact.Count(x => x == '/') != 1 || !artifacts.Add(artifact))
                throw new InvalidDataException("Invalid owned metadata path.");
        }
        var patchTargets = new HashSet<string>(Names);
        foreach (var patch in record.Patches)
        {
            if (patch == null) throw new InvalidDataException("Missing patch record.");
            ValidatePatchTarget(patch.Target, patch.Offset, patch.After);
            if (patch.Before == null || patch.Before.Length != 10 || !patchTargets.Add(patch.Target))
                throw new InvalidDataException("Invalid patch record.");
        }
        var targets = new HashSet<string>(Names);
        var prepared = new HashSet<string>(Names);
        foreach (var action in record.Metadata)
        {
            ValidateTarget(action.Target);
            if (!targets.Add(action.Target) || (action.Prepared != null && (!artifacts.Contains(action.Prepared) || !prepared.Add(action.Prepared) || !ValidHash(action.AfterHash))) ||
                (action.BeforeHash != null && !ValidHash(action.BeforeHash)) || (action.Prepared == null && action.AfterHash != null))
                throw new InvalidDataException("Invalid metadata record.");
        }
    }

    private void ValidateWork()
    {
        CheckPath(WorkName);
        if (!Directory.Exists(At(WorkName)))
        {
            if (Path.Exists(At(WorkName)) || record.Phase is not (Phase.Preparing or Phase.Cleaning)) throw new InvalidDataException("Save workspace is missing.");
            return;
        }
        foreach (string path in Directory.EnumerateFileSystemEntries(At(WorkName)))
        {
            string name = Path.GetFileName(path);
            CheckPath(WorkName + "/" + name);
            bool owned = record.Retiring.ContainsKey(WorkName + "/" + name) || record.Items.Any(x => Names.Equals(x.StageName, name)) || record.Artifacts.Contains(WorkName + "/" + name, Names) || record.WorkDirectories.ContainsKey(WorkName + "/" + name);
            if (name == "Journal.next")
            {
                Record next = ReadRecord(path);
                if (next.Owner != record.Owner || next.Version != record.Version || next.Id != record.Id || next.Revision != record.Revision + 1)
                    throw new InvalidDataException("Unowned journal write: " + path);
                new CardSaveTransaction(root, next, moveDirectory).ValidateRecord();
                continue;
            }
            if (!owned) throw new InvalidDataException("Unowned save data: " + path);
            if (record.WorkDirectories.TryGetValue(WorkName + "/" + name, out var workFiles) && workFiles != null)
            {
                var actual = Inventory(WorkName + "/" + name);
                if (record.Phase == Phase.Cleaning)
                {
                    if (actual.Any(x => !workFiles.TryGetValue(x.Key, out long size) || size != x.Value))
                        throw new InvalidDataException("Work directory changed: " + path);
                }
                else MatchFiles(actual, workFiles, path);
            }
        }
    }

    private void Persist()
    {
        if (deferPersist) return;
        ValidateWork();
        Directory.CreateDirectory(At(WorkName));
        string next = At(WorkName + "/Journal.next");
        if (File.Exists(next)) File.Delete(next);
        int revision = record.Revision;
        try
        {
            record.Revision++;
            using (var stream = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, record, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(next, At(JournalName), overwrite: true);
        }
        catch
        {
            record.Revision = revision;
            throw;
        }
    }

    private void WriteInitial()
    {
        using var stream = new FileStream(At(JournalName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, record, Json);
        stream.Flush(flushToDisk: true);
    }

    private void SetPhase(Phase phase)
    {
        Phase previous = record.Phase;
        record.Phase = phase;
        try { Persist(); }
        catch { record.Phase = previous; throw; }
    }
    private void Require(Phase phase) { if (record.Phase != phase) throw new InvalidOperationException("Save is in phase " + record.Phase + "."); }
    private CardSaveItem Item(string id) => record.Items.Single(x => Names.Equals(x.Id, id));
    private string Stage(CardSaveItem item) => WorkName + "/" + item.StageName;
    private string At(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private Dictionary<string, long> Inventory(string relative)
    {
        InventoryReadCount++;
        var status = CheckPath(relative);
        if (status == null || (status & FileAttributes.Directory) == 0) throw new InvalidDataException("Game folder is missing: " + At(relative));
        var files = new Dictionary<string, long>(Names);
        void Read(string directory, string prefix)
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked paths are not supported: " + entry.FullName);
                string name = prefix + entry.Name;
                if (entry is DirectoryInfo)
                {
                    files.Add(name + "/", -1);
                    Read(entry.FullName, name + "/");
                }
                else files.Add(name, ((FileInfo)entry).Length);
            }
        }
        Read(At(relative), "");
        return files;
    }

    private static void MatchFiles(Dictionary<string, long> actual, Dictionary<string, long> expected, string path)
    {
        if (actual.Count != expected.Count || expected.Any(x => !actual.TryGetValue(x.Key, out long size) || size != x.Value))
            throw new InvalidDataException("Folder contents changed or are incomplete: " + path);
    }

    private string? Hash(string relative)
    {
        var status = CheckPath(relative);
        if (status == null) return null;
        if ((status & FileAttributes.Directory) != 0) throw new InvalidDataException("Expected a file: " + At(relative));
        MetadataHashReadCount++;
        using var stream = File.OpenRead(At(relative));
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool ValidHash(string? hash) => hash?.Length == 64 && hash.All(Uri.IsHexDigit);
    private static void RequireCompanion(Dictionary<string, long> files, string file, string extension)
    {
        if (!files.TryGetValue(Path.ChangeExtension(file, extension), out long size) || size <= 0)
            throw new InvalidDataException("Missing image companion: " + Path.ChangeExtension(file, extension));
    }

    private FileAttributes? CheckPath(string relative)
    {
        string path = root;
        var status = Check(path);
        foreach (string part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (status != null && (status & FileAttributes.Directory) == 0)
                throw new IOException("Expected a directory: " + path);
            path = Path.Combine(path, part);
            status = Check(path);
        }
        return status;

        static FileAttributes? Check(string path)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked paths are not supported: " + path);
            return attributes;
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ') ||
            name.Any(x => x < 32 || "<>:\"/\\|?*".Contains(x))) throw new InvalidDataException("Invalid card path component: " + name);
    }

    private static void ValidateNumberedName(string name)
    {
        ValidateName(name);
        if (!int.TryParse(name, out int number) || number < 1 || name.Any(x => x < '0' || x > '9'))
            throw new InvalidDataException("Not a numbered game folder: " + name);
    }

    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new InvalidDataException("Missing card path.");
        foreach (string part in path.Split('/')) ValidateName(part);
    }

    private void ValidateTarget(string target)
    {
        ValidateRelative(target);
        string[] parts = target.Split('/');
        var item = record.Items.SingleOrDefault(x => Names.Equals(x.FinalName, parts[0]));
        bool allowed = (parts.Length == 1 && new[] { "GameList.txt", "GameDB.json", "Rhea.ini", "Phoebe.ini" }.Contains(target, Names)) ||
            (parts.Length == 2 && Constants.AllSidecarFiles.Contains(parts[1], Names) && item != null && item.FinalName != "01") ||
            (item?.Kind == CardSaveOutputKind.Menu && ((parts.Length == 2 && Names.Equals(parts[1], "RMENU.iso")) ||
                (parts.Length >= 4 && Names.Equals(parts[1], "BIN") && Names.Equals(parts[2], "RMENU"))));
        if (!allowed) throw new InvalidDataException("Not a managed metadata path: " + target);
    }

    private static void ValidateFiles(Dictionary<string, long> files)
    {
        var names = new HashSet<string>(Names);
        foreach (var file in files)
        {
            ValidateRelative(file.Key.TrimEnd('/'));
            if (!names.Add(file.Key) || (file.Key.EndsWith('/') ? file.Value != -1 : file.Value < 0)) throw new InvalidDataException("Invalid file inventory.");
        }
    }

    private static bool Reserved(string name) => Names.Equals(name, WorkName) || Names.Equals(name, JournalName) ||
        Names.Equals(name, Constants.TempFolderName) || name.Contains("_lockcheck_", StringComparison.OrdinalIgnoreCase);

    private void CheckLegacyPaths()
    {
        if (!Directory.Exists(root)) return;
        var paths = Directory.EnumerateFileSystemEntries(root).Where(x =>
        {
            string name = Path.GetFileName(x);
            return Names.Equals(name, Constants.TempFolderName) || name.Contains("_lockcheck_", StringComparison.OrdinalIgnoreCase) ||
                (Directory.Exists(x) && (name.StartsWith("OrbitalOrganizer_", StringComparison.OrdinalIgnoreCase) ||
                 (Guid.TryParse(name, out _) && Directory.EnumerateFiles(x).Any(p => Constants.AllImageExtensions.Contains(Path.GetExtension(p).ToLowerInvariant())))));
        }).ToArray();
        if (paths.Length > 0) throw new InvalidDataException("Unrecorded recovery paths require review: " + string.Join(", ", paths));
    }
}
