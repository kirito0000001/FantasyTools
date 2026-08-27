using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FantasyTools.Views;

internal sealed class OutlinedTextBlock : Grid
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(OutlinedTextBlock), new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(OutlinedTextBlock), new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(OutlinedTextBlock), new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(OutlinedTextBlock), new PropertyMetadata(15d, OnVisualPropertyChanged));

    private readonly TextBlock[] _outlineTextBlocks;
    private readonly TextBlock _foregroundTextBlock;

    public OutlinedTextBlock()
    {
        IsHitTestVisible = false;
        _outlineTextBlocks =
        [
            CreateTextBlock(-1.4, 0),
            CreateTextBlock(1.4, 0),
            CreateTextBlock(0, -1.4),
            CreateTextBlock(0, 1.4),
            CreateTextBlock(-1, -1),
            CreateTextBlock(1, -1),
            CreateTextBlock(-1, 1),
            CreateTextBlock(1, 1)
        ];
        foreach (var textBlock in _outlineTextBlocks)
        {
            Children.Add(textBlock);
        }

        _foregroundTextBlock = CreateTextBlock(0, 0);
        Children.Add(_foregroundTextBlock);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    private static void OnVisualPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is OutlinedTextBlock outlinedTextBlock)
        {
            outlinedTextBlock.ApplyVisualState();
        }
    }

    private static TextBlock CreateTextBlock(double x, double y)
    {
        return new TextBlock
        {
            RenderTransform = new TranslateTransform { X = x, Y = y },
            IsHitTestVisible = false
        };
    }

    private void ApplyVisualState()
    {
        foreach (var textBlock in _outlineTextBlocks)
        {
            ApplyTextState(textBlock, Stroke);
        }

        ApplyTextState(_foregroundTextBlock, Foreground);
    }

    private void ApplyTextState(TextBlock textBlock, Brush? brush)
    {
        textBlock.Text = Text;
        textBlock.FontSize = FontSize;
        textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        if (brush is not null)
        {
            textBlock.Foreground = brush;
        }
    }
}
