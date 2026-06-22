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

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public string UpdatesDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ToolboxStableKey,
        "Updates");

    public Version CurrentVersion { get; } = ParseVersion(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ??
        "1.0.0");

    public async Task<UpdateCheckResult> CheckAsync(
        string releasesApiUrl,
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var release = await RunWithTimeoutAsync(
            token => GetReleaseAsync(releasesApiUrl, channel, token),
            timeoutSeconds,
            cancellationToken);
        if (release is null)
        {
            return new UpdateCheckResult(false, CurrentVersion, null, null, null, "没有找到可用 Release。", string.Empty);
        }

        var manifest = await RunWithTimeoutAsync(
            token => BuildManifestAsync(release, channel, token),
            timeoutSeconds,
            cancellationToken);
        var latestVersion = ParseVersion(manifest.Version);
        if (latestVersion <= CurrentVersion)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersion,
                latestVersion,
                manifest,
                null,
                $"当前已是最新版本：{CurrentVersion}",
                manifest.ReleaseNotesUrl);
        }

        if (manifest.RequiresManualMigration)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersion,
                latestVersion,
                manifest,
                null,
                $"发现 {latestVersion}，但该版本需要手动更新。",
                manifest.ReleaseNotesUrl);
        }

        var asset = SelectRuntimeAsset(manifest);
        if (asset is null)
        {
            return new UpdateCheckResult(
                false,
                CurrentVersion,
                latestVersion,
                manifest,
                null,
                $"发现 {latestVersion}，但没有 {RuntimeIdentifier} 更新包。",
                manifest.ReleaseNotesUrl);
        }

        return new UpdateCheckResult(
            true,
            CurrentVersion,
            latestVersion,
            manifest,
            asset,
            $"发现新版本：{CurrentVersion} -> {latestVersion}",
            manifest.ReleaseNotesUrl);
    }

    public async Task<UpdateConnectionTestResult> MeasureConnectionAsync(
        string releasesApiUrl,
        UpdateChannel channel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var release = await RunWithTimeoutAsync(
            token => GetReleaseAsync(releasesApiUrl, channel, token),
            timeoutSeconds,
            cancellationToken);
        stopwatch.Stop();

        return release is null
            ? new UpdateConnectionTestResult(true, stopwatch.Elapsed, "GitHub 可以连接，但当前通道没有找到 Release。")
            : new UpdateConnectionTestResult(true, stopwatch.Elapsed, $"GitHub 连接正常，最新远端版本：{release.TagName}");
    }

    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateManifest manifest,
        UpdateAssetManifest asset,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
        {
            throw new InvalidOperationException("更新包下载地址为空。");
        }

        Directory.CreateDirectory(UpdatesDirectoryPath);
        var safeFileName = Path.GetFileName(asset.FileName);
        var packagePath = Path.Combine(UpdatesDirectoryPath, safeFileName);
        var tempPath = packagePath + ".download";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        progress?.Report(new ProgressUpdate("正在下载更新包...", 8, asset.FileName));
        using var response = await HttpClient.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength ?? asset.SizeBytes;
        await using (var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var fileStream = File.Create(tempPath))
        {
            var buffer = new byte[1024 * 128];
            long downloadedBytes = 0;
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
        return new UpdateDownloadResult(packagePath, manifest, asset);
    }

    public void LaunchUpdater(string packagePath, UpdateManifest manifest, UpdateAssetManifest asset)
    {
        var updaterPath = Path.Combine(AppContext.BaseDirectory, "Scripts", "热更新覆盖.ps1");
        if (!File.Exists(updaterPath))
        {
            throw new FileNotFoundException("热更新脚本不存在。", updaterPath);
        }

        var process = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        foreach (var argument in new[]
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            updaterPath,
            "-AppProcessId",
            process,
            "-InstallDir",
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            "-PackagePath",
            packagePath,
            "-ExpectedSha256",
            asset.Sha256,
            "-ExeRelativePath",
            EntryExeName,
            "-ToolboxStableKey",
            ToolboxStableKey,
            "-TargetVersion",
            manifest.Version
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }

    public async Task OpenReleasePageAsync(string releasePageUrl)
    {
        await Task.Run(() =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = string.IsNullOrWhiteSpace(releasePageUrl)
                    ? "https://github.com/kirito0000001/FantasyTools/releases"
                    : releasePageUrl,
                UseShellExecute = true
            });
        });
    }

    private static async Task<GitHubRelease?> GetReleaseAsync(string releasesApiUrl, UpdateChannel channel, CancellationToken cancellationToken)
    {
        var url = string.IsNullOrWhiteSpace(releasesApiUrl)
            ? "https://api.github.com/repos/kirito0000001/FantasyTools/releases"
            : releasesApiUrl;
        if (channel == UpdateChannel.Stable)
        {
            var latestUrl = url.TrimEnd('/') + "/latest";
            return await GetJsonAsync(latestUrl, AppJsonSerializerContext.Default.GitHubRelease, cancellationToken);
        }

        var releases = await GetJsonAsync(url, AppJsonSerializerContext.Default.ListGitHubRelease, cancellationToken);
        return releases.FirstOrDefault(release => release.Prerelease);
    }

    private static async Task<UpdateManifest> BuildManifestAsync(
        GitHubRelease release,
        UpdateChannel channel,
        CancellationToken cancellationToken)
    {
        var manifestAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, ManifestAssetName, StringComparison.OrdinalIgnoreCase));
        if (manifestAsset is not null)
        {
            var manifest = await GetJsonAsync(manifestAsset.BrowserDownloadUrl, AppJsonSerializerContext.Default.UpdateManifest, cancellationToken);
            FillAssetDownloadUrls(manifest, release);
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
            Channel = channel == UpdateChannel.Beta ? "beta" : "stable",
            PublishedAt = release.PublishedAt,
            ReleaseNotesUrl = release.HtmlUrl,
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

    private static void FillAssetDownloadUrls(UpdateManifest manifest, GitHubRelease release)
    {
        foreach (var asset in manifest.Assets)
        {
            if (!string.IsNullOrWhiteSpace(asset.DownloadUrl))
            {
                continue;
            }

            var githubAsset = release.Assets.FirstOrDefault(item =>
                string.Equals(item.Name, asset.FileName, StringComparison.OrdinalIgnoreCase));
            if (githubAsset is not null)
            {
                asset.DownloadUrl = githubAsset.BrowserDownloadUrl;
            }
        }

        if (string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl))
        {
            manifest.ReleaseNotesUrl = release.HtmlUrl;
        }
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
            throw new TimeoutException($"GitHub 连接超时（{normalizedTimeoutSeconds} 秒）。请检查网络或稍后重试。");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
        var text = TrimVersionPrefix(value);
        return Version.TryParse(text, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static string TrimVersionPrefix(string value)
    {
        return value.Trim().TrimStart('v', 'V');
    }

    private static string FormatBytes(long bytes)
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
}
