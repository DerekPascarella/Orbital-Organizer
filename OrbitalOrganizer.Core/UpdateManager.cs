using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OrbitalOrganizer.Core;

public enum ManualUpdateReason
{
    None,
    UnsupportedPlatform,
    KillSwitch
}

public class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public bool ManualUpdateRequired { get; set; }
    public ManualUpdateReason ManualReason { get; set; }
    public string LatestTag { get; set; } = "";
    public string LatestVersion { get; set; } = "";
}

public class DownloadProgress
{
    public long BytesRead { get; set; }
    public long TotalBytes { get; set; }
    public double SpeedBytesPerSecond { get; set; }
}

public static class UpdateManager
{
    private static readonly HttpClient _client;
    private const string DefaultRepo = "DerekPascarella/Orbital-Organizer";
    private const string StagingDirName = "OrbitalOrganizer_update";
    private const string AutoUpdateKillSwitch = "This release cannot be auto-updated.";
    private const string WindowsScriptName = "_oo_updater.bat";
    private const string UnixScriptName = "_oo_updater.sh";

    public static string? RepoOverride { get; set; }

    static UpdateManager()
    {
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("OrbitalOrganizer-UpdateCheck/1.0");
    }

    private static string GetRepoPath()
    {
        return string.IsNullOrWhiteSpace(RepoOverride) ? DefaultRepo : RepoOverride;
    }

    private static Version ParseVersion(string? versionString)
    {
        if (string.IsNullOrWhiteSpace(versionString))
            return new Version(0, 0);

        var cleaned = versionString.TrimStart('v', 'V');
        var hyphenIndex = cleaned.IndexOf('-');
        if (hyphenIndex > 0)
            cleaned = cleaned.Substring(0, hyphenIndex);

        return Version.TryParse(cleaned, out var v) ? v : new Version(0, 0);
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var result = new UpdateCheckResult();

        try
        {
            var repoPath = GetRepoPath();
            var url = $"https://api.github.com/repos/{repoPath}/releases/latest";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            using var response = await _client.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token);
            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";

            var body = "";
            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
                body = bodyElement.GetString() ?? "";

            result.LatestTag = tagName;
            result.LatestVersion = "v" + tagName.TrimStart('v', 'V');

            var currentVersion = ParseVersion(Constants.Version);
            var latestVersion = ParseVersion(tagName);

            var isNewer = latestVersion > currentVersion;
            var killSwitchActive = body.Contains(AutoUpdateKillSwitch, StringComparison.OrdinalIgnoreCase);
            var isMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

            result.UpdateAvailable = isNewer && !killSwitchActive && !isMacOS;
            result.ManualUpdateRequired = isNewer && (killSwitchActive || isMacOS);
            if (result.ManualUpdateRequired)
                result.ManualReason = isMacOS ? ManualUpdateReason.UnsupportedPlatform : ManualUpdateReason.KillSwitch;
        }
        catch
        {
            result.UpdateAvailable = false;
        }

        return result;
    }

    private static string GetAssetSuffix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X86
                ? "win-x86.zip"
                : "win-x64.zip";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "osx-arm64.tar.gz"
                : "osx-x64.tar.gz";
        }
        return "linux-x64.tar.gz";
    }

    private static string GetAssetUrl(string tag)
    {
        var repoPath = GetRepoPath();
        var suffix = GetAssetSuffix();
        var cleanTag = tag.TrimStart('v', 'V');
        return $"https://github.com/{repoPath}/releases/download/{tag}/OrbitalOrganizer.v{cleanTag}-{suffix}";
    }

    private static string GetStagingDir()
    {
        return Path.Combine(Path.GetTempPath(), StagingDirName);
    }

    public static async Task DownloadUpdateAsync(string tag, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var stagingDir = GetStagingDir();
        var downloadDir = Path.Combine(stagingDir, "download");

        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, true);

        Directory.CreateDirectory(downloadDir);

        var url = GetAssetUrl(tag);
        var suffix = GetAssetSuffix();
        var cleanTag = tag.TrimStart('v', 'V');
        var fileName = $"OrbitalOrganizer.v{cleanTag}-{suffix}";
        var downloadPath = Path.Combine(downloadDir, fileName);

        try
        {
            using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);

            var buffer = new byte[65536];
            long bytesRead = 0;
            int read;
            var sw = Stopwatch.StartNew();
            long lastReportBytes = 0;
            double lastReportTime = 0;

            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;

                var elapsed = sw.Elapsed.TotalSeconds;
                if (elapsed - lastReportTime >= 0.25 || bytesRead == totalBytes)
                {
                    var speed = (elapsed - lastReportTime) > 0
                        ? (bytesRead - lastReportBytes) / (elapsed - lastReportTime)
                        : 0;
                    lastReportBytes = bytesRead;
                    lastReportTime = elapsed;

                    progress?.Report(new DownloadProgress
                    {
                        BytesRead = bytesRead,
                        TotalBytes = totalBytes,
                        SpeedBytesPerSecond = speed
                    });
                }
            }
        }
        catch
        {
            CleanupStagingDirectory();
            throw;
        }
    }

    public static async Task ExtractUpdateAsync(string tag, CancellationToken cancellationToken)
    {
        var stagingDir = GetStagingDir();
        var downloadDir = Path.Combine(stagingDir, "download");
        var extractedDir = Path.Combine(stagingDir, "extracted");

        Directory.CreateDirectory(extractedDir);

        var suffix = GetAssetSuffix();
        var cleanTag = tag.TrimStart('v', 'V');
        var fileName = $"OrbitalOrganizer.v{cleanTag}-{suffix}";
        var archivePath = Path.Combine(downloadDir, fileName);

        try
        {
            if (suffix.EndsWith(".zip"))
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractedDir), cancellationToken);
            }
            else
            {
                await ExtractTarGzAsync(archivePath, extractedDir, cancellationToken);
            }
        }
        catch
        {
            CleanupStagingDirectory();
            throw;
        }
    }

    private static async Task ExtractTarGzAsync(string archivePath, string extractedDir, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{extractedDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0)
                    return;
            }
        }
        catch
        {
            // tar not available
        }

        throw new Exception("Could not extract tar.gz archive. Please ensure 'tar' is available on your system.");
    }

    public static async Task PrepareUpdateAsync()
    {
        var stagingDir = GetStagingDir();
        var extractedDir = Path.Combine(stagingDir, "extracted");

        var contentRoot = FindContentRoot(extractedDir);
        if (contentRoot == null)
            throw new Exception("Could not find application files in the extracted archive.");

        if (contentRoot != extractedDir)
        {
            var tempMove = Path.Combine(stagingDir, "content_temp");
            Directory.Move(contentRoot, tempMove);
            if (Directory.Exists(extractedDir))
                Directory.Delete(extractedDir, true);
            Directory.Move(tempMove, extractedDir);
        }

        await Task.Run(() => MergeSettings(extractedDir));
    }

    private static string? FindContentRoot(string extractedDir)
    {
        if (HasAppFiles(extractedDir))
            return extractedDir;

        foreach (var subDir in Directory.GetDirectories(extractedDir))
        {
            if (HasAppFiles(subDir))
                return subDir;

            var macosDir = Path.Combine(subDir, "Contents", "MacOS");
            if (Directory.Exists(macosDir) && HasAppFiles(macosDir))
                return macosDir;
        }

        foreach (var subDir in Directory.GetDirectories(extractedDir))
        {
            foreach (var subSubDir in Directory.GetDirectories(subDir))
            {
                if (HasAppFiles(subSubDir))
                    return subSubDir;
            }
        }

        return null;
    }

    private static bool HasAppFiles(string dir)
    {
        return File.Exists(Path.Combine(dir, "OrbitalOrganizer.exe")) ||
               File.Exists(Path.Combine(dir, "OrbitalOrganizer")) ||
               File.Exists(Path.Combine(dir, "OrbitalOrganizer.dll"));
    }

    private static void MergeSettings(string extractedDir)
    {
        var appDir = AppContext.BaseDirectory;
        var currentSettingsPath = Path.Combine(appDir, "settings.json");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            currentSettingsPath = Path.Combine(home, "Library", "Application Support", "OrbitalOrganizer", "settings.json");
        }

        var newSettingsPath = Path.Combine(extractedDir, "settings.json");

        if (!File.Exists(currentSettingsPath))
        {
            // No existing settings to preserve, delete the new one if present
            DeleteIfExists(newSettingsPath);
            return;
        }

        try
        {
            var currentJson = File.ReadAllText(currentSettingsPath);
            var currentDoc = JsonDocument.Parse(currentJson);

            // If a new settings.json ships with the update, merge new keys into current
            if (File.Exists(newSettingsPath))
            {
                var newJson = File.ReadAllText(newSettingsPath);
                var newDoc = JsonDocument.Parse(newJson);

                var merged = new System.Collections.Generic.Dictionary<string, JsonElement>();

                // Start with all new keys (provides defaults for any new settings)
                foreach (var prop in newDoc.RootElement.EnumerateObject())
                    merged[prop.Name] = prop.Value;

                // Overlay current user values (preserves all existing settings)
                foreach (var prop in currentDoc.RootElement.EnumerateObject())
                    merged[prop.Name] = prop.Value;

                var options = new JsonSerializerOptions { WriteIndented = true };
                var mergedJson = JsonSerializer.Serialize(merged, options);
                File.WriteAllText(newSettingsPath, mergedJson);
            }
            else
            {
                // No new settings file in the update, just copy current over
                File.Copy(currentSettingsPath, newSettingsPath);
            }
        }
        catch
        {
            // If merge fails, just delete the new settings so the updater
            // copies over the current one untouched
            DeleteIfExists(newSettingsPath);
        }
    }

    public static void LaunchUpdaterAndExit()
    {
        var stagingDir = GetStagingDir();
        var extractedDir = Path.Combine(stagingDir, "extracted");
        var appDir = AppContext.BaseDirectory;
        var pid = Process.GetCurrentProcess().Id;
        var processName = Process.GetCurrentProcess().ProcessName;

        string scriptPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            scriptPath = Path.Combine(Path.GetTempPath(), WindowsScriptName);
            var script = GenerateWindowsScript(pid, processName, extractedDir, appDir);
            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        else
        {
            scriptPath = Path.Combine(Path.GetTempPath(), UnixScriptName);
            var script = GenerateUnixScript(pid, extractedDir, appDir);
            File.WriteAllText(scriptPath, script);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.WaitForExit(2000);
            }
            catch { }

            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"\"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        Environment.Exit(0);
    }

    private static string GenerateWindowsScript(int pid, string processName, string extractedDir, string appDir)
    {
        var escaped_extracted = extractedDir.Replace("/", "\\");
        var escaped_app = appDir.TrimEnd('\\').Replace("/", "\\");

        return $@"@echo off
:waitloop
tasklist /FI ""PID eq {pid}"" 2>NUL | find /I ""{processName}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >nul
    goto waitloop
)

xcopy /E /Y ""{escaped_extracted}\*"" ""{escaped_app}\""

rmdir /S /Q ""{escaped_extracted}""
rmdir /S /Q ""{GetStagingDir().Replace("/", "\\")}""

start """" ""{Path.Combine(escaped_app, "OrbitalOrganizer.exe")}""

del ""%~f0""
";
    }

    private static string GenerateUnixScript(int pid, string extractedDir, string appDir)
    {
        var escaped_app = appDir.TrimEnd('/');

        return $@"#!/bin/bash

# Wait for the app to exit
while kill -0 {pid} 2>/dev/null; do
    sleep 1
done

# Copy staged files over current install
cp -rf ""{extractedDir}/""* ""{escaped_app}/""

# Clean up staging directory
rm -rf ""{GetStagingDir()}""

# Fix permissions
chmod +x ""{escaped_app}/OrbitalOrganizer""

# Relaunch the app
""{escaped_app}/OrbitalOrganizer"" &

# Delete this script
rm ""$0""
";
    }

    public static void CleanupStaleStagingData()
    {
        try
        {
            var stagingDir = GetStagingDir();
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
        }
        catch { }

        try
        {
            var batScript = Path.Combine(Path.GetTempPath(), WindowsScriptName);
            if (File.Exists(batScript))
                File.Delete(batScript);
        }
        catch { }

        try
        {
            var shScript = Path.Combine(Path.GetTempPath(), UnixScriptName);
            if (File.Exists(shScript))
                File.Delete(shScript);
        }
        catch { }
    }

    public static void CleanupStagingDirectory()
    {
        try
        {
            var stagingDir = GetStagingDir();
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
        }
        catch { }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
