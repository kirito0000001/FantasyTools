using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FantasyTools.Services;

internal enum DialogResultKind
{
    None,
    Primary,
    Secondary,
    Cancel
}

internal sealed record ContentDialogRequest(
    string Title,
    UIElement Content,
    string PrimaryButtonText = "确定",
    string CloseButtonText = "取消",
    string? SecondaryButtonText = null,
    ContentDialogButton DefaultButton = ContentDialogButton.Primary,
    Style? PrimaryButtonStyle = null,
    Action<ContentDialog>? ConfigureDialog = null);
