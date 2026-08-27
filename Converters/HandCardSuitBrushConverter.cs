using System;
using FantasyTools.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FantasyTools.Converters;

public sealed class HandCardSuitBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is HandCardViewModel handCard)
        {
            if (handCard.IsAddCard)
            {
                return GetBrush(IsTextRequest(parameter) ? "TextFillColorPrimaryBrush" : "HandCardAddBackgroundBrush");
            }

            return GetBrush(GetResourceKey(handCard.Suit, handCard.UseSuitColoredCard, parameter));
        }

        if (value is HandCardDetailViewModel handCardDetail)
        {
            return GetBrush(GetResourceKey(handCardDetail.Suit, true, parameter));
        }

        if (value is not string suit || string.IsNullOrWhiteSpace(suit))
        {
            return GetBrush(IsTextRequest(parameter) ? "TextFillColorPrimaryBrush" : "LayerFillColorDefaultBrush");
        }

        return GetBrush(GetResourceKey(suit, true, parameter));
    }

    private static string GetResourceKey(string suit, bool useSuitColoredCard, object parameter)
    {
        var role = parameter as string ?? "Background";
        return role switch
        {
            "Text" => GetTextResourceKey(suit),
            _ => useSuitColoredCard
                ? GetBackgroundResourceKey(suit)
                : "LayerFillColorDefaultBrush"
        };
    }

    private static bool IsDarkTheme()
    {
        if (Application.Current is null)
        {
            return false;
        }

        return App.CurrentActualTheme == ElementTheme.Dark;
    }

    private static bool IsTextRequest(object parameter)
    {
        return string.Equals(parameter as string, "Text", StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static string GetBackgroundResourceKey(string suit)
    {
        return suit switch
        {
            "Hearts" => "HandCardHeartsBackgroundBrush",
            "Diamonds" => "HandCardDiamondsBackgroundBrush",
            "Clubs" => "HandCardClubsBackgroundBrush",
            "Spade" => "HandCardSpadesBackgroundBrush",
            _ => "LayerFillColorDefaultBrush"
        };
    }

    private static string GetTextResourceKey(string suit)
    {
        return suit switch
        {
            "Hearts" => "HandCardHeartsTextBrush",
            "Diamonds" => "HandCardDiamondsTextBrush",
            "Clubs" => "HandCardClubsTextBrush",
            "Spade" => "HandCardSpadesTextBrush",
            _ => "TextFillColorPrimaryBrush"
        };
    }

    private static Brush GetBrush(string resourceKey)
    {
        var themedBrush = GetExplicitSuitBrush(resourceKey, IsDarkTheme());
        if (themedBrush is not null)
        {
            return themedBrush;
        }

        return Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(Colors.Transparent);
    }

    private static Brush? GetExplicitSuitBrush(string resourceKey, bool isDarkTheme)
    {
        var color = resourceKey switch
        {
            "HandCardAddBackgroundBrush" => isDarkTheme ? "#3A3838" : "#F1F1F1",
            "HandCardSuitTextStrokeBrush" => isDarkTheme ? "#1A1A1A" : "#FFFFFF",
            "HandCardHeartsBackgroundBrush" => isDarkTheme ? "#4B1821" : "#FFE0E5",
            "HandCardDiamondsBackgroundBrush" => isDarkTheme ? "#4A2E11" : "#FFE6C2",
            "HandCardClubsBackgroundBrush" => isDarkTheme ? "#113F3B" : "#D3F4EF",
            "HandCardSpadesBackgroundBrush" => isDarkTheme ? "#143762" : "#D8E9FF",
            "HandCardHeartsTextBrush" => isDarkTheme ? "#FF5E78" : "#B20F2A",
            "HandCardDiamondsTextBrush" => isDarkTheme ? "#FF9D3D" : "#B85A00",
            "HandCardClubsTextBrush" => isDarkTheme ? "#27CABD" : "#006F67",
            "HandCardSpadesTextBrush" => isDarkTheme ? "#66AAFF" : "#124CA6",
            _ => null
        };

        return color is null
            ? null
            : new SolidColorBrush(ColorHelper.FromArgb(
                255,
                System.Convert.ToByte(color.Substring(1, 2), 16),
                System.Convert.ToByte(color.Substring(3, 2), 16),
                System.Convert.ToByte(color.Substring(5, 2), 16)));
    }
}
