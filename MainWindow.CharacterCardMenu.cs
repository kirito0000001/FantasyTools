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
        private void CharacterCard_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            if (IsExportSelectionActive(Models.WorkspaceTransferKind.Characters))
            {
                args.Handled = true;
                return;
            }

            if (sender is not FrameworkElement { DataContext: CharacterCardViewModel card } element ||
                card.IsAddCard)
            {
                return;
            }

            var flyout = CreateCharacterCardMenu(card);
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

        private void CharacterCard_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (IsExportSelectionActive(Models.WorkspaceTransferKind.Characters))
            {
                e.Handled = true;
                return;
            }

            if (e.Key != VirtualKey.Delete ||
                sender is not FrameworkElement { DataContext: CharacterCardViewModel card } ||
                card.IsAddCard)
            {
                return;
            }

            e.Handled = true;
            _ = DeleteCharacterAsync(card);
        }

        private MenuFlyout CreateCharacterCardMenu(CharacterCardViewModel card)
        {
            var flyout = new MenuFlyout();
            flyout.Items.Add(CreateMenuItem("重命名", Symbol.Edit, async (_, _) => await RenameCharacterFromCardAsync(card)));
            flyout.Items.Add(CreateMenuItem("复制", Symbol.Copy, (_, _) => DuplicateCharacter(card.Code)));
            flyout.Items.Add(CreateMenuItem("备份", Symbol.Save, async (_, _) => await BackupCharacterAsync(card.Code)));
            flyout.Items.Add(CreateMenuItem("打开文件夹", Symbol.OpenFile, async (_, _) => await OpenCharacterFolderAsync(card.Code)));
            flyout.Items.Add(CreateMenuItem("导出", Symbol.Upload, async (_, _) => await ExportCharacterAsync(card.Code)));
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(CreateMenuItem("删除", Symbol.Delete, async (_, _) => await DeleteCharacterAsync(card)));
            return flyout;
        }

        private static MenuFlyoutItem CreateMenuItem(string text, Symbol symbol, RoutedEventHandler click)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new SymbolIcon(symbol)
            };
            item.Click += click;
            return item;
        }

        private async System.Threading.Tasks.Task RenameCharacterFromCardAsync(CharacterCardViewModel card)
        {
            var codeBox = new TextBox
            {
                Header = "角色英文代号",
                Text = card.Code,
                Width = 460,
                PlaceholderText = "例如 Character_LiuBei"
            };
            var preview = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };

            void UpdatePreview()
            {
                var sanitized = CharacterWorkspaceService.SanitizeCharacterCode(codeBox.Text);
                preview.Text = $"修改后文件夹：{_characterWorkspaceService.BuildCharacterFolderPreview(_viewModel.Settings.ProjectRootPath, sanitized)}";
            }

            codeBox.TextChanged += (_, _) => UpdatePreview();
            UpdatePreview();

            var panel = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Width = 460,
                        Text = $"英文代号会影响角色文件夹、技能编号和后续引用命名。\n\n当前角色：{card.DisplayName} / {card.Code}",
                        TextWrapping = TextWrapping.Wrap
                    },
                    codeBox,
                    preview
                }
            };

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "重命名角色？",
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

            var newCode = CharacterWorkspaceService.SanitizeCharacterCode(codeBox.Text);
            if (string.IsNullOrWhiteSpace(newCode))
            {
                ShowFloatingTip(InfoBarSeverity.Warning, "英文代号不能为空", "请重新输入角色英文代号。");
                return;
            }

            if (string.Equals(card.Code, newCode, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                FlushCharacterDetailSave();
                var renamed = _characterWorkspaceService.RenameCharacterCode(
                    _viewModel.Settings.ProjectRootPath,
                    card.Code,
                    newCode);
                if (CharacterDetailPage.Visibility == Visibility.Visible &&
                    string.Equals(_viewModel.CharacterDetail.Code, card.Code, StringComparison.Ordinal))
                {
                    _viewModel.CharacterDetail.ApplyRenamedCharacter(renamed);
                    await LoadCharacterCardFacePreviewAsync();
                }

                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "角色已重命名", $"{card.Code} -> {renamed.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "重命名角色失败",
                    ex.Message,
                    $"重命名角色失败：{card.Code} -> {newCode}；{ex}");
            }
        }

        private void DuplicateCharacter(string code)
        {
            try
            {
                FlushCharacterDetailSave();
                var duplicated = _characterWorkspaceService.DuplicateCharacter(_viewModel.Settings.ProjectRootPath, code);
                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "角色已复制", $"{code} -> {duplicated.Code}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "复制角色失败",
                    ex.Message,
                    $"复制角色失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task BackupCharacterAsync(string code)
        {
            var reasonBox = new TextBox
            {
                Header = "备份备注",
                Text = "Manual",
                Width = 460,
                PlaceholderText = "例如 BeforeBalanceEdit"
            };
            var panel = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Width = 460,
                        Text = $"将为角色 {code} 创建完整文件夹备份。\n\n备注会写入备份文件夹名，方便之后区分还原点。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    reasonBox
                }
            };

            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "备份角色？",
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
                FlushCharacterDetailSave();
                var backupPath = _characterWorkspaceService.BackupCharacter(_viewModel.Settings.ProjectRootPath, code, reasonBox.Text);
                ShowFloatingTip(InfoBarSeverity.Success, "角色已备份", backupPath);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "备份角色失败",
                    ex.Message,
                    $"备份角色失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task OpenCharacterFolderAsync(string code)
        {
            try
            {
                var character = _characterWorkspaceService.GetCharacter(_viewModel.Settings.ProjectRootPath, code);
                await Launcher.LaunchFolderPathAsync(character.Path);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "打开角色文件夹失败",
                    ex.Message,
                    $"打开角色文件夹失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task ExportCharacterAsync(string code)
        {
            try
            {
                FlushCharacterDetailSave();
                var exportPath = _characterWorkspaceService.ExportCharacter(_viewModel.Settings.ProjectRootPath, code);
                ShowFloatingTip(InfoBarSeverity.Success, "角色已导出", exportPath);
                await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(exportPath)!);
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "导出角色失败",
                    ex.Message,
                    $"导出角色失败：{code}；{ex}");
            }
        }

        private async System.Threading.Tasks.Task DeleteCharacterAsync(CharacterCardViewModel card)
        {
            var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "删除角色？",
                new TextBlock
                {
                    Width = 480,
                    Text = $"将删除角色：{card.DisplayName} / {card.Code}\n\n删除前会自动创建 PreDelete 备份，删除后刷新角色卡列表。",
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
                FlushCharacterDetailSave();
                var backupPath = _characterWorkspaceService.DeleteCharacterWithBackup(_viewModel.Settings.ProjectRootPath, card.Code);
                if (CharacterDetailPage.Visibility == Visibility.Visible &&
                    string.Equals(_viewModel.CharacterDetail.Code, card.Code, StringComparison.Ordinal))
                {
                    CharacterDetailPage.Visibility = Visibility.Collapsed;
                    CharactersPage.Visibility = Visibility.Visible;
                }

                _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
                ShowFloatingTip(InfoBarSeverity.Success, "角色已删除", $"已创建删除前备份：{backupPath}");
            }
            catch (Exception ex)
            {
                ShowFloatingTip(
                    InfoBarSeverity.Error,
                    "删除角色失败",
                    ex.Message,
                    $"删除角色失败：{card.Code}；{ex}");
            }
        }
    }
}
