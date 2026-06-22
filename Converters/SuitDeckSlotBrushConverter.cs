using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FantasyTools.Converters;

public sealed class SuitDeckSlotBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isFilled = value is true;
        var role = parameter as string ?? string.Empty;
        var resourceKey = role switch
        {
            "Background" => isFilled ? "SuitDeckFilledBackgroundBrush" : "SuitDeckMissingBackgroundBrush",
            "Border" => isFilled ? "SuitDeckFilledBorderBrush" : "SuitDeckMissingBorderBrush",
            "Accent" => isFilled ? "SuitDeckFilledAccentBrush" : "SuitDeckMissingAccentBrush",
            "AccentText" => "TextOnAccentFillColorPrimaryBrush",
            "BadgeBackground" => isFilled ? "SuitDeckFilledBackgroundBrush" : "SuitDeckMissingBackgroundBrush",
            "BadgeBorder" => isFilled ? "SuitDeckFilledBorderBrush" : "SuitDeckMissingBorderBrush",
            "BadgeText" => isFilled ? "SuitDeckFilledTextBrush" : "SuitDeckMissingTextBrush",
            _ => "CardStrokeColorDefaultBrush"
        };

        return Application.Current.Resources.TryGetValue(resourceKey, out var resource) && resource is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
