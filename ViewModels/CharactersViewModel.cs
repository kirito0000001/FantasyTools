using System.Collections.ObjectModel;
using System.IO;
using FantasyTools.Models;
using FantasyTools.Services;

namespace FantasyTools.ViewModels;

internal sealed class CharactersViewModel : ObservableObject
{
    private readonly CharacterWorkspaceService _characterWorkspaceService;
    private readonly string _defaultCardFacePath;

    public CharactersViewModel(CharacterWorkspaceService characterWorkspaceService, string defaultCardFacePath)
    {
        _characterWorkspaceService = characterWorkspaceService;
        _defaultCardFacePath = defaultCardFacePath;
        Cards.Add(CharacterCardViewModel.CreateAddCard(_defaultCardFacePath));
    }

    public ObservableCollection<CharacterCardViewModel> Cards { get; } = [];

    public void Load(string projectRootPath)
    {
        Cards.Clear();
        Cards.Add(CharacterCardViewModel.CreateAddCard(_defaultCardFacePath));

        foreach (var character in _characterWorkspaceService.GetCharacters(projectRootPath))
        {
            Cards.Add(CharacterCardViewModel.FromCharacter(character, _defaultCardFacePath));
        }
    }
}

internal sealed class CharacterCardViewModel
{
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
