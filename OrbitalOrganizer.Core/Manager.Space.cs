using System.Runtime.InteropServices;
using System.Text;
using OrbitalOrganizer.Core.Models;

namespace OrbitalOrganizer.Core;

public partial class Manager
{
    internal Func<string, (string Volume, long Available)> SpaceProbe { get; set; } = GetVolumeSpace;

    public Task<SpaceCheckResult> CalculateRequiredSpaceAsync(string? tempFolderRoot = null) => RunOperationAsync(() => CalculateRequiredSpaceCoreAsync(tempFolderRoot));

    private async Task<SpaceCheckResult> CalculateRequiredSpaceCoreAsync(string? tempFolderRoot = null)
    {
        var result = new SpaceCheckResult { MetadataBuffer = 1024 * 1024 };
        if (string.IsNullOrEmpty(SdCardPath) || !Directory.Exists(SdCardPath))
        {
            result.HasSufficientSpace = true;
            return result;
        }
        await RecoverCardCoreAsync();
        string root = Path.GetFullPath(SdCardPath);
        var rows = ItemList.ToList();
        var removed = _removedItems.Where(g => !ItemList.Contains(g)).ToList();
        var ready = OpenSave()?.Items.Where(x => x.Ready).Select(x => x.Id).ToHashSet() ?? new();
        string tempRoot = Path.GetFullPath(!string.IsNullOrEmpty(tempFolderRoot) && Directory.Exists(tempFolderRoot)
            ? tempFolderRoot : Path.GetTempPath());
        ValidateSavePaths(root, tempRoot, rows, removed);
        bool onCard = string.Equals(tempRoot, root, StringComparison.OrdinalIgnoreCase) ||
            tempRoot.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        await Task.Run(() =>
        {
            var cardVolume = SpaceProbe(root);
            var temporaryVolume = SpaceProbe(tempRoot);
            result.TemporaryFolderPath = tempRoot;
            result.TemporarySharesCardVolume = string.Equals(cardVolume.Volume, temporaryVolume.Volume, StringComparison.OrdinalIgnoreCase);
            result.AvailableSpace = result.TemporarySharesCardVolume
                ? Math.Min(cardVolume.Available, temporaryVolume.Available) : cardVolume.Available;
            result.TemporaryAvailableSpace = temporaryVolume.Available;
            result.MenuFolderExists = Directory.Exists(Path.Combine(root, "01"));
            long sharedAssets = GetDirectorySize(Path.Combine(ToolsPath, "shared"));
            string primaryAssets = MenuKindSelected == MenuKind.Rmenu ? "rmenu_legacy" : "rmenukai";
            result.MenuSpaceNeeded = (sharedAssets + GetDirectorySize(Path.Combine(ToolsPath, primaryAssets))) * 2 + 5L * 1024 * 1024;
            if (MenuKindSelected == MenuKind.Both)
                result.MenuSpaceNeeded += (sharedAssets + GetDirectorySize(Path.Combine(ToolsPath, "rmenu_legacy"))) * 2 + 5L * 1024 * 1024;
            foreach (var game in removed.Where(g => g.WorkMode != WorkMode.New && Directory.Exists(g.FullFolderPath)))
                result.SpaceToBeFreed += GetDirectorySize(game.FullFolderPath);
            long preparationPeak = 0;
            long retainedWork = 0;
            long externalWorkPeak = 0;
            foreach (var game in rows)
            {
                if (game.WorkMode != WorkMode.New || game.IsLegacyRmenu || ready.Contains(game.SaveId)) continue;
                result.NewItemCount++;
                long size = Math.Max(game.Length, 0);
                var format = game.FileFormat == FileFormat.Compressed ? game.InnerFileFormat ?? FileFormat.Uncompressed : game.FileFormat;
                long intermediate = game.FileFormat == FileFormat.Compressed ? size : 0;
                if (game.FileFormat == FileFormat.Compressed) result.ContainsEstimatedSizes = true;
                if (format == FileFormat.CueBin)
                {
                    size = (long)(size * 1.25);
                    result.ContainsEstimatedSizes = true;
                }
                else if (format == FileFormat.Chd)
                {
                    size *= 2;
                    intermediate += size;
                    result.ContainsEstimatedSizes = true;
                }
                result.NewItemsSize += size;
                if (onCard) retainedWork += intermediate;
                else externalWorkPeak = Math.Max(externalWorkPeak, intermediate);
                preparationPeak = Math.Max(preparationPeak, result.NewItemsSize + retainedWork +
                    (!onCard && result.TemporarySharesCardVolume ? intermediate : 0));
            }
            long cardOutput = result.NewItemsSize + result.MenuSpaceNeeded + result.MetadataBuffer;
            result.TotalNeeded = Math.Max(cardOutput + retainedWork, preparationPeak + result.MetadataBuffer);
            result.TemporarySpaceNeeded = result.TotalNeeded - cardOutput;
            result.ExternalTemporarySpaceNeeded = result.TemporarySharesCardVolume ? 0 : externalWorkPeak;
            result.TemporaryShortfall = Math.Max(0, result.ExternalTemporarySpaceNeeded - result.TemporaryAvailableSpace);
            result.EffectiveAvailable = result.AvailableSpace + result.SpaceToBeFreed;
            result.Shortfall = result.TotalNeeded - result.EffectiveAvailable;
            result.HasSufficientSpace = result.Shortfall <= 0 && result.TemporaryShortfall == 0;
        });
        return result;
    }

    /// <summary>
    /// Builds the warning text shown when the space check comes up short.
    /// </summary>
    public static string BuildSpaceWarningMessage(SpaceCheckResult spaceCheck)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Insufficient free space for this save.\n");
        sb.AppendLine("Space needed:");
        sb.AppendLine($"  • New disc images ({spaceCheck.NewItemCount}): {FormatBytes(spaceCheck.NewItemsSize)}");
        sb.AppendLine($"  • Menu files: ~{FormatBytes(spaceCheck.MenuSpaceNeeded)}");
        sb.AppendLine($"  • Metadata files: ~{FormatBytes(spaceCheck.MetadataBuffer)}");
        if (spaceCheck.TemporarySpaceNeeded > 0)
            sb.AppendLine($"  Temporary conversion files: ~{FormatBytes(spaceCheck.TemporarySpaceNeeded)}");
        sb.AppendLine($"  Total: ~{FormatBytes(spaceCheck.TotalNeeded)}\n");
        sb.AppendLine($"Space available: {FormatBytes(spaceCheck.AvailableSpace)}");
        if (spaceCheck.SpaceToBeFreed > 0)
        {
            sb.AppendLine($"Space to be freed: {FormatBytes(spaceCheck.SpaceToBeFreed)}");
            sb.AppendLine($"Effective available: {FormatBytes(spaceCheck.EffectiveAvailable)}");
        }
        if (spaceCheck.Shortfall > 0)
            sb.AppendLine($"\nSD card shortfall: ~{FormatBytes(spaceCheck.Shortfall)}");
        if (spaceCheck.ExternalTemporarySpaceNeeded > 0)
        {
            sb.AppendLine($"\nTemporary folder: {spaceCheck.TemporaryFolderPath}");
            sb.AppendLine($"Temporary space needed: ~{FormatBytes(spaceCheck.ExternalTemporarySpaceNeeded)}");
            sb.AppendLine($"Temporary space available: {FormatBytes(spaceCheck.TemporaryAvailableSpace)}");
            if (spaceCheck.TemporaryShortfall > 0)
                sb.AppendLine($"Temporary space shortfall: ~{FormatBytes(spaceCheck.TemporaryShortfall)}");
        }
        if (spaceCheck.ContainsEstimatedSizes)
            sb.AppendLine("\nNote: Some items are compressed or need conversion and their final sizes are estimates.");
        sb.Append("\nDo you want to proceed anyway?");
        return sb.ToString();
    }

    private static (string Volume, long Available) GetVolumeSpace(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            string resolved = Path.GetPathRoot(fullPath)!;
            foreach (string part in Path.GetRelativePath(resolved, fullPath).Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".") continue;
                var directory = new DirectoryInfo(Path.Combine(resolved, part));
                resolved = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
            }
            if (OperatingSystem.IsWindows())
            {
                var mount = new StringBuilder(1024);
                var volume = new StringBuilder(1024);
                if (!GetVolumePathName(resolved, mount, mount.Capacity) ||
                    !GetDiskFreeSpaceEx(resolved, out ulong available, out _, out _))
                    return (fullPath, 0);
                string identity = GetVolumeNameForVolumeMountPoint(mount.ToString(), volume, volume.Capacity)
                    ? volume.ToString() : mount.ToString();
                return (identity, (long)Math.Min(available, (ulong)long.MaxValue));
            }
            string directoryPath = resolved.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var drive = DriveInfo.GetDrives().Where(d => d.IsReady)
                .Where(d => directoryPath.StartsWith(d.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .OrderByDescending(d => d.RootDirectory.FullName.Length).FirstOrDefault();
            return drive == null ? (fullPath, 0) : (drive.RootDirectory.FullName, drive.AvailableFreeSpace);
        }
        catch (IOException) { return (fullPath, 0); }
        catch (UnauthorizedAccessException) { return (fullPath, 0); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumePathName(string fileName, StringBuilder volumePathName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string volumeMountPoint, StringBuilder volumeName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceEx(string directoryName, out ulong availableBytes, out ulong totalBytes, out ulong totalFreeBytes);

}
