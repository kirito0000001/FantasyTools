using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class UpdateService
{
    public const string ToolboxStableKey = "FantasyTools";
    public const string DisplayName = "幻杀工具箱";
    public const string EntryExeName = "幻杀工具箱.exe";
    public const string RuntimeIdentifier = "win-x64";
    public const string ManifestAssetName = "toolbox-update.json";
    public const string GitHubReleaseApiUrl = "https://api.github.com/repos/kirito0000001/FantasyTools/releases";
    public const string GitHubReleasePageUrl = "https://github.com/kirito0000001/FantasyTools/releases";
    public const string GiteeReleaseApiUrl = "https://gitee.com/api/v5/repos/xiaojie578/FantasyTools/releases";
    public const string GiteeReleasePageUrl = "https://gitee.com/xiaojie578/FantasyTools/releases";
    private const int RetainedUpdatePackageCount = 2;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly string _currentVersionText = ResolveCurrentVersionText();

    public string UpdatesDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ToolboxStableKey,
        "Updates");

    public Version CurrentVersion => ParseVersion(_currentVersionText);

    public async Task<UpdateCheckResult> CheckAsync(
        UpdateSource source,
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var effectiveChannel = GetEffectiveUpdateChannel(channel);
        var sourceDisplayName = GetUpdateSourceDisplayName(source);
        var release = await RunWithTimeoutAsync(
            token => GetReleaseAsync(source, effectiveChannel, token),
            sourceDisplayName,
            timeoutSeconds,
            cancellationToken);
        if (release is null)
        {
            return new UpdateCheckResult(false, CurrentVersion, null, _currentVersionText, null, null, null, $"{sourceDisplayName} 没有找到可用 Release。", GetReleasePageUrl(source), string.Empty);
        }

        var manifest = await RunWithTimeoutAsync(
            token => BuildManifestAsync(release, effectiveChannel, token),
            sourceDisplayName,
            timeoutSeconds,
            cancellationToken);
        var latestVersion = ParseVersion(manifest.Version);
        var latestVersionText = NormalizeVersionText(manifest.Version);
        var versionCompare = CompareSemanticVersions(latestVersionText, _currentVersionText);
        if (versionCompare <= 0)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersion,
                latestVersion,
                _currentVersionText,
                latestVersionText,
                manifest,
                null,
                BuildNoUpdateMessage(source, effectiveChannel, latestVersionText, _currentVersionText),
                manifest.ReleaseNotesUrl,
                manifest.ReleaseNotes);
        }

        if (manifest.RequiresManualMigration)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersion,
                latestVersion,
                _currentVersionText,
                latestVersionText,
                manifest,
                null,
                $"发现 {latestVersionText}，但该版本需要手动更新。",
                manifest.ReleaseNotesUrl,
                manifest.ReleaseNotes);
        }

        var asset = SelectRuntimeAsset(manifest);
        if (asset is null)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersion,
                latestVersion,
                _currentVersionText,
                latestVersionText,
                manifest,
                null,
                $"发现 {latestVersionText}，但没有 {RuntimeIdentifier} 更新包。",
                manifest.ReleaseNotesUrl,
                manifest.ReleaseNotes);
        }

        return new UpdateCheckResult(
            true,
            CurrentVersion,
            latestVersion,
            _currentVersionText,
            latestVersionText,
            manifest,
            asset,
            $"发现新版本：{_currentVersionText} -> {latestVersionText}",
            manifest.ReleaseNotesUrl,
            manifest.ReleaseNotes);
    }

    private static string BuildNoUpdateMessage(
        UpdateSource source,
        UpdateChannel channel,
        string latestVersionText,
        string currentVersionText)
    {
        var currentVersion = SemanticVersionParts.Parse(currentVersionText);
        if (channel == UpdateChannel.Stable && currentVersion.IsPrerelease)
        {
            return $"当前测试版 V{currentVersionText} 比正式版 V{latestVersionText} 更新；如果要用正式版，请自行去 {GetUpdateSourceDisplayName(source)} 下载。";
        }

        return $"当前已是最新版本：{currentVersionText}";
    }

    public async Task<UpdateConnectionTestResult> MeasureConnectionAsync(
        UpdateSource source,
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var effectiveChannel = GetEffectiveUpdateChannel(channel);
        var sourceDisplayName = GetUpdateSourceDisplayName(source);
        var stopwatch = Stopwatch.StartNew();
        var release = await RunWithTimeoutAsync(
            token => GetReleaseAsync(source, effectiveChannel, token),
            sourceDisplayName,
            timeoutSeconds,
            cancellationToken);
        stopwatch.Stop();

        return release is null
            ? new UpdateConnectionTestResult(true, stopwatch.Elapsed, $"{sourceDisplayName} 可以连接，但当前通道没有找到 Release。")
            : new UpdateConnectionTestResult(true, stopwatch.Elapsed, $"{sourceDisplayName} 连接正常，最新远端版本：{release.TagName}");
    }

    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(UpdatesDirectoryPath);
        var safeFileName = Path.GetFileName(asset.FileName);
        var packagePath = Path.Combine(UpdatesDirectoryPath, safeFileName);
        var tempPath = packagePath + ".download";
        CleanUpdateCache(packagePath, tempPath);

        if (await TryUseExistingPackageAsync(packagePath, manifest, asset, progress, cancellationToken))
        {
            CleanUpdateCache(packagePath, tempPath);
            return new UpdateDownloadResult(packagePath, manifest, asset);
        }

        var resumeBytes = GetResumableDownloadBytes(tempPath, asset.SizeBytes);
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
        {
            throw new InvalidOperationException("更新包下载地址为空。");
        }

        var downloadUrl = asset.DownloadUrl;
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
        if (resumeBytes > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeBytes, null);
            progress?.Report(new ProgressUpdate("正在继续下载更新包...", 8, $"{FormatBytes(resumeBytes)} / {FormatBytes(asset.SizeBytes)}"));
        }
        else
        {
            progress?.Report(new ProgressUpdate("正在下载更新包...", 8, asset.FileName));
        }

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (resumeBytes > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            File.Delete(tempPath);
            resumeBytes = 0;
        }

        var responseBytes = response.Content.Headers.ContentLength ?? Math.Max(asset.SizeBytes - resumeBytes, 0);
        var totalBytes = asset.SizeBytes > 0
            ? asset.SizeBytes
            : resumeBytes + responseBytes;
        await using (var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = new FileStream(
            tempPath,
            resumeBytes > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read))
        {
            var buffer = new byte[1024 * 128];
            long downloadedBytes = resumeBytes;
            while (true)
            {
                var read = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                if (totalBytes > 0)
                {
                    var percent = 8 + downloadedBytes / (double)totalBytes * 62;
                    progress?.Report(new ProgressUpdate(
                        "正在下载更新包...",
                        percent,
                        $"{FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)}"));
                }
            }
        }

        progress?.Report(new ProgressUpdate("正在校验更新包大小...", 72, asset.FileName));
        var fileInfo = new FileInfo(tempPath);
        if (asset.SizeBytes > 0 && fileInfo.Length != asset.SizeBytes)
        {
            File.Delete(tempPath);
            throw new InvalidOperationException($"更新包大小校验失败：{fileInfo.Length} / {asset.SizeBytes}");
        }

        progress?.Report(new ProgressUpdate("正在校验 SHA-256...", 82, asset.Sha256));
        var actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
        if (!string.Equals(actualSha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException($"更新包 SHA-256 不一致：{actualSha256}");
        }

        progress?.Report(new ProgressUpdate("正在校验更新包结构...", 92, asset.FileName));
        ValidatePackage(tempPath, manifest, asset);
        if (File.Exists(packagePath))
        {
            File.Delete(packagePath);
        }

        File.Move(tempPath, packagePath);
        progress?.Report(new ProgressUpdate("更新包已准备就绪。", 100, packagePath));
        CleanUpdateCache(packagePath, tempPath);
        return new UpdateDownloadResult(packagePath, manifest, asset);
    }

    private async Task<bool> TryUseExistingPackageAsync(
        string packagePath,
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(packagePath))
        {
            return false;
        }

        try
        {
            progress?.Report(new ProgressUpdate("正在校验已下载更新包...", 18, Path.GetFileName(packagePath)));
            var fileInfo = new FileInfo(packagePath);
            if (asset.SizeBytes > 0 && fileInfo.Length != asset.SizeBytes)
            {
                File.Delete(packagePath);
                return false;
            }

            var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken);
            if (!string.Equals(actualSha256, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(packagePath);
                return false;
            }

            ValidatePackage(packagePath, manifest, asset);
            progress?.Report(new ProgressUpdate("已复用缓存更新包。", 100, packagePath));
            return true;
        }
        catch
        {
            TryDeleteFile(packagePath);
            return false;
        }
    }

    private static long GetResumableDownloadBytes(string tempPath, long expectedSizeBytes)
    {
        if (!File.Exists(tempPath))
        {
            return 0;
        }

        var length = new FileInfo(tempPath).Length;
        if (length <= 0)
        {
            TryDeleteFile(tempPath);
            return 0;
        }

        if (expectedSizeBytes > 0 && length >= expectedSizeBytes)
        {
            TryDeleteFile(tempPath);
            return 0;
        }

        return length;
    }

    private void CleanUpdateCache(string keepPackagePath, string keepTempPath)
    {
        if (!Directory.Exists(UpdatesDirectoryPath))
        {
            return;
        }

        var keepPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(keepPackagePath),
            Path.GetFullPath(keepTempPath)
        };

        foreach (var downloadFile in Directory.EnumerateFiles(UpdatesDirectoryPath, "*.download"))
        {
            if (!keepPaths.Contains(Path.GetFullPath(downloadFile)))
            {
                TryDeleteFile(downloadFile);
            }
        }

        var packages = Directory
            .EnumerateFiles(UpdatesDirectoryPath, "FantasyTools-v*-win-*.zip")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();
        var retained = 0;
        foreach (var package in packages)
        {
            var fullPath = Path.GetFullPath(package.FullName);
            if (keepPaths.Contains(fullPath))
            {
                retained++;
                continue;
            }

            if (retained < RetainedUpdatePackageCount)
            {
                retained++;
                continue;
            }

            TryDeleteFile(package.FullName);
        }
    }

    public async Task LaunchUpdaterAsync(
        string packagePath,
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var updaterPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "热更新覆盖.ps1");
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("热更新脚本不存在。", updaterPath);
        }

        var readySignalPath = CreateReadySignalPath();
        var runnerPath = CreateUpdaterRunner(packagePath, manifest, asset, updaterPath, readySignalPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + QuoteForCommandLine(runnerPath),
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory,
            WindowStyle = ProcessWindowStyle.Normal
        };
        Process.Start(startInfo);
        progress?.Report(new ProgressUpdate("正在等待热更新程序完成预检...", 100, runnerPath, true));
        await WaitForUpdaterReadyAsync(readySignalPath, runnerPath, progress, cancellationToken);
    }

    private static string CreateUpdaterRunner(
        string packagePath,
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        string updaterPath,
        string readySignalPath)
    {
        var runnerRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ToolboxStableKey,
            "UpdateRunners");
        Directory.CreateDirectory(runnerRoot);

        var logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ToolboxStableKey,
            "UpdateLogs");
        Directory.CreateDirectory(logRoot);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var runnerPath = Path.Combine(runnerRoot, $"RunUpdate-{stamp}.cmd");
        var launchLogPath = Path.Combine(logRoot, $"UpdaterLaunch-{stamp}.log");
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        if (File.Exists(readySignalPath))
        {
            File.Delete(readySignalPath);
        }

        var lines = new[]
        {
            "@echo off",
            "setlocal EnableExtensions EnableDelayedExpansion",
            "chcp 65001 >nul",
            $"set \"LAUNCH_LOG={launchLogPath}\"",
            "echo FantasyTools updater launcher> \"%LAUNCH_LOG%\"",
            $"echo CreatedAt: {DateTime.Now:O}>> \"%LAUNCH_LOG%\"",
            $"echo InstallDir: {installDir}>> \"%LAUNCH_LOG%\"",
            $"echo PackagePath: {packagePath}>> \"%LAUNCH_LOG%\"",
            $"echo TargetVersion: {manifest.Version}>> \"%LAUNCH_LOG%\"",
            $"echo UpdaterPath: {updaterPath}>> \"%LAUNCH_LOG%\"",
            $"echo ReadySignalPath: {readySignalPath}>> \"%LAUNCH_LOG%\"",
            "where powershell.exe>> \"%LAUNCH_LOG%\" 2>&1",
            "echo.",
            "echo FantasyTools updater launcher",
            "echo Log: %LAUNCH_LOG%",
            "echo.",
            "if not exist " + QuoteForBatch(updaterPath) + " (",
            "    echo UpdaterNotFound>> \"%LAUNCH_LOG%\"",
            "    echo Updater script was not found: " + QuoteForBatch(updaterPath),
            "    echo Press any key to close.",
            "    pause >nul",
            "    exit /b 1",
            ")",
            "echo Starting PowerShell updater...",
            "echo PowerShellStart: %DATE% %TIME%>> \"%LAUNCH_LOG%\"",
            "powershell.exe -NoProfile -ExecutionPolicy Bypass -File " +
                $"{QuoteForBatch(updaterPath)} " +
                "-AppProcessId " + QuoteForBatch(processId) + " " +
                "-InstallDir " + QuoteForBatch(installDir) + " " +
                "-PackagePath " + QuoteForBatch(packagePath) + " " +
                "-ExpectedSha256 " + QuoteForBatch(asset.Sha256) + " " +
                "-ExeRelativePath " + QuoteForBatch(EntryExeName) + " " +
                "-ToolboxStableKey " + QuoteForBatch(ToolboxStableKey) + " " +
                "-TargetVersion " + QuoteForBatch(manifest.Version) + " " +
                "-ReadySignalPath " + QuoteForBatch(readySignalPath),
            "set \"EXIT_CODE=!ERRORLEVEL!\"",
            "echo PowerShellExitCode: !EXIT_CODE!>> \"%LAUNCH_LOG%\"",
            "if not \"!EXIT_CODE!\"==\"0\" (",
            "    echo.",
            "    echo Updater failed. Error output was written to: %LAUNCH_LOG%",
            "    echo Please keep this window open for troubleshooting.",
            "    echo Press any key to close.",
            "    pause >nul",
            "    exit /b !EXIT_CODE!",
            ")",
            "endlocal",
            "exit /b 0"
        };

        File.WriteAllText(runnerPath, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        return runnerPath;
    }

    private static string CreateReadySignalPath()
    {
        var signalRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ToolboxStableKey,
            "UpdateRunners");
        Directory.CreateDirectory(signalRoot);
        return Path.Combine(signalRoot, $"UpdaterReady-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.signal");
    }

    private static async Task WaitForUpdaterReadyAsync(
        string readySignalPath,
        string runnerPath,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var timeout = TimeSpan.FromSeconds(90);
        while (DateTimeOffset.Now - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(readySignalPath))
            {
                var text = await File.ReadAllTextAsync(readySignalPath, cancellationToken);
                if (text.Contains("READY", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new ProgressUpdate("热更新程序已完成预检。", 100, "主程序即将退出并开始覆盖。"));
                    return;
                }
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException($"热更新程序预检超时。启动器：{runnerPath}");
    }

    private static string QuoteForCommandLine(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }

    private static string QuoteForBatch(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    public async Task OpenReleasePageAsync(string releasePageUrl)
    {
        await Task.Run(() =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(releasePageUrl)
                    ? GitHubReleasePageUrl
                    : releasePageUrl,
                UseShellExecute = true
            });
        });
    }

    public static string GetReleaseApiUrl(UpdateSource source)
    {
        return source switch
        {
            UpdateSource.Gitee => GiteeReleaseApiUrl,
            _ => GitHubReleaseApiUrl
        };
    }

    public static string GetReleasePageUrl(UpdateSource source)
    {
        return source switch
        {
            UpdateSource.Gitee => GiteeReleasePageUrl,
            _ => GitHubReleasePageUrl
        };
    }

    public static string GetUpdateSourceDisplayName(UpdateSource source)
    {
        return source switch
        {
            UpdateSource.Gitee => "Gitee",
            _ => "GitHub"
        };
    }

    private static async Task<RemoteRelease?> GetReleaseAsync(UpdateSource source, UpdateChannel channel, CancellationToken cancellationToken)
    {
        return source == UpdateSource.Gitee
            ? await GetGiteeReleaseAsync(channel, cancellationToken)
            : await GetGitHubReleaseAsync(channel, cancellationToken);
    }

    private static async Task<RemoteRelease?> GetGitHubReleaseAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        var url = GitHubReleaseApiUrl;
        if (channel == UpdateChannel.Stable)
        {
            var latestUrl = url.TrimEnd('/') + "/latest";
            var release = await GetJsonAsync(latestUrl, AppJsonSerializerContext.Default.GitHubRelease, cancellationToken);
            return RemoteRelease.FromGitHub(release);
        }

        var releases = await GetJsonAsync(url, AppJsonSerializerContext.Default.ListGitHubRelease, cancellationToken);
        return releases
            .Select(RemoteRelease.FromGitHub)
            .OrderByDescending(GetReleaseSortVersion)
            .ThenBy(release => release.Prerelease)
            .ThenByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static async Task<RemoteRelease?> GetGiteeReleaseAsync(UpdateChannel channel, CancellationToken cancellationToken)
    {
        var releases = await GetJsonAsync(GiteeReleaseApiUrl, AppJsonSerializerContext.Default.ListGiteeRelease, cancellationToken);
        var release = channel == UpdateChannel.Stable
            ? releases
                .Where(item => !item.Prerelease)
                .OrderByDescending(GetReleaseSortVersion)
                .ThenByDescending(item => item.CreatedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault()
            : releases
                .OrderByDescending(GetReleaseSortVersion)
                .ThenBy(item => item.Prerelease)
                .ThenByDescending(item => item.CreatedAt ?? DateTimeOffset.MinValue)
                .FirstOrDefault();
        if (release is null)
        {
            return null;
        }

        var assetsUrl = $"{GiteeReleaseApiUrl}/{release.Id}/attach_files?per_page=100";
        var attachFiles = await GetJsonAsync(assetsUrl, AppJsonSerializerContext.Default.ListGiteeAttachFile, cancellationToken);
        return RemoteRelease.FromGitee(release, attachFiles);
    }

    private static async Task<UpdateManifest> BuildManifestAsync(
        RemoteRelease release,
        UpdateChannel channel,
        CancellationToken cancellationToken)
    {
        var manifestAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, ManifestAssetName, StringComparison.OrdinalIgnoreCase));
        if (manifestAsset is not null)
        {
            var manifest = await GetJsonAsync(manifestAsset.BrowserDownloadUrl, AppJsonSerializerContext.Default.UpdateManifest, cancellationToken);
            FillReleaseInfo(manifest, release);
            return manifest;
        }

        var version = TrimVersionPrefix(release.TagName);
        var zipAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains(RuntimeIdentifier, StringComparison.OrdinalIgnoreCase));
        var shaAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains(RuntimeIdentifier, StringComparison.OrdinalIgnoreCase));
        if (zipAsset is null || shaAsset is null)
        {
            throw new InvalidOperationException("Release 缺少 zip 或 sha256 asset。");
        }

        var shaText = await HttpClient.GetStringAsync(shaAsset.BrowserDownloadUrl, cancellationToken);
        return new UpdateManifest
        {
            Version = version,
            Channel = release.Prerelease ? "beta" : "stable",
            PublishedAt = release.PublishedAt,
            ReleaseNotesUrl = release.HtmlUrl,
            ReleaseNotes = NormalizeReleaseNotes(release.Body),
            Assets =
            [
                new UpdateAssetManifest
                {
                    Runtime = RuntimeIdentifier,
                    FileName = zipAsset.Name,
                    SizeBytes = zipAsset.Size,
                    Sha256 = ParseSha256Text(shaText),
                    DownloadUrl = zipAsset.BrowserDownloadUrl
                }
            ]
        };
    }

    private static void FillReleaseInfo(UpdateManifest manifest, RemoteRelease release)
    {
        foreach (var asset in manifest.Assets)
        {
            var remoteAsset = release.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, asset.FileName, StringComparison.OrdinalIgnoreCase));
            if (remoteAsset is not null)
            {
                asset.DownloadUrl = remoteAsset.BrowserDownloadUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl))
        {
            manifest.ReleaseNotesUrl = release.HtmlUrl;
        }

        if (string.IsNullOrWhiteSpace(manifest.ReleaseNotes))
        {
            manifest.ReleaseNotes = NormalizeReleaseNotes(release.Body);
        }
    }

    private static string NormalizeReleaseNotes(string? releaseNotes)
    {
        return string.IsNullOrWhiteSpace(releaseNotes)
            ? "本次 Release 没有填写更新公告。"
            : releaseNotes.Trim();
    }

    private static UpdateAssetManifest? SelectRuntimeAsset(UpdateManifest manifest)
    {
        return manifest.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Runtime, RuntimeIdentifier, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidatePackage(string packagePath, UpdateManifest manifest, UpdateAssetManifest asset)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var packageManifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), "update-package.json", StringComparison.OrdinalIgnoreCase));
        if (packageManifestEntry is null)
        {
            throw new InvalidOperationException("更新包缺少 update-package.json。");
        }

        using var stream = packageManifestEntry.Open();
        var packageManifest = JsonSerializer.Deserialize(stream, AppJsonSerializerContext.Default.UpdatePackageManifest)
            ?? throw new InvalidOperationException("update-package.json 无法读取。");
        if (!string.Equals(packageManifest.ToolboxStableKey, ToolboxStableKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("更新包工具箱标识不匹配。");
        }

        if (!string.Equals(packageManifest.Version, manifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新包版本和清单版本不一致。");
        }

        if (!string.Equals(packageManifest.Runtime, asset.Runtime, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("更新包 Runtime 不匹配。");
        }

        if (archive.Entries.All(entry =>
            !string.Equals(Path.GetFileName(entry.FullName), packageManifest.EntryExe, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("更新包缺少主程序 exe。");
        }
    }

    private static async Task<T> GetJsonAsync<T>(
        string url,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        await using var stream = await HttpClient.GetStreamAsync(url, cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken)
            ?? throw new InvalidOperationException("远端 JSON 无法读取。");
    }

    private static async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string sourceDisplayName,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var normalizedTimeoutSeconds = Math.Clamp(timeoutSeconds, 10, 600);
        using var timeoutCancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(normalizedTimeoutSeconds));
        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellationTokenSource.Token);
        try
        {
            return await operation(linkedCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCancellationTokenSource.IsCancellationRequested)
        {
            throw new TimeoutException($"{sourceDisplayName} 连接超时（{normalizedTimeoutSeconds} 秒）。请检查网络或稍后重试。");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cache cleanup should never block update checks or installation.
        }
    }

    private static string ParseSha256Text(string text)
    {
        var firstToken = text
            .Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstToken) || firstToken.Length < 64)
        {
            throw new InvalidOperationException("sha256 文件格式无效。");
        }

        return firstToken[..64].ToLowerInvariant();
    }

    private static Version ParseVersion(string value)
    {
        if (TryParseVersion(value, out var version))
        {
            return version;
        }

        throw new InvalidOperationException($"版本号格式无效：{value}");
    }

    private static int CompareSemanticVersions(string left, string right)
    {
        var leftParts = SemanticVersionParts.Parse(left);
        var rightParts = SemanticVersionParts.Parse(right);
        var coreCompare = leftParts.CoreVersion.CompareTo(rightParts.CoreVersion);
        if (coreCompare != 0)
        {
            return coreCompare;
        }

        if (leftParts.IsPrerelease && !rightParts.IsPrerelease)
        {
            return -1;
        }

        if (!leftParts.IsPrerelease && rightParts.IsPrerelease)
        {
            return 1;
        }

        if (!leftParts.IsPrerelease && !rightParts.IsPrerelease)
        {
            return 0;
        }

        return ComparePrereleaseLabels(leftParts.PrereleaseLabel, rightParts.PrereleaseLabel);
    }

    private static int ComparePrereleaseLabels(string left, string right)
    {
        var leftTokens = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var rightTokens = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var count = Math.Max(leftTokens.Length, rightTokens.Length);
        for (var index = 0; index < count; index++)
        {
            if (index >= leftTokens.Length)
            {
                return -1;
            }

            if (index >= rightTokens.Length)
            {
                return 1;
            }

            var leftIsNumber = int.TryParse(leftTokens[index], out var leftNumber);
            var rightIsNumber = int.TryParse(rightTokens[index], out var rightNumber);
            if (leftIsNumber && rightIsNumber)
            {
                var numberCompare = leftNumber.CompareTo(rightNumber);
                if (numberCompare != 0)
                {
                    return numberCompare;
                }

                continue;
            }

            if (leftIsNumber && !rightIsNumber)
            {
                return -1;
            }

            if (!leftIsNumber && rightIsNumber)
            {
                return 1;
            }

            var textCompare = string.Compare(leftTokens[index], rightTokens[index], StringComparison.OrdinalIgnoreCase);
            if (textCompare != 0)
            {
                return textCompare;
            }
        }

        return 0;
    }

    private static SemanticVersionParts GetReleaseSortVersion(RemoteRelease release)
    {
        return SemanticVersionParts.Parse(TrimVersionPrefix(release.TagName));
    }

    private static SemanticVersionParts GetReleaseSortVersion(GiteeRelease release)
    {
        return SemanticVersionParts.Parse(TrimVersionPrefix(release.TagName));
    }

    private UpdateChannel GetEffectiveUpdateChannel(UpdateChannel requestedChannel)
    {
        return requestedChannel;
    }

    private sealed class RemoteRelease
    {
        public string TagName { get; private init; } = string.Empty;

        public string HtmlUrl { get; private init; } = string.Empty;

        public string? Body { get; private init; }

        public bool Prerelease { get; private init; }

        public DateTimeOffset? PublishedAt { get; private init; }

        public List<RemoteReleaseAsset> Assets { get; private init; } = [];

        public static RemoteRelease FromGitHub(GitHubRelease release)
        {
            return new RemoteRelease
            {
                TagName = release.TagName,
                HtmlUrl = release.HtmlUrl,
                Body = release.Body,
                Prerelease = release.Prerelease,
                PublishedAt = release.PublishedAt,
                Assets = release.Assets
                    .Select(asset => new RemoteReleaseAsset(asset.Name, asset.Size, asset.BrowserDownloadUrl))
                    .ToList()
            };
        }

        public static RemoteRelease FromGitee(GiteeRelease release, List<GiteeAttachFile> attachFiles)
        {
            return new RemoteRelease
            {
                TagName = release.TagName,
                HtmlUrl = $"{GiteeReleasePageUrl}/tag/{Uri.EscapeDataString(release.TagName)}",
                Body = release.Body,
                Prerelease = release.Prerelease,
                PublishedAt = release.CreatedAt,
                Assets = attachFiles
                    .Select(asset => new RemoteReleaseAsset(asset.Name, asset.Size, asset.BrowserDownloadUrl))
                    .ToList()
            };
        }
    }

    private sealed record RemoteReleaseAsset(
        string Name,
        long Size,
        string BrowserDownloadUrl);

    private static string ResolveCurrentVersionText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyPath = assembly.Location;
        var fileVersionInfo = string.IsNullOrWhiteSpace(assemblyPath)
            ? null
            : FileVersionInfo.GetVersionInfo(assemblyPath);
        var candidates = new[]
        {
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            assembly.GetName().Version?.ToString(),
            fileVersionInfo?.ProductVersion,
            fileVersionInfo?.FileVersion,
            "1.0.0"
        };

        foreach (var candidate in candidates)
        {
            if (TryParseVersion(candidate, out _))
            {
                return NormalizeVersionText(candidate);
            }
        }

        return "1.0.0";
    }

    private static Version ResolveCurrentVersion()
    {
        return ParseVersion(ResolveCurrentVersionText());
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = TrimVersionPrefix(value);
        var metadataIndex = text.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            text = text[..metadataIndex];
        }

        var prereleaseIndex = text.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            text = text[..prereleaseIndex];
        }

        text = text.Trim();
        return Version.TryParse(text, out version!);
    }

    private static string NormalizeVersionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "1.0.0";
        }

        var text = TrimVersionPrefix(value);
        var metadataIndex = text.IndexOf('+', StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            text = text[..metadataIndex];
        }

        return text.Trim();
    }

    private static string TrimVersionPrefix(string value)
    {
        return value.Trim().TrimStart('v', 'V');
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FantasyTools-Updater/1.0");
        return client;
    }

    private readonly record struct SemanticVersionParts(
        Version CoreVersion,
        string PrereleaseLabel) : IComparable<SemanticVersionParts>
    {
        public bool IsPrerelease => !string.IsNullOrWhiteSpace(PrereleaseLabel);

        public static SemanticVersionParts Parse(string value)
        {
            var text = NormalizeVersionText(value);
            var prereleaseIndex = text.IndexOf('-', StringComparison.Ordinal);
            var coreText = prereleaseIndex >= 0
                ? text[..prereleaseIndex]
                : text;
            var label = prereleaseIndex >= 0
                ? text[(prereleaseIndex + 1)..]
                : string.Empty;

            if (!Version.TryParse(coreText, out var coreVersion))
            {
                coreVersion = new Version(0, 0, 0);
            }

            return new SemanticVersionParts(coreVersion, label);
        }

        public int CompareTo(SemanticVersionParts other)
        {
            var coreCompare = CoreVersion.CompareTo(other.CoreVersion);
            if (coreCompare != 0)
            {
                return coreCompare;
            }

            if (IsPrerelease && !other.IsPrerelease)
            {
                return -1;
            }

            if (!IsPrerelease && other.IsPrerelease)
            {
                return 1;
            }

            if (!IsPrerelease && !other.IsPrerelease)
            {
                return 0;
            }

            return ComparePrereleaseLabels(PrereleaseLabel, other.PrereleaseLabel);
        }
    }
}
