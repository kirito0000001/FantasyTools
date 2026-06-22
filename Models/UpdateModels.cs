using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FantasyTools.Models;

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = [];
}

internal sealed class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

internal sealed class UpdateManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ToolboxStableKey { get; set; } = "FantasyTools";

    public string DisplayName { get; set; } = "幻杀工具箱";

    public string Version { get; set; } = string.Empty;

    public string Channel { get; set; } = "stable";

    public DateTimeOffset? PublishedAt { get; set; }

    public string ReleaseNotesUrl { get; set; } = string.Empty;

    public bool RequiresManualMigration { get; set; }

    public bool RequiresRestart { get; set; } = true;

    public List<UpdateAssetManifest> Assets { get; set; } = [];
}

internal sealed class UpdateAssetManifest
{
    public string Runtime { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;
}

internal sealed class UpdatePackageManifest
{
    public int SchemaVersion { get; set; } = 1;

    public string ToolboxStableKey { get; set; } = "FantasyTools";

    public string Version { get; set; } = string.Empty;

    public string Runtime { get; set; } = string.Empty;

    public string EntryExe { get; set; } = "幻杀工具箱.exe";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed record UpdateCheckResult(
    bool HasUpdate,
    Version CurrentVersion,
    Version? LatestVersion,
    UpdateManifest? Manifest,
    UpdateAssetManifest? Asset,
    string Message,
    string ReleasePageUrl);

internal sealed record UpdateDownloadResult(
    string PackagePath,
    UpdateManifest Manifest,
    UpdateAssetManifest Asset);
