using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using FantasyTools.Models;
using FantasyTools.Services;

namespace FantasyTools.ViewModels;

internal sealed class CharactersViewModel : ObservableObject
{
    private readonly CharacterWorkspaceService _characterWorkspaceService;
    private readonly string _defaultCardFacePath;
    private IReadOnlyList<CharacterInfo> _allCharacters = [];
    private string _searchText = string.Empty;
    private CharacterSortKey _sortKey = CharacterSortKey.UpdatedAt;
    private bool _sortDescending = true;

    public CharactersViewModel(CharacterWorkspaceService characterWorkspaceService, string defaultCardFacePath)
    {
        _characterWorkspaceService = characterWorkspaceService;
        _defaultCardFacePath = defaultCardFacePath;
        Cards.Add(CharacterCardViewModel.CreateAddCard(_defaultCardFacePath));
    }

    public ObservableCollection<CharacterCardViewModel> Cards { get; } = [];

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

    public bool FilterMissingName { get; private set; }

    public bool FilterIncomplete { get; private set; }

    public bool FilterMultiPhase { get; private set; }

    public bool FilterMissingSkillGroups { get; private set; }

    public CharacterSortKey SortKey => _sortKey;

    public bool SortDescending => _sortDescending;

    public string SortSummary => $"排序：{GetSortDisplayName(SortKey)} / {(SortDescending ? "降序" : "升序")}";

    public bool HasActiveFilters =>
        !string.IsNullOrWhiteSpace(SearchText) ||
        FilterMissingName ||
        FilterIncomplete ||
        FilterMultiPhase ||
        FilterMissingSkillGroups;

    public string FilterSummary => HasActiveFilters
        ? $"总角色数：{_allCharacters.Count} / 筛选后：{Cards.Count(card => !card.IsAddCard)}"
        : $"总角色数：{_allCharacters.Count} / 筛选后：{_allCharacters.Count}";

    public void Load(string projectRootPath)
    {
        _allCharacters = _characterWorkspaceService.GetCharacters(projectRootPath);
        RebuildCards();
    }

    public void SetFilters(
        bool missingName,
        bool incomplete,
        bool multiPhase,
        bool missingSkillGroups)
    {
        FilterMissingName = missingName;
        FilterIncomplete = incomplete;
        FilterMultiPhase = multiPhase;
        FilterMissingSkillGroups = missingSkillGroups;
        RebuildCards();
    }

    public void ClearFilters()
    {
        SetFilters(false, false, false, false);
    }

    public void SetSort(CharacterSortKey sortKey, bool descending)
    {
        _sortKey = sortKey;
        _sortDescending = descending;
        RebuildCards();
    }

    private void RebuildCards()
    {
        Cards.Clear();
        Cards.Add(CharacterCardViewModel.CreateAddCard(_defaultCardFacePath));

        foreach (var character in ApplySort(_allCharacters.Where(MatchesFilters)))
        {
            Cards.Add(CharacterCardViewModel.FromCharacter(character, _defaultCardFacePath));
        }

        OnPropertyChanged(nameof(FilterMissingName));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(FilterIncomplete));
        OnPropertyChanged(nameof(FilterMultiPhase));
        OnPropertyChanged(nameof(FilterMissingSkillGroups));
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(SortKey));
        OnPropertyChanged(nameof(SortDescending));
        OnPropertyChanged(nameof(SortSummary));
    }

    private bool MatchesFilters(CharacterInfo character)
    {
        if (!MatchesSearch(character))
        {
            return false;
        }

        if (FilterMissingName && HasChineseName(character))
        {
            return false;
        }

        if (FilterIncomplete && IsComplete(character))
        {
            return false;
        }

        if (FilterMultiPhase && ParsePhaseCount(character.Meta.Phase) <= 1)
        {
            return false;
        }

        if (FilterMissingSkillGroups && character.Meta.SkillGroups.Any(group => !string.IsNullOrWhiteSpace(group)))
        {
            return false;
        }

        return true;
    }

    private bool MatchesSearch(CharacterInfo character)
    {
        var keyword = SearchText.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return ContainsText(character.Code, keyword) ||
            ContainsText(character.Name, keyword) ||
            character.Meta.Tags.Any(tag => ContainsText(tag, keyword)) ||
            character.Meta.Skills.Any(skill => ContainsText(skill.Name, keyword));
    }

    private IEnumerable<CharacterInfo> ApplySort(IEnumerable<CharacterInfo> characters)
    {
        return SortKey switch
        {
            CharacterSortKey.DisplayName => SortDescending
                ? characters.OrderByDescending(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(character => character.Code, StringComparer.OrdinalIgnoreCase)
                : characters.OrderBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(character => character.Code, StringComparer.OrdinalIgnoreCase),
            CharacterSortKey.Code => SortDescending
                ? characters.OrderByDescending(character => character.Code, StringComparer.OrdinalIgnoreCase)
                : characters.OrderBy(character => character.Code, StringComparer.OrdinalIgnoreCase),
            CharacterSortKey.PhaseCount => SortDescending
                ? characters.OrderByDescending(character => ParsePhaseCount(character.Meta.Phase)).ThenBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase)
                : characters.OrderBy(character => ParsePhaseCount(character.Meta.Phase)).ThenBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase),
            CharacterSortKey.Completion => SortDescending
                ? characters.OrderByDescending(GetCompletionScore).ThenBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase)
                : characters.OrderBy(GetCompletionScore).ThenBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase),
            _ => SortDescending
                ? characters.OrderByDescending(character => character.Meta.UpdatedAt).ThenBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase)
                : characters.OrderBy(character => character.Meta.UpdatedAt).ThenBy(GetCharacterDisplayName, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static int GetCompletionScore(CharacterInfo character)
    {
        var score = 0;
        if (HasChineseName(character)) score++;
        if (!string.IsNullOrWhiteSpace(character.Meta.Description)) score++;
        if (ParsePhaseCount(character.Meta.Phase) > 0) score++;
        if (character.Meta.SkillGroups.Any(group => !string.IsNullOrWhiteSpace(group))) score++;
        if (character.Meta.Skills.Any(skill => !string.IsNullOrWhiteSpace(skill.Name))) score++;
        return score;
    }

    private static bool IsComplete(CharacterInfo character)
    {
        return HasChineseName(character) &&
            !string.IsNullOrWhiteSpace(character.Meta.Description) &&
            ParsePhaseCount(character.Meta.Phase) > 0 &&
            character.Meta.SkillGroups.Any(group => !string.IsNullOrWhiteSpace(group)) &&
            character.Meta.Skills.Any(skill =>
                !string.IsNullOrWhiteSpace(skill.Name) &&
                !string.IsNullOrWhiteSpace(skill.Description) &&
                !string.IsNullOrWhiteSpace(skill.Function) &&
                !string.IsNullOrWhiteSpace(skill.Type));
    }

    private static int ParsePhaseCount(string phase)
    {
        if (int.TryParse(phase, out var count))
        {
            return count;
        }

        return 0;
    }

    private static bool HasChineseName(CharacterInfo character)
    {
        return !string.IsNullOrWhiteSpace(character.Name) &&
            !string.Equals(character.Name, character.Code, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsText(string? value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Contains(keyword, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCharacterDisplayName(CharacterInfo character)
    {
        return string.IsNullOrWhiteSpace(character.Name) ? character.Code : character.Name;
    }

    public static string GetSortDisplayName(CharacterSortKey sortKey)
    {
        return sortKey switch
        {
            CharacterSortKey.DisplayName => "中文名",
            CharacterSortKey.Code => "英文代号",
            CharacterSortKey.PhaseCount => "Stage 数量",
            CharacterSortKey.Completion => "完成度",
            _ => "最近修改"
        };
    }
}

internal enum CharacterSortKey
{
    UpdatedAt,
    DisplayName,
    Code,
    PhaseCount,
    Completion
}

internal sealed class CharacterCardViewModel : ObservableObject, IExportSelectableCard
{
    private bool _isExportSelectionVisible;
    private bool _isExportSelected;

    private CharacterCardViewModel(
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

    public string AutomationName => IsAddCard
        ? "新建角色"
        : $"角色：{DisplayName}，{Code}";

    public static CharacterCardViewModel CreateAddCard(string defaultCardFacePath)
    {
        return new CharacterCardViewModel(true, string.Empty, string.Empty, defaultCardFacePath, true);
    }

    public static CharacterCardViewModel FromCharacter(CharacterInfo character, string defaultCardFacePath)
    {
        var cardFacePath = File.Exists(character.CardFacePath)
            ? character.CardFacePath
            : defaultCardFacePath;
        return new CharacterCardViewModel(
            false,
            character.Code,
            character.Name,
            cardFacePath,
            character.UsesDefaultCardFace || !File.Exists(character.CardFacePath));
    }
}
