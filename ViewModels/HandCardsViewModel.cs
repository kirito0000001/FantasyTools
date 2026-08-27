using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System;
using FantasyTools.Models;
using FantasyTools.Services;
using Microsoft.UI.Xaml.Controls;

namespace FantasyTools.ViewModels;

internal sealed class HandCardsViewModel : ObservableObject
{
    private const int TotalSuitDeckSlotCount = 208;
    private readonly HandCardWorkspaceService _handCardWorkspaceService;
    private readonly string _defaultCardFacePath;
    private IReadOnlyList<HandCardInfo> _allHandCards = [];
    private SuitDeckSetViewModel? _selectedSuitDeckGroup;
    private string _searchText = string.Empty;
    private HandCardSortKey _sortKey = HandCardSortKey.UpdatedAt;
    private bool _sortDescending = true;
    private bool _useSuitColoredCards = true;

    public HandCardsViewModel(HandCardWorkspaceService handCardWorkspaceService, string defaultCardFacePath)
    {
        _handCardWorkspaceService = handCardWorkspaceService;
        _defaultCardFacePath = defaultCardFacePath;
        Cards.Add(HandCardViewModel.CreateAddCard(_defaultCardFacePath));
    }

    public ObservableCollection<HandCardViewModel> Cards { get; } = [];

    public ObservableCollection<SuitDeckSlotViewModel> SuitDeckSlots { get; } = [];

    public ObservableCollection<SuitDeckSetViewModel> SuitDeckGroups { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RebuildCards();
            }
        }
    }

    public bool FilterBaseCards { get; private set; }

    public bool FilterEventCards { get; private set; }

    public bool FilterEquipWeaponCards { get; private set; }

    public bool FilterEquipArmorCards { get; private set; }

    public bool FilterEquipPropCards { get; private set; }

    public bool FilterJudgeCards { get; private set; }

    public bool FilterHearts { get; private set; }

    public bool FilterDiamonds { get; private set; }

    public bool FilterClubs { get; private set; }

    public bool FilterSpades { get; private set; }

    public bool FilterBoundToBasicDeck { get; private set; }

    public bool FilterUnboundToBasicDeck { get; private set; }

    public bool FilterMissingName { get; private set; }

    public bool FilterIncomplete { get; private set; }

    public bool FilterLimitedUse { get; private set; }

    public HandCardSortKey SortKey => _sortKey;

    public bool SortDescending => _sortDescending;

    public string SortSummary => $"排序：{GetSortDisplayName(SortKey)} / {(SortDescending ? "降序" : "升序")}";

    public bool UseSuitColoredCards
    {
        get => _useSuitColoredCards;
        set
        {
            if (SetProperty(ref _useSuitColoredCards, value))
            {
                RebuildCards();
            }
        }
    }

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        FilterBaseCards ||
        FilterEventCards ||
        FilterEquipWeaponCards ||
        FilterEquipArmorCards ||
        FilterEquipPropCards ||
        FilterJudgeCards ||
        FilterHearts ||
        FilterDiamonds ||
        FilterClubs ||
        FilterSpades ||
        FilterBoundToBasicDeck ||
        FilterUnboundToBasicDeck ||
        FilterMissingName ||
        FilterIncomplete ||
        FilterLimitedUse;

    public string FilterSummary => HasActiveFilters
        ? $"总牌数：{_allHandCards.Count} / 筛选后：{Cards.Count(card => !card.IsAddCard)}"
        : $"总牌数：{_allHandCards.Count} / 筛选后：{_allHandCards.Count}";

    public int FilledSuitDeckSlotCount => SuitDeckSlots.Count(slot => slot.IsFilled);

    public int MissingSuitDeckSlotCount => Math.Max(TotalSuitDeckSlotCount - FilledSuitDeckSlotCount, 0);

    public string SuitDeckSummary => $"已填入 {FilledSuitDeckSlotCount} / {TotalSuitDeckSlotCount}；缺少 {MissingSuitDeckSlotCount}。";

    public InfoBarSeverity HandCardsNoticeSeverity => MissingSuitDeckSlotCount == 0
        ? InfoBarSeverity.Success
        : InfoBarSeverity.Warning;

    public string HandCardsNoticeTitle => MissingSuitDeckSlotCount == 0
        ? "牌堆已完成"
        : "牌堆未完成";

    public string HandCardsNoticeMessage => MissingSuitDeckSlotCount == 0
        ? $"牌堆 {TotalSuitDeckSlotCount} 个槽位已全部设置完成；手牌数据会继续自动保存到整体项目目录。"
        : $"牌堆还有 {MissingSuitDeckSlotCount} 个槽位未设置。请进入牌堆设置补齐后再对外使用。";

    public SuitDeckSetViewModel? SelectedSuitDeckGroup
    {
        get => _selectedSuitDeckGroup;
        set => SetProperty(ref _selectedSuitDeckGroup, value);
    }

    public void Load(string projectRootPath)
    {
        _allHandCards = _handCardWorkspaceService.GetHandCards(projectRootPath);
        RebuildSuitDeckSlots(projectRootPath, _allHandCards);
        SelectedSuitDeckGroup = SuitDeckGroups.FirstOrDefault(group =>
            group.DeckIndex == SelectedSuitDeckGroup?.DeckIndex) ?? SuitDeckGroups.FirstOrDefault();
        RebuildCards();
        NotifyBasicDeckStateChanged();
    }

    public void RefreshCardVisuals()
    {
        RebuildCards();
    }

    public void SetFilters(
        bool baseCards,
        bool eventCards,
        bool equipWeaponCards,
        bool equipArmorCards,
        bool equipPropCards,
        bool judgeCards,
        bool hearts,
        bool diamonds,
        bool clubs,
        bool spades,
        bool boundToBasicDeck,
        bool unboundToBasicDeck,
        bool missingName,
        bool incomplete,
        bool limitedUse)
    {
        FilterBaseCards = baseCards;
        FilterEventCards = eventCards;
        FilterEquipWeaponCards = equipWeaponCards;
        FilterEquipArmorCards = equipArmorCards;
        FilterEquipPropCards = equipPropCards;
        FilterJudgeCards = judgeCards;
        FilterHearts = hearts;
        FilterDiamonds = diamonds;
        FilterClubs = clubs;
        FilterSpades = spades;
        FilterBoundToBasicDeck = boundToBasicDeck;
        FilterUnboundToBasicDeck = unboundToBasicDeck;
        FilterMissingName = missingName;
        FilterIncomplete = incomplete;
        FilterLimitedUse = limitedUse;
        RebuildCards();
    }

    public void ClearFilters()
    {
        SetFilters(false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
    }

    public void SetSort(HandCardSortKey sortKey, bool descending)
    {
        _sortKey = sortKey;
        _sortDescending = descending;
        RebuildCards();
    }

    private void RebuildCards()
    {
        Cards.Clear();
        Cards.Add(HandCardViewModel.CreateAddCard(_defaultCardFacePath));

        foreach (var handCard in ApplySort(_allHandCards.Where(MatchesFilters)))
        {
            Cards.Add(HandCardViewModel.FromHandCard(handCard, _defaultCardFacePath, UseSuitColoredCards));
        }

        OnPropertyChanged(nameof(FilterBaseCards));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(FilterEventCards));
        OnPropertyChanged(nameof(FilterEquipWeaponCards));
        OnPropertyChanged(nameof(FilterEquipArmorCards));
        OnPropertyChanged(nameof(FilterEquipPropCards));
        OnPropertyChanged(nameof(FilterJudgeCards));
        OnPropertyChanged(nameof(FilterHearts));
        OnPropertyChanged(nameof(FilterDiamonds));
        OnPropertyChanged(nameof(FilterClubs));
        OnPropertyChanged(nameof(FilterSpades));
        OnPropertyChanged(nameof(FilterBoundToBasicDeck));
        OnPropertyChanged(nameof(FilterUnboundToBasicDeck));
        OnPropertyChanged(nameof(FilterMissingName));
        OnPropertyChanged(nameof(FilterIncomplete));
        OnPropertyChanged(nameof(FilterLimitedUse));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(SortKey));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(SortSummary));
    }

    private bool MatchesFilters(HandCardInfo handCard)
    {
        if (!MatchesSearch(handCard))
        {
            return false;
        }

        var hasTypeFilter = FilterBaseCards ||
            FilterEventCards ||
            FilterEquipWeaponCards ||
            FilterEquipArmorCards ||
            FilterEquipPropCards ||
            FilterJudgeCards;
        if (hasTypeFilter && !MatchesCardTypeFilter(handCard))
        {
            return false;
        }

        var hasSuitFilter = FilterHearts || FilterDiamonds || FilterClubs || FilterSpades;
        if (hasSuitFilter && !MatchesSuitFilter(handCard.Meta.Suit))
        {
            return false;
        }

        var isBoundToBasicDeck = SuitDeckSlots.Any(slot =>
            slot.IsFilled && string.Equals(slot.CardCode, handCard.Code, StringComparison.OrdinalIgnoreCase));
        if (FilterBoundToBasicDeck && !isBoundToBasicDeck)
        {
            return false;
        }

        if (FilterUnboundToBasicDeck && isBoundToBasicDeck)
        {
            return false;
        }

        if (FilterMissingName && HasChineseName(handCard))
        {
            return false;
        }

        if (FilterIncomplete && IsComplete(handCard))
        {
            return false;
        }

        if (FilterLimitedUse && handCard.Meta.RemainingUseCount == -1)
        {
            return false;
        }

        return true;
    }

    private bool MatchesSearch(HandCardInfo handCard)
    {
        var keyword = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return ContainsText(handCard.Code, keyword) ||
            ContainsText(handCard.Name, keyword);
    }

    private IEnumerable<HandCardInfo> ApplySort(IEnumerable<HandCardInfo> handCards)
    {
        return SortKey switch
        {
            HandCardSortKey.DisplayName => SortDescending
                ? handCards.OrderByDescending(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(card => card.Code, StringComparer.OrdinalIgnoreCase)
                : handCards.OrderBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(card => card.Code, StringComparer.OrdinalIgnoreCase),
            HandCardSortKey.Code => SortDescending
                ? handCards.OrderByDescending(card => card.Code, StringComparer.OrdinalIgnoreCase)
                : handCards.OrderBy(card => card.Code, StringComparer.OrdinalIgnoreCase),
            HandCardSortKey.CardType => SortDescending
                ? handCards.OrderByDescending(GetCardTypeSortOrder).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase)
                : handCards.OrderBy(GetCardTypeSortOrder).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase),
            HandCardSortKey.SuitNumber => SortDescending
                ? handCards.OrderByDescending(GetSuitSortOrder).ThenByDescending(card => card.Meta.PokerNumber).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase)
                : handCards.OrderBy(GetSuitSortOrder).ThenBy(card => card.Meta.PokerNumber).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase),
            HandCardSortKey.RemainingUseCount => SortDescending
                ? handCards.OrderByDescending(card => card.Meta.RemainingUseCount).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase)
                : handCards.OrderBy(card => card.Meta.RemainingUseCount).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase),
            _ => SortDescending
                ? handCards.OrderByDescending(card => card.Meta.UpdatedAt).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase)
                : handCards.OrderBy(card => card.Meta.UpdatedAt).ThenBy(GetHandCardDisplayName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private bool MatchesCardTypeFilter(HandCardInfo handCard)
    {
        return (FilterBaseCards && string.Equals(handCard.Meta.CardType, "Base", StringComparison.OrdinalIgnoreCase)) ||
            (FilterEventCards && string.Equals(handCard.Meta.CardType, "Zdy", StringComparison.OrdinalIgnoreCase)) ||
            (FilterEquipWeaponCards && IsEquipType(handCard, "Weapon")) ||
            (FilterEquipArmorCards && IsEquipType(handCard, "Armor")) ||
            (FilterEquipPropCards && IsEquipType(handCard, "Prop")) ||
            (FilterJudgeCards && string.Equals(handCard.Meta.CardType, "Judge", StringComparison.OrdinalIgnoreCase));
    }

    private bool MatchesSuitFilter(string suit)
    {
        return (FilterHearts && string.Equals(suit, "Hearts", StringComparison.OrdinalIgnoreCase)) ||
            (FilterDiamonds && string.Equals(suit, "Diamonds", StringComparison.OrdinalIgnoreCase)) ||
            (FilterClubs && string.Equals(suit, "Clubs", StringComparison.OrdinalIgnoreCase)) ||
            (FilterSpades && string.Equals(suit, "Spade", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsEquipType(HandCardInfo handCard, string equipType)
    {
        return string.Equals(handCard.Meta.CardType, "Equip", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(handCard.Meta.EquipType, equipType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComplete(HandCardInfo handCard)
    {
        return HasChineseName(handCard) &&
            !handCard.UsesDefaultCardFace &&
            !string.IsNullOrWhiteSpace(handCard.Meta.Description) &&
            !string.IsNullOrWhiteSpace(handCard.Meta.CardType) &&
            (!string.Equals(handCard.Meta.CardType, "Equip", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(handCard.Meta.EquipType));
    }

    private static bool HasChineseName(HandCardInfo handCard)
    {
        return !string.IsNullOrWhiteSpace(handCard.Name) &&
            !string.Equals(handCard.Name, handCard.Code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsText(string? value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHandCardDisplayName(HandCardInfo handCard)
    {
        return string.IsNullOrWhiteSpace(handCard.Name) ? handCard.Code : handCard.Name;
    }

    private static int GetCardTypeSortOrder(HandCardInfo handCard)
    {
        return handCard.Meta.CardType switch
        {
            "Base" => 0,
            "Zdy" => 1,
            "Equip" when string.Equals(handCard.Meta.EquipType, "Weapon", StringComparison.OrdinalIgnoreCase) => 2,
            "Equip" when string.Equals(handCard.Meta.EquipType, "Armor", StringComparison.OrdinalIgnoreCase) => 3,
            "Equip" when string.Equals(handCard.Meta.EquipType, "Prop", StringComparison.OrdinalIgnoreCase) => 4,
            "Judge" => 5,
            _ => 99
        };
    }

    private static int GetSuitSortOrder(HandCardInfo handCard)
    {
        return handCard.Meta.Suit switch
        {
            "Hearts" => 0,
            "Diamonds" => 1,
            "Clubs" => 2,
            "Spade" => 3,
            _ => 99
        };
    }

    public static string GetSortDisplayName(HandCardSortKey sortKey)
    {
        return sortKey switch
        {
            HandCardSortKey.DisplayName => "中文名",
            HandCardSortKey.Code => "英文代号",
            HandCardSortKey.CardType => "卡牌类型",
            HandCardSortKey.SuitNumber => "花色数字",
            HandCardSortKey.RemainingUseCount => "剩余使用次数",
            _ => "最近修改"
        };
    }

    private void NotifyBasicDeckStateChanged()
    {
        OnPropertyChanged(nameof(FilledSuitDeckSlotCount));
        OnPropertyChanged(nameof(MissingSuitDeckSlotCount));
        OnPropertyChanged(nameof(SuitDeckSummary));
        OnPropertyChanged(nameof(HandCardsNoticeSeverity));
        OnPropertyChanged(nameof(HandCardsNoticeTitle));
        OnPropertyChanged(nameof(HandCardsNoticeMessage));
    }

    private void RebuildSuitDeckSlots(string projectRootPath, IReadOnlyList<HandCardInfo> handCards)
    {
        SuitDeckSlots.Clear();
        SuitDeckGroups.Clear();
        var basicDeckSettings = _handCardWorkspaceService.GetBasicDeckSettings(projectRootPath);
        var handCardMap = handCards.ToDictionary(card => card.Code, StringComparer.OrdinalIgnoreCase);
        for (var deckIndex = 1; deckIndex <= 4; deckIndex++)
        {
            var deckGroup = new SuitDeckSetViewModel(deckIndex);
            foreach (var suit in HandCardDetailViewModel.SuitOptions)
            {
                var suitGroup = new SuitDeckGroupViewModel(deckIndex, suit.Value, suit.DisplayName);
                for (var number = 1; number <= 13; number++)
                {
                    basicDeckSettings.Slots.TryGetValue(HandCardWorkspaceService.BuildBasicDeckSlotKey(deckIndex, suit.Value, number), out var boundCode);
                    if (deckIndex == 1 && string.IsNullOrWhiteSpace(boundCode))
                    {
                        basicDeckSettings.Slots.TryGetValue(HandCardWorkspaceService.BuildLegacyBasicDeckSlotKey(suit.Value, number), out boundCode);
                    }
                    var boundCard = !string.IsNullOrWhiteSpace(boundCode) && handCardMap.TryGetValue(boundCode, out var card)
                        ? card
                        : null;
                    var slot = new SuitDeckSlotViewModel(deckIndex, suit.Value, suit.DisplayName, number, boundCard);
                    SuitDeckSlots.Add(slot);
                    suitGroup.Slots.Add(slot);
                }

                deckGroup.SuitGroups.Add(suitGroup);
            }

            SuitDeckGroups.Add(deckGroup);
        }
    }
}

internal sealed class SuitDeckSetViewModel
{
    public SuitDeckSetViewModel(int deckIndex)
    {
        DeckIndex = deckIndex;
    }

    public int DeckIndex { get; }

    public string DisplayName => DeckIndex switch
    {
        1 => "第一套",
        2 => "第二套",
        3 => "第三套",
        4 => "第四套",
        _ => $"第 {DeckIndex} 套"
    };

    public ObservableCollection<SuitDeckGroupViewModel> SuitGroups { get; } = [];
}

internal enum HandCardSortKey
{
    UpdatedAt,
    DisplayName,
    Code,
    CardType,
    SuitNumber,
    RemainingUseCount
}

internal sealed class SuitDeckGroupViewModel
{
    public SuitDeckGroupViewModel(int deckIndex, string suit, string displayName)
    {
        DeckIndex = deckIndex;
        Suit = suit;
        DisplayName = displayName;
    }

    public int DeckIndex { get; }

    public string Suit { get; }

    public string DisplayName { get; }

    public ObservableCollection<SuitDeckSlotViewModel> Slots { get; } = [];
}

internal sealed class HandCardViewModel : ObservableObject, IExportSelectableCard
{
    private bool _isExportSelectionVisible;
    private bool _isExportSelected;

    private HandCardViewModel(
        bool isAddCard,
        string code,
        string name,
        string cardFacePath,
        bool usesDefaultCardFace,
        string suit,
        int pokerNumber,
        bool useSuitColoredCard)
    {
        IsAddCard = isAddCard;
        Code = code;
        Name = name;
        CardFacePath = cardFacePath;
        UsesDefaultCardFace = usesDefaultCardFace;
        Suit = suit;
        PokerNumber = pokerNumber;
        UseSuitColoredCard = useSuitColoredCard;
    }

    public bool IsAddCard { get; }

    public string Code { get; }

    public string Name { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : Name;

    public string CardFacePath { get; }

    public bool UsesDefaultCardFace { get; }

    public string Suit { get; }

    public int PokerNumber { get; }

    public bool UseSuitColoredCard { get; }

    public bool IsExportSelectionVisible
    {
        get => _isExportSelectionVisible;
        set => SetProperty(ref _isExportSelectionVisible, value);
    }

    public bool IsExportSelected
    {
        get => _isExportSelected;
        set => SetProperty(ref _isExportSelected, value);
    }

    public string PokerNumberLabel => SuitDeckSlotViewModel.FormatNumber(PokerNumber);

    public string SuitSymbol => Suit switch
    {
        "Hearts" => "♥",
        "Diamonds" => "♦",
        "Clubs" => "♣",
        "Spade" => "♠",
        _ => string.Empty
    };

    public string AutomationName => IsAddCard
        ? "新建手牌"
        : $"手牌：{DisplayName}，{Code}";

    public static HandCardViewModel CreateAddCard(string defaultCardFacePath)
    {
        return new HandCardViewModel(true, string.Empty, string.Empty, defaultCardFacePath, true, string.Empty, 1, false);
    }

    public static HandCardViewModel FromHandCard(HandCardInfo handCard, string defaultCardFacePath, bool useSuitColoredCard)
    {
        var cardFacePath = File.Exists(handCard.CardFacePath)
            ? handCard.CardFacePath
            : defaultCardFacePath;
        return new HandCardViewModel(
            false,
            handCard.Code,
            handCard.Name,
            cardFacePath,
            handCard.UsesDefaultCardFace || !File.Exists(handCard.CardFacePath),
            handCard.Meta.Suit,
            handCard.Meta.PokerNumber,
            useSuitColoredCard);
    }
}

internal sealed class SuitDeckSlotViewModel
{
    private readonly HandCardInfo? _card;

    public SuitDeckSlotViewModel(int deckIndex, string suit, string suitDisplayName, int number, HandCardInfo? card)
    {
        DeckIndex = deckIndex;
        Suit = suit;
        SuitDisplayName = suitDisplayName;
        Number = number;
        _card = card;
    }

    public int DeckIndex { get; }

    public string DeckDisplayName => DeckIndex switch
    {
        1 => "第一套",
        2 => "第二套",
        3 => "第三套",
        4 => "第四套",
        _ => $"第 {DeckIndex} 套"
    };

    public string Suit { get; }

    public string SuitDisplayName { get; }

    public int Number { get; }

    public bool IsFilled => _card is not null;

    public string CardCode => _card?.Code ?? string.Empty;

    public string DisplayTitle => $"{DeckDisplayName} {FormatSuitNumber(Suit, Number)}";

    public string NumberLabel => FormatNumber(Number);

    public string DisplaySubtitle => IsFilled
        ? _card!.Name
        : "未填入";

    public string BindingTitle => IsFilled
        ? _card!.Name
        : "尚未设置手牌";

    public string BindingSubtitle => IsFilled
        ? $"英文代号：{CardCode}"
        : $"新建时预填：{DisplayTitle}";

    public string SlotActionText => IsFilled
        ? "打开详情"
        : "填入此槽位";

    public string StatusText => IsFilled
        ? "已设置"
        : "未设置";

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

    public static string FormatNumber(int number)
    {
        return number switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => number.ToString(CultureInfo.InvariantCulture)
        };
    }
}
