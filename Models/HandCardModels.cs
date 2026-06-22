using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;

namespace FantasyTools.Models;

internal sealed record HandCardCreateInput(
    string Code,
    string CardFaceSourcePath,
    Rectangle? CardFaceCrop = null);

internal sealed class HandCardMeta
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("cardFaceFileName")]
    public string CardFaceFileName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("suit")]
    public string Suit { get; set; } = "Hearts";

    [JsonPropertyName("pokerNumber")]
    public int PokerNumber { get; set; } = 1;

    [JsonPropertyName("cardType")]
    public string CardType { get; set; } = "Base";

    [JsonPropertyName("functionGroups")]
    public List<string> FunctionGroups { get; set; } = [];

    [JsonPropertyName("remainingUseCount")]
    public int RemainingUseCount { get; set; } = -1;

    [JsonPropertyName("equipType")]
    public string EquipType { get; set; } = "Weapon";

    [JsonPropertyName("value")]
    public int Value { get; set; }

    [JsonPropertyName("expression")]
    public string Expression { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

internal sealed record HandCardInfo(
    string Code,
    string Name,
    string Path,
    string CardFacePath,
    HandCardMeta Meta,
    bool UsesDefaultCardFace = false);
