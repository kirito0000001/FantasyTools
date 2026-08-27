using System;
using System.Collections.Generic;
using System.Text.Json;
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
    public JsonElement PrereleaseValue { get; set; }

    [JsonIgnore]
    public bool Prerelease => PrereleaseValue.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(PrereleaseValue.GetString(), out var value) && value,
        _ => false
    };

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

internal sealed class GiteeRelease
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

internal sealed class GiteeAttachFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;
}

internal sealed class UpdateManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("toolboxStableKey")]
    public string ToolboxStableKey { get; set; } = "FantasyTools";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "幻杀工具箱";

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";

    [JsonPropertyName("publishedAt")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("releaseNotesUrl")]
    public string ReleaseNotesUrl { get; set; } = string.Empty;

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("requiresManualMigration")]
    public bool RequiresManualMigration { get; set; }

    [JsonPropertyName("requiresRestart")]
    public bool RequiresRestart { get; set; } = true;

    [JsonPropertyName("assets")]
    public List<UpdateAssetManifest> Assets { get; set; } = [];
}

internal sealed class UpdateAssetManifest
{
    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = string.Empty;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

}


internal sealed class UpdatePackageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("toolboxStableKey")]
    public string ToolboxStableKey { get; set; } = "FantasyTools";

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("runtime")]
    public string Runtime { get; set; } = string.Empty;

    [JsonPropertyName("entryExe")]
    public string EntryExe { get; set; } = "幻杀工具箱.exe";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed record UpdateCheckResult(
    bool HasUpdate,
    Version CurrentVersion,
    Version? LatestVersion,
    string CurrentVersionText,
    string? LatestVersionText,
    UpdateManifest? Manifest,
    UpdateAssetManifest? Asset,
    string Message,
    string ReleasePageUrl,
    string ReleaseNotes);

internal sealed record UpdateDownloadResult(
    string PackagePath,
    UpdateManifest Manifest,
    UpdateAssetManifest Asset);

internal sealed record UpdateConnectionTestResult(
    bool IsSuccess,
    TimeSpan Elapsed,
    string Message);
