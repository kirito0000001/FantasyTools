using System;
using System.IO;
using FantasyTools.Services;
using FantasyTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FantasyTools
{
    public sealed partial class MainWindow
    {
        private void HandCard_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            if (sender is not FrameworkElement { DataContext: HandCardViewModel card } element ||
                card.IsAddCard)
            {
                return;
            }

            var flyout = CreateHandCardMenu(card);
            if (args.TryGetPosition(element, out var point))
            {
                flyout.ShowAt(element, point);
            }
            else
            {
                flyout.ShowAt(element);
            }

            args.Handled = true;
        }

        private void HandCard_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Delete ||
                sender is not FrameworkElement { DataContext: HandCardViewModel card } ||
                card.IsAddCard)
            {
                return;
            }

            e.Handled = true;
            _ = DeleteHandCardAsync(card);
        }

        private MenuFlyout CreateHandCardMenu(HandCardViewModel card)
        {
            var flyout = new MenuFlyout();
            flyout.Items.Add(CreateMenuItem("重命名", Symbol.Edit, async (_, _) => await RenameHandCardFromCardAsync(card)));
            flyout.Items.Add(CreateMenuItem("复制", Symbol.Copy, (_, _) => DuplicateHandCard(card.Code)));
            flyout.Items.Add(CreateMenuItem("备份", Symbol.Save, async (_, _) => await BackupHandCardAsync(card.Code)));
            flyout.Items.Add(CreateMenuItem("打开文件夹", Symbol.OpenFile, async (_, _) => await OpenHandCardFolderAsync(card.Code)));
            flyout.Items.Add(CreateMenuItem("导出", Symbol.Upload, async (_, _) => await ExportHandCardAsync(card.Code)));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(CreateMenuItem("删除", Symbol.Delete, async (_, _) => await DeleteHandCardAsync(card)));
            return flyout;
        }

        private async System.Threading.Tasks.Task RenameHandCardFromCardAsync(HandCardViewModel card)
        {
            var codeBox = new TextBox
            {
                Header = "手牌英文代号",
                Text = card.Code,
                Width = 420,
                PlaceholderText = "例如 Sha"
            };
            var preview = new TextBlock
            {
                Width = 420,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };

            void UpdatePreview()
            {
                var sanitized = HandCardWorkspaceService.SanitizeHandCardCode(codeBox.Text);
                preview.Text = $"修改后文件夹：{_handCardWorkspaceService.BuildHandCardFolderPreview(_viewModel.Settings.ProjectRootPath, sanitized)}";
            }

            codeBox.TextChanged += (_, _) => UpdatePreview();
            UpdatePreview();

            var panel = new StackPanel
            {
                Width = 420,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Width = 420,
                        Text = $"英文代号会影响手牌文件夹、角色携带牌引用和 Unreal 同步行名。\n\n当前手牌：{card.DisplayName} / {card.Code}",
                        TextWrapping = TextWrapping.Wrap
                    },
                    codeBox,
                    preview
                }
            };

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "重命名手牌？",
                panel,
                PrimaryButtonText: "确定修改",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Close,
                ConfigureDialog: dialog =>
                {
                    dialog.Opened += (_, _) =>
                    {
                        codeBox.Focus(FocusState.Programmatic);
                        codeBox.SelectAll();
                    };
                }));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            var newCode = HandCardWorkspaceService.SanitizeHandCardCode(codeBox.Text);
            if (string.IsNullOrWhiteSpace(newCode))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "英文代号不能为空", "请重新输入手牌英文代号。");
                return;
            }

            if (string.Equals(card.Code, newCode, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                FlushHandCardDetailSave();
                var renamed = _handCardWorkspaceService.RenameHandCardCode(
                    _viewModel.Settings.ProjectRootPath,
                    card.Code,
                    newCode);
                if (HandCardDetailPage.Visibility == Visibility.Visible &&
                    string.Equals(_viewModel.HandCardDetail.Code, card.Code, StringComparison.Ordinal))
                {
                    _viewModel.HandCardDetail.ApplyRenamedHandCard(renamed);
                    await LoadHandCardFacePreviewAsync();
                }

                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "手牌已重命名", $"{card.Code} -> {renamed.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "重命名手牌失败",
                    ex.Message,
                    $"重命名手牌失败：{card.Code} -> {newCode}；{ex}");
            }
        }

        private void DuplicateHandCard(string code)
        {
            try
            {
                FlushHandCardDetailSave();
                var duplicated = _handCardWorkspaceService.DuplicateHandCard(_viewModel.Settings.ProjectRootPath, code);
                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "手牌已复制", $"{code} -> {duplicated.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "复制手牌失败",
                    ex.Message,
                    $"复制手牌失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task BackupHandCardAsync(string code)
        {
            var reasonBox = new TextBox
            {
                Header = "备份备注",
                Text = "Manual",
                Width = 420,
                PlaceholderText = "例如 BeforeBalanceEdit"
            };
            var panel = new StackPanel
            {
                Width = 420,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Width = 420,
                        Text = $"将为手牌 {code} 创建完整文件夹备份。\n\n备注会写入备份文件夹名，方便之后区分还原点。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    reasonBox
                }
            };

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "备份手牌？",
                panel,
                PrimaryButtonText: "备份",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Primary,
                ConfigureDialog: dialog =>
                {
                    dialog.Opened += (_, _) =>
                    {
                        reasonBox.Focus(FocusState.Programmatic);
                        reasonBox.SelectAll();
                    };
                }));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                FlushHandCardDetailSave();
                var backupPath = _handCardWorkspaceService.BackupHandCard(_viewModel.Settings.ProjectRootPath, code, reasonBox.Text);
                ShowFloatingTip(InfoBarSeverity.Success, "手牌已备份", backupPath);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "备份手牌失败",
                    ex.Message,
                    $"备份手牌失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task OpenHandCardFolderAsync(string code)
        {
            try
            {
                var handCard = _handCardWorkspaceService.GetHandCard(_viewModel.Settings.ProjectRootPath, code);
                await Launcher.LaunchFolderPathAsync(handCard.Path);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "打开手牌文件夹失败",
                    ex.Message,
                    $"打开手牌文件夹失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task ExportHandCardAsync(string code)
        {
            try
            {
                FlushHandCardDetailSave();
                var exportPath = _handCardWorkspaceService.ExportHandCard(_viewModel.Settings.ProjectRootPath, code);
                ShowFloatingTip(InfoBarSeverity.Success, "手牌已导出", exportPath);
                await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(exportPath)!);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "导出手牌失败",
                    ex.Message,
                    $"导出手牌失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task DeleteHandCardAsync(HandCardViewModel card)
        {
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "删除手牌？",
                new TextBlock
                {
                    Width = 420,
                    Text = $"将删除手牌：{card.DisplayName} / {card.Code}\n\n删除前会自动创建 PreDelete 备份，删除后刷新手牌卡列表。",
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText: "删除",
                CloseButtonText: "取消",
                DefaultButton: ContentDialogButton.Close));
            if (result != DialogResultKind.Primary)
            {
                return;
            }

            try
            {
                FlushHandCardDetailSave();
                var backupPath = _handCardWorkspaceService.DeleteHandCardWithBackup(_viewModel.Settings.ProjectRootPath, card.Code);
                if (HandCardDetailPage.Visibility == Visibility.Visible &&
                    string.Equals(_viewModel.HandCardDetail.Code, card.Code, StringComparison.Ordinal))
                {
                    HandCardDetailPage.Visibility = Visibility.Collapsed;
                    HandCardsPage.Visibility = Visibility.Visible;
                    HandCardFacePreviewImage.Source = null;
                }

                _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "手牌已删除", $"已创建删除前备份：{backupPath}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "删除手牌失败",
                    ex.Message,
                    $"删除手牌失败：{card.Code}；{ex}");
            }
        }
    }
}
