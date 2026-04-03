namespace OrbitalOrganizer.Core.Services;

/// <summary>
/// Converts CHD disc images to CUE/BIN format using native libchdr.
/// </summary>
public static class ChdConverter
{
    private const int SectorSize = 2352;
    private const int SectorsPerBatch = 256; // ~588KB per batch
    private const int TrackPadding = 4;

    /// <summary>
    /// Convert a CD-ROM CHD to CUE/BIN format in the given output directory.
    /// Returns the path to the generated CUE file on success.
    /// </summary>
    public static async Task<(bool Success, string? Message, string? CuePath)> ConvertToCueBinAsync(
        string chdPath,
        string outputDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string? gameName = null)
    {
        try
        {
            using var chd = new ChdReader(chdPath);

            if (!Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            int trackCount = chd.Tracks.Count;
            var cueContent = new System.Text.StringBuilder();
            long chdSectorOffset = 0;

            // chdman stores CD-ROM audio big-endian; BIN files need little-endian.
            bool swapAudio = true;

            for (int t = 0; t < chd.Tracks.Count; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var track = chd.Tracks[t];

                string binFilename = $"Track {track.TrackNumber:D2}.bin";
                string binPath = Path.Combine(outputDirectory, binFilename);

                string cueTrackType = track.IsAudio ? "AUDIO" : "MODE1/2352";

                cueContent.AppendLine($"FILE \"{binFilename}\" BINARY");
                cueContent.AppendLine($"  TRACK {track.TrackNumber:D2} {cueTrackType}");

                if (track.Pregap > 0 && track.TrackNumber > 1)
                {
                    cueContent.AppendLine($"    PREGAP {FramesToMsf(track.Pregap)}");
                }

                cueContent.AppendLine($"    INDEX 01 00:00:00");

                // FRAMES includes pregap, so subtract it (and pad) to get actual data count.
                int dataFrames = track.Frames - track.Pad - track.Pregap;
                long dataStart = chdSectorOffset + track.Pregap;

                string displayName = gameName ?? Path.GetFileNameWithoutExtension(chdPath);
                progress?.Report($"Decompressing {displayName} from CHD: track {track.TrackNumber} of {trackCount}...");
                await Task.Run(() => ExtractTrackData(chd, dataStart, dataFrames, binPath,
                    swapAudio && track.IsAudio, cancellationToken), cancellationToken);

                // Advance past this track's full allocation including alignment padding.
                chdSectorOffset += track.Frames + ChdReader.GetExtraFrames(track.Frames);
            }

            // Write CUE sheet
            string baseName = Path.GetFileNameWithoutExtension(chdPath);
            string cuePath = Path.Combine(outputDirectory, baseName + ".cue");
            await File.WriteAllTextAsync(cuePath, cueContent.ToString(), cancellationToken);

            return (true, null, cuePath);
        }
        catch (OperationCanceledException)
        {
            return (false, "Conversion was cancelled", null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Extract track data from CHD to a BIN file in batches.
    /// </summary>
    private static void ExtractTrackData(
        ChdReader chd,
        long startSector,
        int frameCount,
        string outputPath,
        bool swapEndianness,
        CancellationToken cancellationToken)
    {
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.SequentialScan);

        int remaining = frameCount;
        long currentSector = startSector;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int batchSize = Math.Min(remaining, SectorsPerBatch);
            byte[] data = chd.ReadSectors(currentSector, batchSize);

            if (swapEndianness)
                SwapAudioEndianness(data);

            fs.Write(data, 0, data.Length);

            currentSector += batchSize;
            remaining -= batchSize;
        }
    }

    /// <summary>
    /// Byte-swap 16-bit audio samples from big-endian to little-endian.
    /// </summary>
    private static void SwapAudioEndianness(byte[] data)
    {
        for (int i = 0; i < data.Length - 1; i += 2)
        {
            (data[i], data[i + 1]) = (data[i + 1], data[i]);
        }
    }

    /// <summary>
    /// Convert frame count to CUE MSF format (MM:SS:FF).
    /// 75 frames per second, 60 seconds per minute.
    /// </summary>
    private static string FramesToMsf(int frames)
    {
        int minutes = frames / (75 * 60);
        int seconds = (frames / 75) % 60;
        int ff = frames % 75;
        return $"{minutes:D2}:{seconds:D2}:{ff:D2}";
    }
}
