using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FantasyTools.Models;
using FantasyTools.Services;

namespace FantasyTools.ViewModels;

internal sealed class HandCardsViewModel : ObservableObject
{
    private readonly HandCardWorkspaceService _handCardWorkspaceService;
    private readonly string _defaultCardFacePath;

    public HandCardsViewModel(HandCardWorkspaceService handCardWorkspaceService, string defaultCardFacePath)
    {
        _handCardWorkspaceService = handCardWorkspaceService;
        _defaultCardFacePath = defaultCardFacePath;
        Cards.Add(HandCardViewModel.CreateAddCard(_defaultCardFacePath));
    }

    public ObservableCollection<HandCardViewModel> Cards { get; } = [];

    public ObservableCollection<SuitDeckSlotViewModel> SuitDeckSlots { get; } = [];

    public int FilledSuitDeckSlotCount => SuitDeckSlots.Count(slot => slot.IsFilled);

    public int MissingSuitDeckSlotCount => SuitDeckSlots.Count(slot => !slot.IsFilled);

    public string SuitDeckSummary => $"已填入 {FilledSuitDeckSlotCount} / 52；缺少 {MissingSuitDeckSlotCount}。";

    public void Load(string projectRootPath)
    {
        Cards.Clear();
        Cards.Add(HandCardViewModel.CreateAddCard(_defaultCardFacePath));

        var handCards = _handCardWorkspaceService.GetHandCards(projectRootPath);
        foreach (var handCard in handCards)
        {
            Cards.Add(HandCardViewModel.FromHandCard(handCard, _defaultCardFacePath));
        }

        RebuildSuitDeckSlots(handCards);
        OnPropertyChanged(nameof(FilledSuitDeckSlotCount));
        OnPropertyChanged(nameof(MissingSuitDeckSlotCount));
        OnPropertyChanged(nameof(SuitDeckSummary));
    }

    private void RebuildSuitDeckSlots(IReadOnlyList<HandCardInfo> handCards)
    {
        SuitDeckSlots.Clear();
        foreach (var suit in HandCardDetailViewModel.SuitOptions)
        {
            for (var number = 1; number <= 13; number++)
            {
                var matchedCards = handCards
                    .Where(card =>
                        string.Equals(card.Meta.Suit, suit.Value, System.StringComparison.OrdinalIgnoreCase) &&
                        card.Meta.PokerNumber == number)
                    .OrderBy(card => card.Meta.Name, System.StringComparer.OrdinalIgnoreCase)
                    .ThenBy(card => card.Code, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();
                SuitDeckSlots.Add(new SuitDeckSlotViewModel(suit.Value, suit.DisplayName, number, matchedCards));
            }
        }
    }
}

internal sealed class HandCardViewModel
{
    private HandCardViewModel(
        bool isAddCard,
        string code,
        string name,
        string cardFacePath,
        bool usesDefaultCardFace)
    {
        IsAddCard = isAddCard;
        Code = code;
        Name = name;
        CardFacePath = cardFacePath;
        UsesDefaultCardFace = usesDefaultCardFace;
    }

    public bool IsAddCard { get; }

    public string Code { get; }

    public string Name { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : Name;

    public string CardFacePath { get; }

    public bool UsesDefaultCardFace { get; }

    public string AutomationName => IsAddCard
        ? "新建手牌"
        : $"手牌：{DisplayName}，{Code}";

    public static HandCardViewModel CreateAddCard(string defaultCardFacePath)
    {
        return new HandCardViewModel(true, string.Empty, string.Empty, defaultCardFacePath, true);
    }

    public static HandCardViewModel FromHandCard(HandCardInfo handCard, string defaultCardFacePath)
    {
        var cardFacePath = File.Exists(handCard.CardFacePath)
            ? handCard.CardFacePath
            : defaultCardFacePath;
        return new HandCardViewModel(
            false,
            handCard.Code,
            handCard.Name,
            cardFacePath,
            handCard.UsesDefaultCardFace || !File.Exists(handCard.CardFacePath));
    }
}

internal sealed class SuitDeckSlotViewModel
{
    private readonly IReadOnlyList<HandCardInfo> _cards;

    public SuitDeckSlotViewModel(string suit, string suitDisplayName, int number, IReadOnlyList<HandCardInfo> cards)
    {
        Suit = suit;
        SuitDisplayName = suitDisplayName;
        Number = number;
        _cards = cards;
    }

    public string Suit { get; }

    public string SuitDisplayName { get; }

    public int Number { get; }

    public bool IsFilled => _cards.Count > 0;

    public bool HasMultipleCards => _cards.Count > 1;

    public int CardCount => _cards.Count;

    public string CardCode => _cards.FirstOrDefault()?.Code ?? string.Empty;

    public string DisplayTitle => FormatSuitNumber(Suit, Number);

    public string DisplaySubtitle => IsFilled
        ? _cards.First().Name
        : "未填入";

    public string StatusText => IsFilled
        ? (HasMultipleCards ? $"{CardCount} 张" : "已填入")
        : "缺少";

    public string AutomationName => IsFilled
        ? $"{DisplayTitle} 已填入 {DisplaySubtitle}"
        : $"{DisplayTitle} 未填入";

    public static string FormatSuitNumber(string suit, int number)
    {
        var suitName = HandCardDetailViewModel.SuitOptions.FirstOrDefault(option => option.Value == suit)?.DisplayName ?? suit;
        return $"{suitName} {FormatNumber(number)}";
    }

    public static string BuildDefaultCardName(string suit, int number)
    {
        return FormatSuitNumber(suit, number);
    }

    private static string FormatNumber(int number)
    {
        return number switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => number.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
