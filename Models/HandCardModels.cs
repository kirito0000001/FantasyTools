using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;

namespace FantasyTools.Models;

internal sealed record HandCardCreateInput(
    string Code,
    string CardFaceSourcePath,
    Rectangle? CardFaceCrop = null,
    string Name = "",
    string Suit = "Hearts",
    int PokerNumber = 1);

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

internal sealed class BasicDeckSettings
{
    [JsonPropertyName("slots")]
    public Dictionary<string, string> Slots { get; set; } = [];
}

internal sealed record BasicDeckSlotBinding(
    int DeckIndex,
    string Suit,
    int Number,
    string SlotKey)
{
    public string DisplayName => $"{DeckDisplayName} {SuitDeckSlotViewModelNameFormatter.FormatSuitNumber(Suit, Number)}";

    private string DeckDisplayName => DeckIndex switch
    {
        1 => "第一套",
        2 => "第二套",
        3 => "第三套",
        4 => "第四套",
        _ => $"第 {DeckIndex} 套"
    };
}

internal static class SuitDeckSlotViewModelNameFormatter
{
    public static string FormatSuitNumber(string suit, int number)
    {
        var suitName = suit switch
        {
            "Hearts" => "红桃",
            "Diamonds" => "方片",
            "Clubs" => "梅花",
            "Spade" => "黑桃",
            _ => suit
        };
        var label = number switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return $"{suitName} {label}";
    }
}
