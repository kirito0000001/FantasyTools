using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace FantasyTools.Services;

internal sealed class WinUiDialogService
{
    private readonly Func<XamlRoot> _getXamlRoot;

    public WinUiDialogService(Func<XamlRoot> getXamlRoot)
    {
        _getXamlRoot = getXamlRoot;
    }

    public async Task<DialogResultKind> ShowContentAsync(
        ContentDialogRequest request,
        CancellationToken cancellationToken = default)
    {
        var dialog = new ContentDialog
        {
            Title = request.Title,
            Content = request.Content,
            PrimaryButtonText = request.PrimaryButtonText,
            SecondaryButtonText = request.SecondaryButtonText ?? string.Empty,
            CloseButtonText = request.CloseButtonText,
            DefaultButton = request.DefaultButton,
            PrimaryButtonStyle = request.PrimaryButtonStyle,
            XamlRoot = _getXamlRoot()
        };

        request.ConfigureDialog?.Invoke(dialog);
        AttachCancelShortcuts(dialog);
        AttachOpenAnimation(dialog);
        return MapResult(await ShowDialogAsync(dialog, cancellationToken));
    }

    private static void AttachOpenAnimation(ContentDialog dialog)
    {
        dialog.Opacity = 0;
        dialog.RenderTransformOrigin = new Point(0.5, 0.5);
        dialog.RenderTransform = new ScaleTransform
        {
            ScaleX = 0.96,
            ScaleY = 0.96
        };

        dialog.Opened += (_, _) =>
        {
            var storyboard = new Storyboard();
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            AddAnimation(storyboard, dialog, "Opacity", 0, 1, 160, easing);
            AddAnimation(storyboard, dialog, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)", 0.96, 1, 180, easing);
            AddAnimation(storyboard, dialog, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)", 0.96, 1, 180, easing);
            storyboard.Begin();
        };
    }

    private static void AddAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string propertyPath,
        double from,
        double to,
        double milliseconds,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = easing
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        storyboard.Children.Add(animation);
    }

    private static void AttachCancelShortcuts(ContentDialog dialog)
    {
        dialog.RightTapped += (_, args) =>
        {
            dialog.Hide();
            args.Handled = true;
        };
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Escape)
            {
                dialog.Hide();
                args.Handled = true;
            }
        };
    }

    private static async Task<ContentDialogResult> ShowDialogAsync(
        ContentDialog dialog,
        CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(dialog.Hide);
        return await dialog.ShowAsync();
    }

    private static DialogResultKind MapResult(ContentDialogResult result)
    {
        return result switch
        {
            ContentDialogResult.Primary => DialogResultKind.Primary,
            ContentDialogResult.Secondary => DialogResultKind.Secondary,
            ContentDialogResult.None => DialogResultKind.None,
            _ => DialogResultKind.Cancel
        };
    }
}
