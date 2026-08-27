using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FantasyTools.Models;

internal sealed class WorkspacePackageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("packageKind")]
    public string PackageKind { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("items")]
    public List<string> Items { get; set; } = [];
}

internal enum WorkspaceTransferKind
{
    Characters,
    HandCards
}

internal enum WorkspaceImportConflictPolicy
{
    Skip,
    Replace
}

internal sealed record WorkspaceImportResult(
    int ImportedCount,
    int ReplacedCount,
    int SkippedCount,
    IReadOnlyList<string> ImportedCodes)
{
    public int ChangedCount => ImportedCount + ReplacedCount;

    public string Summary => $"新增 {ImportedCount}，覆盖 {ReplacedCount}，跳过 {SkippedCount}";
}
