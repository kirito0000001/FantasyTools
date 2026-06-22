using System;
using FantasyTools.Models;
using FantasyTools.Services;
using FantasyTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;

namespace FantasyTools.Views;

internal static class HandCardDialogContentFactory
{
    public static HandCardCreateDialogContent CreateHandCardCreateContent(
        string projectRootPath,
        string defaultCardFacePath,
        HandCardWorkspaceService handCardWorkspaceService,
        Func<Action<string, DrawingRectangle?>, RoutedEventHandler> createCardFacePickHandler,
        string defaultSuit = "Hearts",
        int defaultPokerNumber = 1)
    {
        return new HandCardCreateDialogContent(
            projectRootPath,
            defaultCardFacePath,
            handCardWorkspaceService,
            createCardFacePickHandler,
            defaultSuit,
            defaultPokerNumber);
    }
}

internal sealed class HandCardCreateDialogContent
{
    private readonly string _projectRootPath;
    private readonly HandCardWorkspaceService _handCardWorkspaceService;
    private readonly TextBox _codeBox;
    private readonly TextBlock _slotPreviewText;
    private readonly Image _cardFacePreview;
    private readonly InfoBar _validationInfoBar;
    private readonly TextBlock _folderPreviewText;
    private string _cardFaceSourcePath;
    private DrawingRectangle? _cardFaceCrop;
    private readonly string _defaultSuit;
    private readonly int _defaultPokerNumber;

    public HandCardCreateDialogContent(
        string projectRootPath,
        string defaultCardFacePath,
        HandCardWorkspaceService handCardWorkspaceService,
        Func<Action<string, DrawingRectangle?>, RoutedEventHandler> createCardFacePickHandler,
        string defaultSuit,
        int defaultPokerNumber)
    {
        _projectRootPath = projectRootPath;
        _handCardWorkspaceService = handCardWorkspaceService;
        _cardFaceSourcePath = defaultCardFacePath;
        _defaultSuit = string.IsNullOrWhiteSpace(defaultSuit) ? "Hearts" : defaultSuit;
        _defaultPokerNumber = Math.Clamp(defaultPokerNumber, 1, 13);

        _codeBox = new TextBox
        {
            Header = "手牌英文代号",
            PlaceholderText = "例如：Sha / Shan / QingGangSword",
            Width = 340
        };

        _slotPreviewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        _cardFacePreview = new Image
        {
            Width = 180,
            Height = 151,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
        };
        _validationInfoBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = "请检查输入"
        };

        _folderPreviewText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap
        };

        Content = new StackPanel
        {
            Spacing = 12,
            Width = 380
        };
        Content.Children.Add(CreateCardFaceRow(createCardFacePickHandler(UpdateCardFacePath)));
        Content.Children.Add(_slotPreviewText);
        Content.Children.Add(_codeBox);
        Content.Children.Add(_folderPreviewText);
        Content.Children.Add(_validationInfoBar);

        _codeBox.TextChanged += (_, _) => UpdatePreview();
        UpdateCardFacePath(_cardFaceSourcePath, null);
        UpdatePreview();
    }

    public StackPanel Content { get; }

    public HandCardCreateInput ReadInput()
    {
        return new HandCardCreateInput(
            HandCardWorkspaceService.SanitizeHandCardCode(_codeBox.Text),
            _cardFaceSourcePath,
            _cardFaceCrop,
            SuitDeckSlotViewModel.BuildDefaultCardName(_defaultSuit, _defaultPokerNumber),
            _defaultSuit,
            _defaultPokerNumber);
    }

    public void FocusFirstInput()
    {
        _codeBox.Focus(FocusState.Programmatic);
    }

    public bool HasValidInput(out string message)
    {
        var input = ReadInput();
        if (string.IsNullOrWhiteSpace(input.Code))
        {
            message = "手牌英文代号不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.CardFaceSourcePath) || !System.IO.File.Exists(input.CardFaceSourcePath))
        {
            message = "手牌卡面图片不存在，请重新设置卡面。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public void ShowValidationMessage(string message)
    {
        _validationInfoBar.Message = message;
        _validationInfoBar.IsOpen = true;
    }

    private Grid CreateCardFaceRow(RoutedEventHandler pickCardFaceHandler)
    {
        var grid = new Grid
        {
            RowSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            Text = "卡面",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var cardFaceTip = new TextBlock
        {
            Text = "点击图片进行设置",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(cardFaceTip, 1);
        grid.Children.Add(cardFaceTip);

        var previewButton = new Button
        {
            Width = 180,
            Height = 151,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new Border
            {
                Width = 180,
                Height = 151,
                CornerRadius = new CornerRadius(6),
                Child = _cardFacePreview
            }
        };
        ToolTipService.SetToolTip(previewButton, "点击设置手牌卡面");
        previewButton.Click += pickCardFaceHandler;
        Grid.SetRow(previewButton, 2);
        grid.Children.Add(previewButton);

        return grid;
    }

    private void UpdateCardFacePath(string path, DrawingRectangle? crop)
    {
        _cardFaceSourcePath = path;
        _cardFaceCrop = crop;
        try
        {
            _cardFacePreview.Source = new BitmapImage(new Uri(path));
        }
        catch
        {
            _cardFacePreview.Source = null;
        }
    }

    private void UpdatePreview()
    {
        _slotPreviewText.Text = $"基础卡堆槽位：{SuitDeckSlotViewModel.FormatSuitNumber(_defaultSuit, _defaultPokerNumber)}";
        _folderPreviewText.Text = $"创建后文件夹预览：{_handCardWorkspaceService.BuildHandCardFolderPreview(_projectRootPath, _codeBox.Text)}";
    }
}
