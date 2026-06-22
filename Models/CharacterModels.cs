using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;

namespace FantasyTools.Models;

internal sealed record CharacterCreateInput(
    string Code,
    string CardFaceSourcePath,
    Rectangle? CardFaceCrop = null);

internal sealed class CharacterMeta
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cardFaceFileName")]
    public string CardFaceFileName { get; set; } = string.Empty;

    [JsonPropertyName("backgroundImageFileName")]
    public string BackgroundImageFileName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("health")]
    public int Health { get; set; } = 30;

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("skillGroups")]
    public List<string> SkillGroups { get; set; } = [];

    [JsonPropertyName("skills")]
    public List<CharacterSkillMeta> Skills { get; set; } = [];

    [JsonPropertyName("carryCards")]
    public List<string> CarryCards { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed class CharacterSkillMeta
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("function")]
    public string Function { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

internal sealed record CharacterInfo(
    string Code,
    string Name,
    string Path,
    string CardFacePath,
    string BackgroundImagePath,
    CharacterMeta Meta,
    bool UsesDefaultCardFace = false);
