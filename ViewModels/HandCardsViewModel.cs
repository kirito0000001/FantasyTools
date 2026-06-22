using System.Collections.ObjectModel;
using System.IO;
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

    public void Load(string projectRootPath)
    {
        Cards.Clear();
        Cards.Add(HandCardViewModel.CreateAddCard(_defaultCardFacePath));

        foreach (var handCard in _handCardWorkspaceService.GetHandCards(projectRootPath))
        {
            Cards.Add(HandCardViewModel.FromHandCard(handCard, _defaultCardFacePath));
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
