using System;
using FantasyTools.Models;
using FantasyTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DrawingRectangle = System.Drawing.Rectangle;

namespace FantasyTools.Views;

internal static class CharacterDialogContentFactory
{
    public static CharacterCreateDialogContent CreateCharacterCreateContent(
        string projectRootPath,
        string defaultCardFacePath,
        CharacterWorkspaceService characterWorkspaceService,
        Func<Action<string, DrawingRectangle?>, RoutedEventHandler> createCardFacePickHandler)
    {
        return new CharacterCreateDialogContent(
            projectRootPath,
            defaultCardFacePath,
            characterWorkspaceService,
            createCardFacePickHandler);
    }
}

internal sealed class CharacterCreateDialogContent
{
    private readonly string _projectRootPath;
    private readonly CharacterWorkspaceService _characterWorkspaceService;
    private readonly TextBox _codeBox;
    private readonly Image _cardFacePreview;
    private readonly InfoBar _validationInfoBar;
    private readonly TextBlock _folderPreviewText;
    private string _cardFaceSourcePath;
    private DrawingRectangle? _cardFaceCrop;

    public CharacterCreateDialogContent(
        string projectRootPath,
        string defaultCardFacePath,
        CharacterWorkspaceService characterWorkspaceService,
        Func<Action<string, DrawingRectangle?>, RoutedEventHandler> createCardFacePickHandler)
    {
        _projectRootPath = projectRootPath;
        _characterWorkspaceService = characterWorkspaceService;
        _cardFaceSourcePath = defaultCardFacePath;

        _codeBox = new TextBox
        {
            Header = "角色英文代号",
            PlaceholderText = "例如：LiuBei / CaoCao / SunQuan",
            Width = 340
        };

        _cardFacePreview = new Image
        {
            Width = 128,
            Height = 180,
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
            Width = 360
        };
        Content.Children.Add(CreateCardFaceRow(createCardFacePickHandler(UpdateCardFacePath)));
        Content.Children.Add(_codeBox);
        Content.Children.Add(_folderPreviewText);
        Content.Children.Add(_validationInfoBar);

        _codeBox.TextChanged += (_, _) => UpdatePreview();
        UpdateCardFacePath(_cardFaceSourcePath, null);
        UpdatePreview();
    }

    public StackPanel Content { get; }

    public CharacterCreateInput ReadInput()
    {
        return new CharacterCreateInput(
            CharacterWorkspaceService.SanitizeCharacterCode(_codeBox.Text),
            _cardFaceSourcePath,
            _cardFaceCrop);
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
            message = "角色英文代号不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(input.CardFaceSourcePath) || !System.IO.File.Exists(input.CardFaceSourcePath))
        {
            message = "卡面图片不存在，请重新设置卡面。";
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
            Width = 128,
            Height = 180,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Content = new Border
            {
                Width = 128,
                Height = 180,
                CornerRadius = new CornerRadius(6),
                Child = _cardFacePreview
            }
        };
        ToolTipService.SetToolTip(previewButton, "点击设置卡面");
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
        _folderPreviewText.Text = $"创建后文件夹预览：{_characterWorkspaceService.BuildCharacterFolderPreview(_projectRootPath, _codeBox.Text)}";
    }
}
