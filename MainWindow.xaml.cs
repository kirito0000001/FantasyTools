using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FantasyTools.Models;
using FantasyTools.Services;
using FantasyTools.ViewModels;
using FantasyTools.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace FantasyTools
{
    public sealed partial class MainWindow : Window
    {
        private readonly ApplicationViewModel _viewModel;
        private readonly WinUiDialogService _dialogService;
        private readonly CharacterWorkspaceService _characterWorkspaceService;
        private readonly HandCardWorkspaceService _handCardWorkspaceService;
        private readonly DispatcherQueueTimer _globalProgressElapsedTimer;
        private readonly DispatcherQueueTimer _characterDetailSaveTimer;
        private readonly DispatcherQueueTimer _handCardDetailSaveTimer;
        private bool _returnToBasicDeckAfterHandCardDetail;

        public MainWindow()
        {
            InitializeComponent();
            _globalProgressElapsedTimer = DispatcherQueue.CreateTimer();
            _globalProgressElapsedTimer.Interval = TimeSpan.FromSeconds(1);
            _globalProgressElapsedTimer.Tick += GlobalProgressElapsedTimer_Tick;
            _characterDetailSaveTimer = DispatcherQueue.CreateTimer();
            _characterDetailSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _characterDetailSaveTimer.Tick += CharacterDetailSaveTimer_Tick;
            _handCardDetailSaveTimer = DispatcherQueue.CreateTimer();
            _handCardDetailSaveTimer.Interval = TimeSpan.FromMilliseconds(800);
            _handCardDetailSaveTimer.Tick += HandCardDetailSaveTimer_Tick;
            var defaultCardFacePath = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultCardFace.png");
            _characterWorkspaceService = new CharacterWorkspaceService();
            _handCardWorkspaceService = new HandCardWorkspaceService();
            var settings = new SettingsViewModel(new AppSettingsService(), new LogService(), new ProjectRootMigrationService());
            _viewModel = new ApplicationViewModel(
                settings,
                new GlobalProgressViewModel(),
                new CharactersViewModel(_characterWorkspaceService, defaultCardFacePath),
                new CharacterDetailViewModel(),
                new HandCardsViewModel(_handCardWorkspaceService, defaultCardFacePath),
                new HandCardDetailViewModel());
            _dialogService = new WinUiDialogService(() => RootGrid.XamlRoot);
            RootGrid.DataContext = _viewModel;
            RegisterHelpKeyboardAccelerators();
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            settings.LoadAndEnsureProjectRoot();
            _viewModel.Characters.Load(settings.ProjectRootPath);
            _viewModel.HandCards.Load(settings.ProjectRootPath);
            SyncThemePreferenceComboBox();
            ApplyTheme();
            ShowPage(ToolboxModuleKey.Characters);
        }

        private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItem is not NavigationViewItem item ||
                item.Tag is not string tag ||
                _viewModel.FindModuleByTag(tag) is not { } module)
            {
                return;
            }

            _viewModel.SelectedModule = module.Key;
            ShowPage(module.Key);
        }

        private async void ChooseProjectRootButton_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var projectRootPath = _viewModel.Settings.BuildProjectRootPathFromParent(folder.Path);
            if (_viewModel.Settings.IsCurrentProjectRoot(projectRootPath))
            {
                _viewModel.Settings.SetProjectRootStatus(
                    InfoBarSeverity.Informational,
                    "目录未变化",
                    $"当前整体项目目录已经是：{projectRootPath}");
                return;
            }

            if (_viewModel.Settings.IsCandidateInsideCurrentRoot(projectRootPath))
            {
                _viewModel.Settings.SetProjectRootStatus(
                    InfoBarSeverity.Error,
                    "目录不可用",
                    "新整体项目目录不能放在旧整体项目目录内部，否则删除旧目录时会连同新目录一起删除。");
                _viewModel.Settings.AppendLog(LogVerbosity.Error, $"整体项目目录迁移被阻止，新目录位于旧目录内部：{projectRootPath}");
                return;
            }

            if (_viewModel.Settings.IsCurrentProjectRootInsideCandidate(projectRootPath))
            {
                _viewModel.Settings.SetProjectRootStatus(
                    InfoBarSeverity.Error,
                    "目录不可用",
                    "新整体项目目录不能包含旧整体项目目录，请选择独立的父目录。");
                _viewModel.Settings.AppendLog(LogVerbosity.Error, $"整体项目目录迁移被阻止，新目录包含旧目录：{projectRootPath}");
                return;
            }

            var oldProjectRootPath = _viewModel.Settings.ProjectRootPath;
            ShowGlobalProgress("迁移整体项目目录", $"{oldProjectRootPath} -> {projectRootPath}");
            UpdateGlobalProgress("正在准备迁移目录...", 1, $"{oldProjectRootPath} -> {projectRootPath}", true);

            try
            {
                var progress = new Progress<ProgressUpdate>(update =>
                {
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate);
                });
                var result = await _viewModel.Settings.ChangeProjectRootAsync(
                    projectRootPath,
                    progress,
                    GetGlobalProgressCancellationToken());

                var cleanupMessage = result.OldDirectoryDeleted
                    ? "旧目录已删除。"
                    : $"新目录已可用，但旧目录清理失败：{result.CleanupError}";
                CompleteGlobalProgress(
                    result.OldDirectoryDeleted ? "整体项目目录迁移完成" : "整体项目目录迁移完成，旧目录待清理",
                    $"已迁移 {result.FileCount} 个文件、{result.DirectoryCount} 个文件夹；{cleanupMessage}");
                _viewModel.Settings.SetProjectRootStatus(
                    result.OldDirectoryDeleted ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    result.OldDirectoryDeleted ? "目录迁移完成" : "目录迁移完成，旧目录待清理",
                    $"新目录：{projectRootPath}。{cleanupMessage}");
                ShowFloatingTip(
                    result.OldDirectoryDeleted ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                    result.OldDirectoryDeleted ? "目录迁移完成" : "旧目录待清理",
                    cleanupMessage);
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("整体项目目录迁移已取消", "原目录和设置已保留，旧目录未删除。");
                _viewModel.Settings.EnsureCurrentProjectRoot();
                _viewModel.Settings.SetProjectRootStatus(
                    InfoBarSeverity.Warning,
                    "迁移已取消",
                    "原目录和设置已保留，旧目录未删除。");
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "迁移已取消",
                    "原目录和设置已保留。",
                    $"整体项目目录迁移已取消：{oldProjectRootPath} -> {projectRootPath}");
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("整体项目目录迁移失败", "原目录和设置已保留，旧目录未删除。");
                _viewModel.Settings.EnsureCurrentProjectRoot();
                _viewModel.Settings.SetProjectRootStatus(
                    InfoBarSeverity.Error,
                    "迁移失败",
                    $"原目录和设置已保留，旧目录未删除。错误：{ex.Message}");
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "迁移失败",
                    "原目录和设置已保留。",
                    $"整体项目目录迁移失败：{oldProjectRootPath} -> {projectRootPath}；{ex}");
            }
            finally
            {
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private void ThemePreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.Settings.ThemePreference = ThemePreferenceComboBox.SelectedIndex switch
            {
                2 => ThemePreference.Dark,
                1 => ThemePreference.System,
                _ => ThemePreference.Light
            };
            ApplyTheme();
        }

        private void RefreshShellButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Settings.EnsureCurrentProjectRoot();
        }

        private async void RestoreRecommendedSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "恢复推荐值？",
                new TextBlock
                {
                    Width = 420,
                    Text = "恢复推荐值会重置夜间模式、辅助显示和 Log 开关，但不会删除旧目录，也不会删除用户制作数据。",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText: "恢复",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Close));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            _viewModel.Settings.RestoreRecommendedDefaults();
            SyncThemePreferenceComboBox();
            ApplyTheme();
            ShowFloatingTip(InfoBarSeverity.Success, "设置已恢复", "已恢复整体设置推荐值。");
        }

        private async void CreateCharacterButton_Click(object sender, RoutedEventArgs e)
        {
            var defaultCardFacePath = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultCardFace.png");
            var editorContent = CharacterDialogContentFactory.CreateCharacterCreateContent(
                _viewModel.Settings.ProjectRootPath,
                defaultCardFacePath,
                _characterWorkspaceService,
                CreateCardFacePickHandler);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "新建角色",
                editorContent.Content,
                PrimaryButtonText: "创建",
                CloseButtonText: "取消",
                ConfigureDialog: dialog =>
                {
                    dialog.Opened += (_, _) => editorContent.FocusFirstInput();
                    dialog.PrimaryButtonClick += (_, args) =>
                    {
                        if (!editorContent.HasValidInput(out var validationMessage))
                        {
                            editorContent.ShowValidationMessage(validationMessage);
                            args.Cancel = true;
                        }
                    };
                }));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                var character = _characterWorkspaceService.CreateCharacter(
                    _viewModel.Settings.ProjectRootPath,
                    editorContent.ReadInput());
                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "角色已创建",
                    $"{character.Code} 已创建。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "创建角色失败",
                    ex.Message,
                    $"创建角色失败：{ex}");
            }
        }

        private void CharacterCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: CharacterCardViewModel card })
            {
                return;
            }

            if (card.IsAddCard)
            {
                CreateCharacterButton_Click(sender, e);
                return;
            }

            OpenCharacterDetail(card.Code);
        }

        private async void CreateHandCardButton_Click(object sender, RoutedEventArgs e)
        {
            _ = await CreateHandCardAsync();
        }

        private async Task<HandCardInfo?> CreateHandCardAsync(string defaultSuit = "Hearts", int defaultPokerNumber = 1)
        {
            var defaultCardFacePath = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultCardFace.png");
            var editorContent = HandCardDialogContentFactory.CreateHandCardCreateContent(
                _viewModel.Settings.ProjectRootPath,
                defaultCardFacePath,
                _handCardWorkspaceService,
                CreateHandCardFacePickHandler,
                defaultSuit,
                defaultPokerNumber);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "新建手牌",
                editorContent.Content,
                PrimaryButtonText: "创建",
                CloseButtonText: "取消",
                ConfigureDialog: dialog =>
                {
                    dialog.Opened += (_, _) => editorContent.FocusFirstInput();
                    dialog.PrimaryButtonClick += (_, args) =>
                    {
                        if (!editorContent.HasValidInput(out var validationMessage))
                        {
                            editorContent.ShowValidationMessage(validationMessage);
                            args.Cancel = true;
                        }
                    };
                }));
            if (result != DialogResultKind.Primary)
            {
                return null;
            }

            try
            {
                var handCard = _handCardWorkspaceService.CreateHandCard(
                    _viewModel.Settings.ProjectRootPath,
                    editorContent.ReadInput());
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "手牌已创建",
                    $"{handCard.Code} 已创建。");
                return handCard;
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "创建手牌失败",
                    ex.Message,
                    $"创建手牌失败：{ex}");
                return null;
            }
        }

        private void HandCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { CommandParameter: HandCardViewModel card })
            {
                return;
            }

            if (card.IsAddCard)
            {
                CreateHandCardButton_Click(sender, e);
                return;
            }

            OpenHandCardDetail(card.Code);
        }

        private void OpenBasicDeckSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            FlushHandCardDetailSave();
            _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
            HandCardsPage.Visibility = Visibility.Collapsed;
            HandCardDetailPage.Visibility = Visibility.Collapsed;
            BasicDeckSettingsPage.Visibility = Visibility.Visible;
            PlayPageEntrance(BasicDeckSettingsPage);
        }

        private void BackToHandCardsListButton_Click(object sender, RoutedEventArgs e)
        {
            FlushHandCardDetailSave();
            _returnToBasicDeckAfterHandCardDetail = false;
            _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
            BasicDeckSettingsPage.Visibility = Visibility.Collapsed;
            HandCardDetailPage.Visibility = Visibility.Collapsed;
            HandCardsPage.Visibility = Visibility.Visible;
            PlayPageEntrance(HandCardsPage);
        }

        private void BackToBasicDeckSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            FlushHandCardDetailSave();
            _returnToBasicDeckAfterHandCardDetail = false;
            _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
            HandCardsPage.Visibility = Visibility.Collapsed;
            HandCardDetailPage.Visibility = Visibility.Collapsed;
            BasicDeckSettingsPage.Visibility = Visibility.Visible;
            PlayPageEntrance(BasicDeckSettingsPage);
        }

        private async void BasicDeckSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not SuitDeckSlotViewModel slot)
            {
                return;
            }

            if (slot.IsFilled)
            {
                OpenHandCardDetail(slot.CardCode, true);
                return;
            }

            var handCard = await CreateHandCardAsync(slot.Suit, slot.Number);
            if (handCard is not null)
            {
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                OpenBasicDeckSettingsButton_Click(sender, e);
            }
        }

        private void OpenHandCardDetail(string code, bool returnToBasicDeck = false)
        {
            try
            {
                FlushHandCardDetailSave();
                var handCard = _handCardWorkspaceService.GetHandCard(_viewModel.Settings.ProjectRootPath, code);
                _returnToBasicDeckAfterHandCardDetail = returnToBasicDeck;
                _viewModel.HandCardDetail.Load(handCard);
                _viewModel.HandCardDetail.ShowBasicDeckBreadcrumb = returnToBasicDeck;
                _ = LoadHandCardFacePreviewAsync();
                HandCardsPage.Visibility = Visibility.Collapsed;
                BasicDeckSettingsPage.Visibility = Visibility.Collapsed;
                HandCardDetailPage.Visibility = Visibility.Visible;
                PlayPageEntrance(HandCardDetailPage);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "手牌资料打开失败",
                    ex.Message,
                    $"手牌资料打开失败：{ex}");
            }
        }

        private void BackToHandCardsButton_Click(object sender, RoutedEventArgs e)
        {
            FlushHandCardDetailSave();
            HandCardDetailPage.Visibility = Visibility.Collapsed;
            _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
            if (_returnToBasicDeckAfterHandCardDetail)
            {
                _returnToBasicDeckAfterHandCardDetail = false;
                HandCardsPage.Visibility = Visibility.Collapsed;
                BasicDeckSettingsPage.Visibility = Visibility.Visible;
                PlayPageEntrance(BasicDeckSettingsPage);
                return;
            }

            BasicDeckSettingsPage.Visibility = Visibility.Collapsed;
            HandCardsPage.Visibility = Visibility.Visible;
            PlayPageEntrance(HandCardsPage);
        }

        private void OpenCharacterDetail(string code)
        {
            try
            {
                FlushCharacterDetailSave();
                var character = _characterWorkspaceService.GetCharacter(_viewModel.Settings.ProjectRootPath, code);
                _viewModel.CharacterDetail.Load(character);
                _ = LoadCharacterCardFacePreviewAsync();
                CharactersPage.Visibility = Visibility.Collapsed;
                CharacterDetailPage.Visibility = Visibility.Visible;
                PlayPageEntrance(CharacterDetailPage);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "角色资料打开失败",
                    ex.Message,
                    $"角色资料打开失败：{ex}");
            }
        }

        private void BackToCharactersButton_Click(object sender, RoutedEventArgs e)
        {
            FlushCharacterDetailSave();
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            CharactersPage.Visibility = Visibility.Visible;
            _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
            PlayPageEntrance(CharactersPage);
        }

        private void CharacterDetailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleCharacterDetailSave();
        }

        private void CharacterDetailNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ScheduleCharacterDetailSave();
        }

        private void HandCardDetailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleHandCardDetailSave();
        }

        private void HandCardDetailNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            ScheduleHandCardDetailSave();
        }

        private void HandCardDetailComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ScheduleHandCardDetailSave();
        }

        private void RootGrid_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && IsInteractiveElement(source))
            {
                return;
            }

            RootGrid.Focus(FocusState.Programmatic);
        }

        private static bool IsInteractiveElement(DependencyObject source)
        {
            for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is TextBox or NumberBox or PasswordBox or Button or ComboBox or ToggleSwitch or CheckBox or RadioButton or ListView or NavigationView)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddCharacterTagButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CharacterDetail.AddTag();
            ScheduleCharacterDetailSave();
        }

        private void RemoveCharacterTagButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EditableTextEntry entry)
            {
                _viewModel.CharacterDetail.RemoveTag(entry);
                ScheduleCharacterDetailSave();
            }
        }

        private void AddCharacterSkillButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CharacterDetail.AddSkill();
            ScheduleCharacterDetailSave();
        }

        private void RemoveCharacterSkillButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EditableSkillEntry entry)
            {
                _viewModel.CharacterDetail.RemoveSkill(entry);
                ScheduleCharacterDetailSave();
            }
        }

        private void AddCharacterCarryCardButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.CharacterDetail.AddCarryCard();
            ScheduleCharacterDetailSave();
        }

        private void RemoveCharacterCarryCardButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EditableTextEntry entry)
            {
                _viewModel.CharacterDetail.RemoveCarryCard(entry);
                ScheduleCharacterDetailSave();
            }
        }

        private void AddHandCardFunctionGroupButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.HandCardDetail.AddFunctionGroup();
            ScheduleHandCardDetailSave();
        }

        private void RemoveHandCardFunctionGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is EditableTextEntry entry)
            {
                _viewModel.HandCardDetail.RemoveFunctionGroup(entry);
                ScheduleHandCardDetailSave();
            }
        }

        private void SaveCharacterDetailButton_Click(object sender, RoutedEventArgs e)
        {
            FlushCharacterDetailSave();
            ShowFloatingTip(InfoBarSeverity.Success, "角色资料已保存", _viewModel.CharacterDetail.Subtitle);
        }

        private async void ChooseCharacterCardFaceButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickImageFileAsync();
            if (file is null || string.IsNullOrWhiteSpace(_viewModel.CharacterDetail.Code))
            {
                return;
            }

            try
            {
                FlushCharacterDetailSave();
                var (width, height) = CharacterWorkspaceService.GetImageSize(file.Path);
                var crop = await ShowImageCropDialogAsync(
                    "裁剪角色卡面",
                    file.Path,
                    CharacterWorkspaceService.CharacterCardFaceWidth,
                    CharacterWorkspaceService.CharacterCardFaceHeight);
                if (crop is null)
                {
                    return;
                }

                var character = _characterWorkspaceService.ImportCardFaceImage(
                    _viewModel.Settings.ProjectRootPath,
                    _viewModel.CharacterDetail.Code,
                    file.Path,
                    crop.Value);
                _viewModel.CharacterDetail.ApplyImportedCardFace(character);
                await LoadCharacterCardFacePreviewAsync();
                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "卡面已设置",
                    $"源图 {width}x{height}，已裁剪为 732x1028。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "卡面设置失败",
                    ex.Message,
                    $"卡面设置失败：{ex}");
            }
        }

        private async void ChooseHandCardFaceButton_Click(object sender, RoutedEventArgs e)
        {
            var file = await PickImageFileAsync();
            if (file is null || string.IsNullOrWhiteSpace(_viewModel.HandCardDetail.Code))
            {
                return;
            }

            try
            {
                FlushHandCardDetailSave();
                var (width, height) = CharacterWorkspaceService.GetImageSize(file.Path);
                var crop = await ShowImageCropDialogAsync(
                    "裁剪手牌卡面",
                    file.Path,
                    HandCardWorkspaceService.HandCardFaceWidth,
                    HandCardWorkspaceService.HandCardFaceHeight);
                if (crop is null)
                {
                    return;
                }

                var handCard = _handCardWorkspaceService.ImportCardFaceImage(
                    _viewModel.Settings.ProjectRootPath,
                    _viewModel.HandCardDetail.Code,
                    file.Path,
                    crop.Value);
                _viewModel.HandCardDetail.ApplyImportedCardFace(handCard);
                await LoadHandCardFacePreviewAsync();
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "手牌卡面已设置",
                    $"源图 {width}x{height}，已裁剪为 357x300。");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "手牌卡面设置失败",
                    ex.Message,
                    $"手牌卡面设置失败：{ex}");
            }
        }

        private async void ApplyCharacterCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var oldCode = _viewModel.CharacterDetail.Code;
            var newCode = CharacterWorkspaceService.SanitizeCharacterCode(_viewModel.CharacterDetail.CodeEditText);
            if (string.IsNullOrWhiteSpace(oldCode) || string.Equals(oldCode, newCode, StringComparison.Ordinal))
            {
                _viewModel.CharacterDetail.CodeEditText = oldCode;
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "修改英文代号？",
                new TextBlock
                {
                    Width = 460,
                    Text = $"英文代号会影响角色文件夹和后续技能、手牌引用命名。\n\n{oldCode} -> {newCode}\n\n确认后会重命名角色文件夹。",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText: "确定修改",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Close));
            if (result != DialogResultKind.Primary)
            {
                _viewModel.CharacterDetail.CodeEditText = oldCode;
                return;
            }

            try
            {
                FlushCharacterDetailSave();
                var renamed = _characterWorkspaceService.RenameCharacterCode(
                    _viewModel.Settings.ProjectRootPath,
                    oldCode,
                    newCode);
                _viewModel.CharacterDetail.ApplyRenamedCharacter(renamed);
                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "英文代号已修改", $"{oldCode} -> {renamed.Code}");
            }
            catch (Exception ex)
            {
                _viewModel.CharacterDetail.CodeEditText = oldCode;
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "英文代号修改失败",
                    ex.Message,
                    $"英文代号修改失败：{ex}");
            }
        }

        private async void ApplyHandCardCodeButton_Click(object sender, RoutedEventArgs e)
        {
            var oldCode = _viewModel.HandCardDetail.Code;
            var newCode = HandCardWorkspaceService.SanitizeHandCardCode(_viewModel.HandCardDetail.CodeEditText);
            if (string.IsNullOrWhiteSpace(oldCode) || string.Equals(oldCode, newCode, StringComparison.Ordinal))
            {
                _viewModel.HandCardDetail.CodeEditText = oldCode;
                return;
            }

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "修改手牌英文代号？",
                new TextBlock
                {
                    Width = 460,
                    Text = $"英文代号会影响手牌文件夹、后续角色携带牌引用和 Unreal 同步行名。\n\n{oldCode} -> {newCode}\n\n确认后会重命名手牌文件夹。",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText: "确定修改",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Close));
            if (result != DialogResultKind.Primary)
            {
                _viewModel.HandCardDetail.CodeEditText = oldCode;
                return;
            }

            try
            {
                FlushHandCardDetailSave();
                var renamed = _handCardWorkspaceService.RenameHandCardCode(
                    _viewModel.Settings.ProjectRootPath,
                    oldCode,
                    newCode);
                _viewModel.HandCardDetail.ApplyRenamedHandCard(renamed);
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                await LoadHandCardFacePreviewAsync();
                ShowFloatingTip(InfoBarSeverity.Success, "手牌英文代号已修改", $"{oldCode} -> {renamed.Code}");
            }
            catch (Exception ex)
            {
                _viewModel.HandCardDetail.CodeEditText = oldCode;
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "手牌英文代号修改失败",
                    ex.Message,
                    $"手牌英文代号修改失败：{ex}");
            }
        }

        private void ScheduleCharacterDetailSave()
        {
            if (CharacterDetailPage.Visibility != Visibility.Visible)
            {
                return;
            }

            _characterDetailSaveTimer.Stop();
            _characterDetailSaveTimer.Start();
        }

        private void CharacterDetailSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = SaveCharacterDetailAsync(showErrorTip: true);
        }

        private void HandCardDetailSaveTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            sender.Stop();
            _ = SaveHandCardDetailAsync(showErrorTip: true);
        }

        private void FlushCharacterDetailSave()
        {
            _characterDetailSaveTimer.Stop();
            if (!_viewModel.CharacterDetail.IsDirty)
            {
                return;
            }

            try
            {
                var saved = _characterWorkspaceService.SaveCharacter(
                    _viewModel.Settings.ProjectRootPath,
                    _viewModel.CharacterDetail.BuildSnapshot());
                _viewModel.CharacterDetail.ApplySavedCharacter(saved);
                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "角色资料保存失败",
                    ex.Message,
                    $"角色资料保存失败：{ex}");
            }
        }

        private void ScheduleHandCardDetailSave()
        {
            if (HandCardDetailPage.Visibility != Visibility.Visible)
            {
                return;
            }

            _handCardDetailSaveTimer.Stop();
            _handCardDetailSaveTimer.Start();
        }

        private void FlushHandCardDetailSave()
        {
            _handCardDetailSaveTimer.Stop();
            if (!_viewModel.HandCardDetail.IsDirty)
            {
                return;
            }

            try
            {
                var saved = _handCardWorkspaceService.SaveHandCard(
                    _viewModel.Settings.ProjectRootPath,
                    _viewModel.HandCardDetail.BuildSnapshot());
                _viewModel.HandCardDetail.ApplySavedHandCard(saved);
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "手牌资料保存失败",
                    ex.Message,
                    $"手牌资料保存失败：{ex}");
            }
        }

        private async Task SaveCharacterDetailAsync(bool showErrorTip)
        {
            if (!_viewModel.CharacterDetail.IsDirty)
            {
                return;
            }

            try
            {
                var saved = await Task.Run(() => _characterWorkspaceService.SaveCharacter(
                    _viewModel.Settings.ProjectRootPath,
                    _viewModel.CharacterDetail.BuildSnapshot()));
                _viewModel.CharacterDetail.ApplySavedCharacter(saved);
                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
            }
            catch (Exception ex)
            {
                if (showErrorTip)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        "角色资料保存失败",
                        ex.Message,
                        $"角色资料保存失败：{ex}");
                }
            }
        }

        private async Task SaveHandCardDetailAsync(bool showErrorTip)
        {
            if (!_viewModel.HandCardDetail.IsDirty)
            {
                return;
            }

            try
            {
                var saved = await Task.Run(() => _handCardWorkspaceService.SaveHandCard(
                    _viewModel.Settings.ProjectRootPath,
                    _viewModel.HandCardDetail.BuildSnapshot()));
                _viewModel.HandCardDetail.ApplySavedHandCard(saved);
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
            }
            catch (Exception ex)
            {
                if (showErrorTip)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        "手牌资料保存失败",
                        ex.Message,
                        $"手牌资料保存失败：{ex}");
                }
            }
        }

        private RoutedEventHandler CreateCardFacePickHandler(Action<string, System.Drawing.Rectangle?> updatePath)
        {
            return async (_, _) =>
            {
                var file = await PickImageFileAsync();
                if (file is not null)
                {
                    var crop = await ShowImageCropDialogAsync(
                        "裁剪角色卡面",
                        file.Path,
                        CharacterWorkspaceService.CharacterCardFaceWidth,
                        CharacterWorkspaceService.CharacterCardFaceHeight);
                    if (crop is not null)
                    {
                        updatePath(file.Path, crop.Value);
                    }
                }
            };
        }

        private RoutedEventHandler CreateHandCardFacePickHandler(Action<string, System.Drawing.Rectangle?> updatePath)
        {
            return async (_, _) =>
            {
                var file = await PickImageFileAsync();
                if (file is not null)
                {
                    var crop = await ShowImageCropDialogAsync(
                        "裁剪手牌卡面",
                        file.Path,
                        HandCardWorkspaceService.HandCardFaceWidth,
                        HandCardWorkspaceService.HandCardFaceHeight);
                    if (crop is not null)
                    {
                        updatePath(file.Path, crop.Value);
                    }
                }
            };
        }

        private async Task<Windows.Storage.StorageFile?> PickImageFileAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            return await picker.PickSingleFileAsync();
        }

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.Settings.ClearLog();
        }

        private void ShowPage(ToolboxModuleKey key)
        {
            FlushCharacterDetailSave();
            FlushHandCardDetailSave();
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BasicDeckSettingsPage.Visibility = Visibility.Collapsed;
            HandCardDetailPage.Visibility = Visibility.Collapsed;
            _returnToBasicDeckAfterHandCardDetail = false;
            if (key != ToolboxModuleKey.Characters)
            {
                CharacterCardFacePreviewImage.Source = null;
            }
            if (key != ToolboxModuleKey.HandCards)
            {
                HandCardFacePreviewImage.Source = null;
            }
            CharactersPage.Visibility = key == ToolboxModuleKey.Characters ? Visibility.Visible : Visibility.Collapsed;
            HandCardsPage.Visibility = key == ToolboxModuleKey.HandCards ? Visibility.Visible : Visibility.Collapsed;
            UnrealSyncPage.Visibility = key == ToolboxModuleKey.UnrealSync ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = key == ToolboxModuleKey.Settings ? Visibility.Visible : Visibility.Collapsed;

            var module = _viewModel.Modules.FirstOrDefault(item => item.Key == key);
            if (module is not null)
            {
                CurrentModuleText.Text = $"{module.DisplayName}：{module.Description}";
            }

            PlayPageEntrance(GetPageForKey(key));
        }

        private async Task LoadCharacterCardFacePreviewAsync()
        {
            var path = _viewModel.CharacterDetail.CardFacePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                CharacterCardFacePreviewImage.Source = await LoadDefaultCardFacePreviewAsync();
                return;
            }

            try
            {
                CharacterCardFacePreviewImage.Source = await LoadBitmapImageFromFileAsync(path);
            }
            catch (Exception ex)
            {
                CharacterCardFacePreviewImage.Source = await LoadDefaultCardFacePreviewAsync();
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "卡面预览读取失败",
                    "该卡面文件无法解码，已临时显示默认卡面。请重新设置卡面图片。",
                    $"卡面预览读取失败：{BuildSafeDisplayPath(path)}；{ex.GetType().Name}: {ex.Message}");
            }
        }

        private async Task LoadHandCardFacePreviewAsync()
        {
            var path = _viewModel.HandCardDetail.CardFacePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                HandCardFacePreviewImage.Source = await LoadDefaultCardFacePreviewAsync();
                return;
            }

            try
            {
                HandCardFacePreviewImage.Source = await LoadBitmapImageFromFileAsync(path);
            }
            catch (Exception ex)
            {
                HandCardFacePreviewImage.Source = await LoadDefaultCardFacePreviewAsync();
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "手牌卡面预览读取失败",
                    "该手牌卡面文件无法解码，已临时显示默认卡面。请重新设置卡面图片。",
                    $"手牌卡面预览读取失败：{BuildSafeDisplayPath(path)}；{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string BuildSafeDisplayPath(string path)
        {
            var fileName = Path.GetFileName(path);
            var parentName = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
            return string.IsNullOrWhiteSpace(parentName)
                ? fileName
                : Path.Combine(parentName, fileName);
        }

        private async Task<BitmapImage?> LoadDefaultCardFacePreviewAsync()
        {
            var defaultCardFacePath = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultCardFace.png");
            if (!File.Exists(defaultCardFacePath))
            {
                return null;
            }

            try
            {
                return await LoadBitmapImageFromFileAsync(defaultCardFacePath);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<BitmapImage> LoadBitmapImageFromFileAsync(string path)
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await using var fileStream = await file.OpenStreamForReadAsync();
            using var memoryStream = new InMemoryRandomAccessStream();
            await fileStream.CopyToAsync(memoryStream.AsStreamForWrite());
            memoryStream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(memoryStream);
            return bitmap;
        }

        private FrameworkElement GetPageForKey(ToolboxModuleKey key)
        {
            return key switch
            {
                ToolboxModuleKey.Characters => CharactersPage,
                ToolboxModuleKey.HandCards => HandCardsPage,
                ToolboxModuleKey.UnrealSync => UnrealSyncPage,
                ToolboxModuleKey.Settings => SettingsPage,
                _ => CharactersPage
            };
        }

        private void ApplyTheme()
        {
            RootGrid.RequestedTheme = _viewModel.Settings.ThemePreference switch
            {
                ThemePreference.Dark => ElementTheme.Dark,
                ThemePreference.System => ElementTheme.Default,
                _ => ElementTheme.Light
            };
        }

        private void SyncThemePreferenceComboBox()
        {
            ThemePreferenceComboBox.SelectedIndex = _viewModel.Settings.ThemePreference switch
            {
                ThemePreference.Dark => 2,
                ThemePreference.System => 1,
                _ => 0
            };
        }

        private void ApplyWindowIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
            }
        }

        private void ApplyCustomTitleBar()
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        private void ApplyInitialWindowPlacement()
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
    }
}
