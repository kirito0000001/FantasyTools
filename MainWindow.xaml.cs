using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
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
        private readonly WorkspaceTransferService _workspaceTransferService;
        private readonly UpdateService _updateService;
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
            _workspaceTransferService = new WorkspaceTransferService();
            _updateService = new UpdateService();
            var settings = new SettingsViewModel(new AppSettingsService(), new LogService(), new ProjectRootMigrationService());
            _viewModel = new ApplicationViewModel(
                settings,
                new GlobalProgressViewModel(),
                new CharactersViewModel(_characterWorkspaceService, defaultCardFacePath),
                new CharacterDetailViewModel(),
                new HandCardsViewModel(_handCardWorkspaceService, defaultCardFacePath),
                new HandCardDetailViewModel(),
                new DeveloperReleaseViewModel(new DeveloperReleaseService()));
            _dialogService = new WinUiDialogService(() => RootGrid.XamlRoot);
            RootGrid.DataContext = _viewModel;
            RegisterHelpKeyboardAccelerators();
            ApplyCustomTitleBar();
            ApplyWindowIcon();
            AppWindow.Resize(new SizeInt32(1500, 920));
            ApplyInitialWindowPlacement();
            settings.LoadAndEnsureProjectRoot();
            settings.PropertyChanged += Settings_PropertyChanged;
            _viewModel.HandCards.UseSuitColoredCards = settings.UseSuitColoredHandCards;
            _viewModel.Characters.Load(settings.ProjectRootPath);
            _viewModel.HandCards.Load(settings.ProjectRootPath);
            SyncThemePreferenceComboBox();
            SyncUpdateSourceComboBox();
            SyncUpdateChannelComboBox();
            ApplyTheme();
            ShowPage(ToolboxModuleKey.Characters);
            _ = _viewModel.DeveloperRelease.RefreshAsync();
            _ = CheckUpdateOnStartupAsync();
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

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.UseSuitColoredHandCards))
            {
                _viewModel.HandCards.UseSuitColoredCards = _viewModel.Settings.UseSuitColoredHandCards;
            }
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
                    InfoBarSeverity.Warning,
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

        private void UpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UpdateChannelComboBox.SelectedIndex < 0)
            {
                return;
            }

            _viewModel.Settings.UpdateChannel = UpdateChannelComboBox.SelectedIndex == 1
                ? UpdateChannel.Beta
                : UpdateChannel.Stable;
        }

        private void UpdateSourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UpdateSourceComboBox.SelectedIndex < 0)
            {
                return;
            }

            _viewModel.Settings.UpdateSource = UpdateSourceComboBox.SelectedIndex switch
            {
                1 => UpdateSource.Gitee,
                _ => UpdateSource.GitHub
            };
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
            SyncUpdateSourceComboBox();
            SyncUpdateChannelComboBox();
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

            if (TryToggleExportSelection(card))
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

        private async void OpenCharacterFilterButton_Click(object sender, RoutedEventArgs e)
        {
            var missingNameBox = CreateFilterCheckBox("缺少中文名", _viewModel.Characters.FilterMissingName);
            var incompleteBox = CreateFilterCheckBox("未设置完全", _viewModel.Characters.FilterIncomplete);
            var multiPhaseBox = CreateFilterCheckBox("拥有多 Stage", _viewModel.Characters.FilterMultiPhase);
            var missingSkillGroupsBox = CreateFilterCheckBox("未设置技能组", _viewModel.Characters.FilterMissingSkillGroups);

            var content = CreateFilterDialogContent(
                "勾选后只显示同时满足这些条件的角色。",
                "资料状态",
                missingNameBox,
                incompleteBox,
                multiPhaseBox,
                missingSkillGroupsBox);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "筛选角色",
                content,
                PrimaryButtonText: "应用",
                SecondaryButtonText: "清除",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary));
            if (result == DialogResultKind.Primary)
            {
                _viewModel.Characters.SetFilters(
                    IsChecked(missingNameBox),
                    IsChecked(incompleteBox),
                    IsChecked(multiPhaseBox),
                    IsChecked(missingSkillGroupsBox));
            }
            else if (result == DialogResultKind.Secondary)
            {
                _viewModel.Characters.ClearFilters();
            }
        }

        private async void OpenCharacterSortButton_Click(object sender, RoutedEventArgs e)
        {
            var updatedAtBox = CreateSortRadioButton("最近修改", CharacterSortKey.UpdatedAt, _viewModel.Characters.SortKey);
            var displayNameBox = CreateSortRadioButton("中文名", CharacterSortKey.DisplayName, _viewModel.Characters.SortKey);
            var codeBox = CreateSortRadioButton("英文代号", CharacterSortKey.Code, _viewModel.Characters.SortKey);
            var phaseBox = CreateSortRadioButton("Stage 数量", CharacterSortKey.PhaseCount, _viewModel.Characters.SortKey);
            var completionBox = CreateSortRadioButton("完成度", CharacterSortKey.Completion, _viewModel.Characters.SortKey);
            var descendingBox = CreateDirectionRadioButton("降序", true, _viewModel.Characters.SortDescending);
            var ascendingBox = CreateDirectionRadioButton("升序", false, _viewModel.Characters.SortDescending);

            var content = CreateSortDialogContent(
                "排序会在当前搜索和筛选结果内生效。",
                [updatedAtBox, displayNameBox, codeBox, phaseBox, completionBox],
                [descendingBox, ascendingBox]);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "排序角色",
                content,
                PrimaryButtonText: "应用",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            _viewModel.Characters.SetSort(
                ReadSelectedSortKey<CharacterSortKey>(updatedAtBox, displayNameBox, codeBox, phaseBox, completionBox),
                ReadSelectedDirection(descendingBox, ascendingBox));
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

            if (TryToggleExportSelection(card))
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

        private async void OpenHandCardFilterButton_Click(object sender, RoutedEventArgs e)
        {
            var baseCardBox = CreateFilterCheckBox("基本牌", _viewModel.HandCards.FilterBaseCards);
            var eventCardBox = CreateFilterCheckBox("事件牌", _viewModel.HandCards.FilterEventCards);
            var equipWeaponBox = CreateFilterCheckBox("装备牌-武器", _viewModel.HandCards.FilterEquipWeaponCards);
            var equipArmorBox = CreateFilterCheckBox("装备牌-防具", _viewModel.HandCards.FilterEquipArmorCards);
            var equipPropBox = CreateFilterCheckBox("装备牌-道具", _viewModel.HandCards.FilterEquipPropCards);
            var judgeCardBox = CreateFilterCheckBox("共鸣牌", _viewModel.HandCards.FilterJudgeCards);
            var heartsBox = CreateFilterCheckBox("红桃", _viewModel.HandCards.FilterHearts);
            var diamondsBox = CreateFilterCheckBox("方片", _viewModel.HandCards.FilterDiamonds);
            var clubsBox = CreateFilterCheckBox("梅花", _viewModel.HandCards.FilterClubs);
            var spadesBox = CreateFilterCheckBox("黑桃", _viewModel.HandCards.FilterSpades);
            var boundBox = CreateFilterCheckBox("已填入基本牌堆", _viewModel.HandCards.FilterBoundToBasicDeck);
            var unboundBox = CreateFilterCheckBox("未填入基本牌堆", _viewModel.HandCards.FilterUnboundToBasicDeck);
            var missingNameBox = CreateFilterCheckBox("未设置中文名", _viewModel.HandCards.FilterMissingName);
            var incompleteBox = CreateFilterCheckBox("未设置完全", _viewModel.HandCards.FilterIncomplete);
            var limitedUseBox = CreateFilterCheckBox("有使用限制", _viewModel.HandCards.FilterLimitedUse);

            var content = CreateFilterDialogContent(
                "勾选后只显示同时满足这些条件的手牌；卡牌类型内部可多选。",
                "卡牌类型",
                baseCardBox,
                eventCardBox,
                equipWeaponBox,
                equipArmorBox,
                equipPropBox,
                judgeCardBox,
                heartsBox,
                diamondsBox,
                clubsBox,
                spadesBox,
                boundBox,
                unboundBox,
                missingNameBox,
                incompleteBox,
                limitedUseBox);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "筛选手牌",
                content,
                PrimaryButtonText: "应用",
                SecondaryButtonText: "清除",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary));
            if (result == DialogResultKind.Primary)
            {
                _viewModel.HandCards.SetFilters(
                    IsChecked(baseCardBox),
                    IsChecked(eventCardBox),
                    IsChecked(equipWeaponBox),
                    IsChecked(equipArmorBox),
                    IsChecked(equipPropBox),
                    IsChecked(judgeCardBox),
                    IsChecked(heartsBox),
                    IsChecked(diamondsBox),
                    IsChecked(clubsBox),
                    IsChecked(spadesBox),
                    IsChecked(boundBox),
                    IsChecked(unboundBox),
                    IsChecked(missingNameBox),
                    IsChecked(incompleteBox),
                    IsChecked(limitedUseBox));
            }
            else if (result == DialogResultKind.Secondary)
            {
                _viewModel.HandCards.ClearFilters();
            }
        }

        private async void OpenHandCardSortButton_Click(object sender, RoutedEventArgs e)
        {
            var updatedAtBox = CreateSortRadioButton("最近修改", HandCardSortKey.UpdatedAt, _viewModel.HandCards.SortKey);
            var displayNameBox = CreateSortRadioButton("中文名", HandCardSortKey.DisplayName, _viewModel.HandCards.SortKey);
            var codeBox = CreateSortRadioButton("英文代号", HandCardSortKey.Code, _viewModel.HandCards.SortKey);
            var cardTypeBox = CreateSortRadioButton("卡牌类型", HandCardSortKey.CardType, _viewModel.HandCards.SortKey);
            var suitNumberBox = CreateSortRadioButton("花色数字", HandCardSortKey.SuitNumber, _viewModel.HandCards.SortKey);
            var remainingUseBox = CreateSortRadioButton("剩余使用次数", HandCardSortKey.RemainingUseCount, _viewModel.HandCards.SortKey);
            var descendingBox = CreateDirectionRadioButton("降序", true, _viewModel.HandCards.SortDescending);
            var ascendingBox = CreateDirectionRadioButton("升序", false, _viewModel.HandCards.SortDescending);

            var content = CreateSortDialogContent(
                "排序会在当前搜索和筛选结果内生效。",
                [updatedAtBox, displayNameBox, codeBox, cardTypeBox, suitNumberBox, remainingUseBox],
                [descendingBox, ascendingBox]);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "排序手牌",
                content,
                PrimaryButtonText: "应用",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            _viewModel.HandCards.SetSort(
                ReadSelectedSortKey<HandCardSortKey>(updatedAtBox, displayNameBox, codeBox, cardTypeBox, suitNumberBox, remainingUseBox),
                ReadSelectedDirection(descendingBox, ascendingBox));
        }

        private static ScrollViewer CreateFilterDialogContent(string description, string groupTitle, params CheckBox[] checkBoxes)
        {
            var panel = new StackPanel
            {
                Width = 520,
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = groupTitle,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            var filterGrid = new Grid
            {
                ColumnSpacing = 18
            };
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var leftColumn = new StackPanel { Spacing = 6 };
            var rightColumn = new StackPanel { Spacing = 6 };
            Grid.SetColumn(rightColumn, 1);
            filterGrid.Children.Add(leftColumn);
            filterGrid.Children.Add(rightColumn);
            for (var index = 0; index < checkBoxes.Length; index++)
            {
                if (index % 2 == 0)
                {
                    leftColumn.Children.Add(checkBoxes[index]);
                }
                else
                {
                    rightColumn.Children.Add(checkBoxes[index]);
                }
            }

            panel.Children.Add(filterGrid);

            return new ScrollViewer
            {
                Width = 540,
                MaxHeight = 560,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = panel
            };
        }

        private static CheckBox CreateFilterCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                MinHeight = 32
            };
        }

        private static bool IsChecked(CheckBox checkBox)
        {
            return checkBox.IsChecked == true;
        }

        private static StackPanel CreateSortDialogContent(string description, RadioButton[] sortButtons, RadioButton[] directionButtons)
        {
            var panel = new StackPanel
            {
                Width = 460,
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "排序方式",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            foreach (var button in sortButtons)
            {
                panel.Children.Add(button);
            }

            panel.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                Text = "方向",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            foreach (var button in directionButtons)
            {
                panel.Children.Add(button);
            }

            return panel;
        }

        private static RadioButton CreateSortRadioButton<TSortKey>(string text, TSortKey value, TSortKey current)
            where TSortKey : struct, Enum
        {
            return new RadioButton
            {
                Content = text,
                GroupName = "SortKey",
                IsChecked = EqualityComparer<TSortKey>.Default.Equals(value, current),
                MinHeight = 32,
                Tag = value
            };
        }

        private static RadioButton CreateDirectionRadioButton(string text, bool descending, bool currentDescending)
        {
            return new RadioButton
            {
                Content = text,
                GroupName = "SortDirection",
                IsChecked = descending == currentDescending,
                MinHeight = 32,
                Tag = descending
            };
        }

        private static TSortKey ReadSelectedSortKey<TSortKey>(params RadioButton[] buttons)
            where TSortKey : struct, Enum
        {
            foreach (var button in buttons)
            {
                if (button.IsChecked == true && button.Tag is TSortKey value)
                {
                    return value;
                }
            }

            return buttons.FirstOrDefault()?.Tag is TSortKey fallback ? fallback : default;
        }

        private static bool ReadSelectedDirection(params RadioButton[] buttons)
        {
            foreach (var button in buttons)
            {
                if (button.IsChecked == true && button.Tag is bool descending)
                {
                    return descending;
                }
            }

            return true;
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

            await ShowBasicDeckHandCardPickerAsync(slot);
        }

        private async void ChangeBasicDeckSlotButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not SuitDeckSlotViewModel slot)
            {
                return;
            }

            await ShowBasicDeckHandCardPickerAsync(slot);
        }

        private async Task ShowBasicDeckHandCardPickerAsync(SuitDeckSlotViewModel slot)
        {
            var slotKey = HandCardWorkspaceService.BuildBasicDeckSlotKey(slot.DeckIndex, slot.Suit, slot.Number);
            var basicDeckSettings = _handCardWorkspaceService.GetBasicDeckSettings(_viewModel.Settings.ProjectRootPath);
            var handCards = _handCardWorkspaceService.GetHandCards(_viewModel.Settings.ProjectRootPath)
                .Where(card =>
                {
                    var binding = _handCardWorkspaceService.GetBasicDeckBindingForHandCard(basicDeckSettings, card.Code);
                    return binding is null ||
                        string.Equals(binding.SlotKey, slotKey, StringComparison.OrdinalIgnoreCase) ||
                        (binding.DeckIndex == slot.DeckIndex &&
                         string.Equals(binding.Suit, slot.Suit, StringComparison.OrdinalIgnoreCase) &&
                         binding.Number == slot.Number);
                })
                .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(card => card.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (handCards.Count == 0)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "没有可选手牌",
                    "请先在手牌页面创建手牌，再填入牌堆槽位。");
                return;
            }

            var selectedCode = slot.CardCode;
            var defaultCardFacePath = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultCardFace.png");
            var selectedText = new TextBlock
            {
                Style = GetAppResource<Style>("SubtleTextStyle"),
                Text = string.IsNullOrWhiteSpace(selectedCode)
                    ? "尚未选择手牌。"
                    : $"已选择：{FindHandCardDisplayName(handCards, selectedCode)}"
            };
            var pickerButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
            var pickerItemsPanel = new Grid
            {
                RowSpacing = 8,
                ColumnSpacing = 8
            };
            void RefreshSelectedPickerItem()
            {
                foreach (var (code, button) in pickerButtons)
                {
                    if (string.Equals(code, selectedCode, StringComparison.OrdinalIgnoreCase))
                    {
                        button.BorderBrush = GetAppResource<Brush>("AccentTextFillColorPrimaryBrush");
                        button.BorderThickness = new Thickness(2);
                    }
                    else
                    {
                        button.BorderBrush = null;
                        button.BorderThickness = new Thickness(0);
                    }
                }
            }

            void BuildPickerItems()
            {
                pickerItemsPanel.Children.Clear();
                pickerItemsPanel.RowDefinitions.Clear();
                pickerItemsPanel.ColumnDefinitions.Clear();
                pickerButtons.Clear();
                for (var column = 0; column < 4; column++)
                {
                    pickerItemsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
                }

                var items = BuildBasicDeckPickerItems(handCards, defaultCardFacePath, selectedCode);
                for (var index = 0; index < items.Count; index++)
                {
                    if (index % 4 == 0)
                    {
                        pickerItemsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
                    }

                    var item = items[index];
                    var button = CreateBasicDeckPickerButton(item, () =>
                    {
                        selectedCode = item.Code;
                        selectedText.Text = $"已选择：{item.DisplayName}";
                        RefreshSelectedPickerItem();
                    });
                    pickerButtons[item.Code] = button;
                    Grid.SetColumn(button, index % 4);
                    Grid.SetRow(button, index / 4);
                    pickerItemsPanel.Children.Add(button);
                }

                RefreshSelectedPickerItem();
            }

            BuildPickerItems();

            var scrollViewer = new ScrollViewer
            {
                MaxHeight = 520,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = pickerItemsPanel
            };
            var panel = new StackPanel
            {
                Width = 660,
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"选择要填入 {slot.DisplayTitle} 的已有手牌。",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(selectedText);
            panel.Children.Add(scrollViewer);

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                slot.IsFilled ? "更改牌堆绑定" : "选择已有手牌",
                panel,
                PrimaryButtonText: "填入槽位",
                SecondaryButtonText: slot.IsFilled ? "清空绑定" : null,
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                ConfigureDialog: dialog =>
                {
                    dialog.PrimaryButtonClick += (_, args) =>
                    {
                        if (string.IsNullOrWhiteSpace(selectedCode))
                        {
                            selectedText.Text = "请先选择一张手牌。";
                            args.Cancel = true;
                        }
                    };
                }));
            if (result == DialogResultKind.Secondary)
            {
                try
                {
                    _handCardWorkspaceService.ClearBasicDeckSlot(
                        _viewModel.Settings.ProjectRootPath,
                        slot.DeckIndex,
                        slot.Suit,
                        slot.Number);
                    _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                    ShowFloatingTip(
                        InfoBarSeverity.Success,
                        "牌堆已清空",
                        $"{slot.DisplayTitle} 已取消绑定。");
                }
                catch (Exception ex)
                {
                    ShowFloatingTip(
                        InfoBarSeverity.Error,
                        "牌堆清空失败",
                        ex.Message,
                        $"牌堆清空失败：{ex}");
                }

                return;
            }

            if (result != DialogResultKind.Primary || string.IsNullOrWhiteSpace(selectedCode))
            {
                return;
            }

            try
            {
                _handCardWorkspaceService.SetBasicDeckSlot(
                    _viewModel.Settings.ProjectRootPath,
                    slot.DeckIndex,
                    slot.Suit,
                    slot.Number,
                    selectedCode);
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(
                    InfoBarSeverity.Success,
                    "牌堆已填入",
                    $"{slot.DisplayTitle} <- {selectedCode}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "牌堆填入失败",
                    ex.Message,
                    $"牌堆填入失败：{ex}");
            }
        }

        private static List<BasicDeckHandCardPickerItem> BuildBasicDeckPickerItems(
            IReadOnlyList<HandCardInfo> handCards,
            string defaultCardFacePath,
            string selectedCode)
        {
            return handCards
                .Select(card => new BasicDeckHandCardPickerItem(
                    card,
                    defaultCardFacePath,
                    string.Equals(card.Code, selectedCode, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        private static string FindHandCardDisplayName(IReadOnlyList<HandCardInfo> handCards, string code)
        {
            var card = handCards.FirstOrDefault(candidate =>
                string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase));
            return card is null
                ? code
                : string.IsNullOrWhiteSpace(card.Name) ? card.Code : card.Name;
        }

        private Button CreateBasicDeckPickerButton(BasicDeckHandCardPickerItem item, Action select)
        {
            var button = new Button
            {
                Width = 136,
                Height = 216,
                Padding = new Thickness(6),
                Style = GetAppResource<Style>("StandardPlayingCardButtonStyle")
            };
            ToolTipService.SetToolTip(button, item.DisplayName);
            var grid = new Grid
            {
                Width = 122,
                Height = 184,
                RowSpacing = 6
            };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(154) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var face = new Border
            {
                Style = GetAppResource<Style>("StandardPlayingCardFrameStyle")
            };
            var media = new Border
            {
                Style = GetAppResource<Style>("StandardPlayingCardMediaStyle"),
                Background = new ImageBrush
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                }
            };
            face.Child = media;
            _ = LoadImageBrushSourceAsync(media, item.CardFacePath);
            Grid.SetRow(face, 0);
            grid.Children.Add(face);

            var title = new TextBlock
            {
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = item.DisplayName,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 112
            };
            Grid.SetRow(title, 1);
            grid.Children.Add(title);

            if (item.IsSelected)
            {
                button.BorderBrush = GetAppResource<Brush>("AccentTextFillColorPrimaryBrush");
                button.BorderThickness = new Thickness(2);
            }

            button.Content = grid;
            button.Click += (_, _) => select();
            return button;
        }

        private static T? GetAppResource<T>(string key) where T : class
        {
            return Application.Current.Resources.TryGetValue(key, out var resource)
                ? resource as T
                : null;
        }

        private static async Task LoadImageBrushSourceAsync(Border target, string path)
        {
            if (target.Background is not ImageBrush imageBrush ||
                string.IsNullOrWhiteSpace(path) ||
                !File.Exists(path))
            {
                return;
            }

            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                using var stream = await file.OpenReadAsync();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                imageBrush.ImageSource = bitmap;
            }
            catch
            {
            }
        }

        private void OpenHandCardDetail(string code, bool returnToBasicDeck = false)
        {
            try
            {
                FlushHandCardDetailSave();
                var handCard = _handCardWorkspaceService.GetHandCard(_viewModel.Settings.ProjectRootPath, code);
                _returnToBasicDeckAfterHandCardDetail = returnToBasicDeck;
                var basicDeckBinding = _handCardWorkspaceService.GetBasicDeckBindingForHandCard(
                    _viewModel.Settings.ProjectRootPath,
                    handCard.Code);
                _viewModel.HandCardDetail.Load(handCard, basicDeckBinding);
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
                SaveHandCardDetailCore();
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

        private Task SaveHandCardDetailAsync(bool showErrorTip)
        {
            if (!_viewModel.HandCardDetail.IsDirty)
            {
                return Task.CompletedTask;
            }

            try
            {
                SaveHandCardDetailCore();
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

            return Task.CompletedTask;
        }

        private void SaveHandCardDetailCore()
        {
            if (!_viewModel.HandCardDetail.IsDirty)
            {
                return;
            }

            var projectRootPath = _viewModel.Settings.ProjectRootPath;
            var snapshotVersion = _viewModel.HandCardDetail.EditVersion;
            var snapshot = _viewModel.HandCardDetail.BuildSnapshot();
            var saved = _handCardWorkspaceService.SaveHandCard(projectRootPath, snapshot);
            _viewModel.HandCardDetail.ApplySavedHandCard(saved, snapshotVersion);
            _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
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

        private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(showNoUpdateTip: true, allowUpdatePrompt: true);
        }

        private async void TestUpdateConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            await TestUpdateConnectionAsync();
        }

        private async void OpenReleasePageButton_Click(object sender, RoutedEventArgs e)
        {
            await _updateService.OpenReleasePageAsync(_viewModel.Settings.UpdateReleasePageUrl);
        }

        private async Task CheckUpdateOnStartupAsync()
        {
            if (!_viewModel.Settings.UpdateAutoCheckEnabled || !_viewModel.Settings.UpdateCheckOnStartup)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5));
            await CheckForUpdatesAsync(showNoUpdateTip: true, allowUpdatePrompt: true, isStartupCheck: true);
        }

        private async Task CheckForUpdatesAsync(
            bool showNoUpdateTip,
            bool allowUpdatePrompt,
            bool isStartupCheck = false)
        {
            try
            {
                var sourceDisplayName = _viewModel.Settings.UpdateSourceDisplayName;
                if (!isStartupCheck)
                {
                    ShowGlobalProgress("检查热更新", $"正在请求 {sourceDisplayName} Release...");
                    UpdateGlobalProgress($"正在请求 {sourceDisplayName} Release...", 8, _viewModel.Settings.UpdateChannelText, true);
                }

                var result = await _updateService.CheckAsync(
                    _viewModel.Settings.UpdateSource,
                    _viewModel.Settings.UpdateChannel,
                    _viewModel.Settings.UpdateConnectionTimeoutSecondsValue,
                    isStartupCheck ? CancellationToken.None : GetGlobalProgressCancellationToken());
                _viewModel.Settings.SetUpdateStatus(result.Message);
                if (!isStartupCheck)
                {
                    CompleteGlobalProgress("检查完成", result.Message);
                    await HideGlobalProgressAfterDelayAsync();
                }

                if (!result.HasUpdate || result.Manifest is null || result.Asset is null)
                {
                    if (showNoUpdateTip)
                    {
                        ShowFloatingTip(InfoBarSeverity.Success, "热更新检查完成", result.Message);
                    }

                    return;
                }

                if (allowUpdatePrompt)
                {
                    await PromptAndInstallUpdateAsync(result);
                }
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("检查已取消", "没有下载或替换任何文件。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (TimeoutException ex)
            {
                _viewModel.Settings.SetUpdateStatus(ex.Message);
                if (!isStartupCheck)
                {
                    CompleteGlobalProgress($"{_viewModel.Settings.UpdateSourceDisplayName} 连接超时", ex.Message);
                    await HideGlobalProgressAfterDelayAsync();
                }

                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    $"{_viewModel.Settings.UpdateSourceDisplayName} 连接超时",
                    ex.Message,
                    $"热更新连接超时：{ex}");
            }
            catch (Exception ex)
            {
                _viewModel.Settings.SetUpdateStatus($"热更新检查失败：{ex.Message}");
                if (!isStartupCheck)
                {
                    CompleteGlobalProgress("热更新检查失败", ex.Message);
                    await HideGlobalProgressAfterDelayAsync();
                }

                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    "热更新检查失败",
                    ex.Message,
                    $"热更新检查失败：{ex}");
            }
        }

        private async Task TestUpdateConnectionAsync()
        {
            var sourceDisplayName = _viewModel.Settings.UpdateSourceDisplayName;
            ShowGlobalProgress($"测试 {sourceDisplayName} 连接", $"正在访问 {sourceDisplayName} Release...");
            UpdateGlobalProgress(
                $"正在访问 {sourceDisplayName} Release...",
                20,
                $"最长 { _viewModel.Settings.UpdateConnectionTimeoutSecondsValue } 秒",
                true);
            try
            {
                var result = await _updateService.MeasureConnectionAsync(
                    _viewModel.Settings.UpdateSource,
                    _viewModel.Settings.UpdateChannel,
                    _viewModel.Settings.UpdateConnectionTimeoutSecondsValue,
                    GetGlobalProgressCancellationToken());
                var message = $"{result.Message}；耗时 {result.Elapsed.TotalSeconds:0.0} 秒。";
                _viewModel.Settings.SetUpdateStatus(message);
                CompleteGlobalProgress($"{sourceDisplayName} 连接正常", message);
                ShowFloatingTip(InfoBarSeverity.Success, $"{sourceDisplayName} 连接正常", message);
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("测速已取消", "没有进行更新检查或下载。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (TimeoutException ex)
            {
                _viewModel.Settings.SetUpdateStatus(ex.Message);
                CompleteGlobalProgress($"{sourceDisplayName} 连接超时", ex.Message);
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    $"{sourceDisplayName} 连接超时",
                    ex.Message,
                    $"{sourceDisplayName} 连接测速超时：{ex}");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                var message = $"{sourceDisplayName} 连接失败：{ex.Message}";
                _viewModel.Settings.SetUpdateStatus(message);
                CompleteGlobalProgress($"{sourceDisplayName} 连接失败", ex.Message);
                ShowFloatingTip(
                    InfoBarSeverity.Warning,
                    $"{sourceDisplayName} 连接失败",
                    ex.Message,
                    $"{sourceDisplayName} 连接测速失败：{ex}");
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private async Task PromptAndInstallUpdateAsync(UpdateCheckResult result)
        {
            var manifest = result.Manifest!;
            var asset = result.Asset!;
            var body = new StackPanel
            {
                Width = 460,
                Spacing = 8
            };
            body.Children.Add(new TextBlock { Text = $"当前版本：{result.CurrentVersionText}" });
            body.Children.Add(new TextBlock { Text = $"新版本：{result.LatestVersionText ?? manifest.Version}" });
            body.Children.Add(new TextBlock { Text = $"更新通道：{GetUpdateChannelDisplayName(manifest.Channel)}" });
            body.Children.Add(new TextBlock { Text = $"更新包：{asset.FileName}" });
            body.Children.Add(new TextBlock { Text = $"包大小：{UpdateService.FormatBytes(asset.SizeBytes)}" });
            body.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 6, 0, 0),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = "更新公告"
            });
            body.Children.Add(new Border
            {
                MaxHeight = 180,
                Padding = new Thickness(12),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                            ? "本次 Release 没有填写更新公告。"
                            : result.ReleaseNotes,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            });
            body.Children.Add(new TextBlock
            {
                Text = "下载和校验会在工具箱内完成；之后会打开 PowerShell 替换程序文件，完成后按 Enter 打开新版本。",
                TextWrapping = TextWrapping.Wrap
            });

            var dialogResult = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "发现新版本",
                body,
                PrimaryButtonText: "下载并更新",
                CloseButtonText: "继续使用旧版"));
            if (dialogResult != DialogResultKind.Primary)
            {
                return;
            }

            await DownloadAndLaunchUpdaterAsync(manifest, asset);
        }

        private static string GetUpdateChannelDisplayName(string channel)
        {
            return string.Equals(channel, "beta", StringComparison.OrdinalIgnoreCase)
                ? "测试版"
                : "正式版";
        }

        private async Task DownloadAndLaunchUpdaterAsync(UpdateManifest manifest, UpdateAssetManifest asset)
        {
            ShowGlobalProgress("下载热更新", asset.FileName);
            try
            {
                var progress = new Progress<ProgressUpdate>(update =>
                {
                    UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate);
                });
                var download = await _updateService.DownloadAndVerifyAsync(
                    manifest,
                    asset,
                    progress,
                    GetGlobalProgressCancellationToken());
                CompleteGlobalProgress("更新包已校验", "正在打开 PowerShell updater。");
                _viewModel.Settings.SetUpdateStatus($"更新包已下载：{manifest.Version}");
                await _updateService.LaunchUpdaterAsync(
                    download.PackagePath,
                    manifest,
                    asset,
                    progress,
                    GetGlobalProgressCancellationToken());
                Application.Current.Exit();
            }
            catch (OperationCanceledException)
            {
                CompleteGlobalProgress("下载已取消", "没有替换任何程序文件。");
                ShowFloatingTip(InfoBarSeverity.Warning, "下载已取消", "没有替换任何程序文件。");
                await HideGlobalProgressAfterDelayAsync();
            }
            catch (Exception ex)
            {
                CompleteGlobalProgress("热更新失败", ex.Message);
                _viewModel.Settings.SetUpdateStatus($"热更新失败：{ex.Message}");
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "热更新失败",
                    ex.Message,
                    $"热更新失败：{ex}");
                await HideGlobalProgressAfterDelayAsync();
            }
        }

        private void ShowPage(ToolboxModuleKey key)
        {
            if (_activeExportSelectionKind is not null)
            {
                EndExportSelection();
            }

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
            _viewModel.HandCards.RefreshCardVisuals();
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

        private void SyncUpdateChannelComboBox()
        {
            UpdateChannelComboBox.SelectedIndex = _viewModel.Settings.UpdateChannel == UpdateChannel.Beta ? 1 : 0;
        }

        private void SyncUpdateSourceComboBox()
        {
            UpdateSourceComboBox.SelectedIndex = _viewModel.Settings.UpdateSource switch
            {
                UpdateSource.Gitee => 1,
                _ => 0
            };
        }

        private void ApplyWindowIcon()
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);
                ApplyWin32WindowIcon(iconPath);
            }
        }

        private void ApplyWin32WindowIcon(string iconPath)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var largeIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 32, 32, LoadFromFile);
            var smallIcon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 16, 16, LoadFromFile);
            if (largeIcon != IntPtr.Zero)
            {
                SendMessage(hwnd, WindowMessageSetIcon, IconBig, largeIcon);
            }

            if (smallIcon != IntPtr.Zero)
            {
                SendMessage(hwnd, WindowMessageSetIcon, IconSmall, smallIcon);
                SendMessage(hwnd, WindowMessageSetIcon, IconSmall2, smallIcon);
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

        private const uint WindowMessageSetIcon = 0x0080;
        private const nuint IconSmall = 0;
        private const nuint IconBig = 1;
        private const nuint IconSmall2 = 2;
        private const uint ImageIcon = 1;
        private const uint LoadFromFile = 0x00000010;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadImage(
            IntPtr hinst,
            string lpszName,
            uint uType,
            int cxDesired,
            int cyDesired,
            uint fuLoad);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(
            IntPtr hWnd,
            uint msg,
            nuint wParam,
            IntPtr lParam);
    }
}
