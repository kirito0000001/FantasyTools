using System.IO;
using FantasyTools.Models;

namespace FantasyTools.ViewModels;

internal sealed class BasicDeckHandCardPickerItem : ObservableObject
{
    public BasicDeckHandCardPickerItem(HandCardInfo handCard, string defaultCardFacePath, bool isSelected)
    {
        Code = handCard.Code;
        DisplayName = string.IsNullOrWhiteSpace(handCard.Name) ? handCard.Code : handCard.Name;
        CardFacePath = File.Exists(handCard.CardFacePath) ? handCard.CardFacePath : defaultCardFacePath;
        IsSelected = isSelected;
    }

    public string Code { get; }

    public string DisplayName { get; }

    public string CardFacePath { get; }

    public bool IsSelected { get; }
}
