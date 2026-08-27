using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FantasyTools.Models;
using FantasyTools.Services;
using FantasyTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;

namespace FantasyTools;

public sealed partial class MainWindow
{
    private WorkspaceTransferKind? _activeExportSelectionKind;
    private bool _isSyncingExportSelection;

    private async void ImportCharacterPackagesButton_Click(object sender, RoutedEventArgs e)
    {
        var packagePaths = await PickTransferPackagesAsync();
        if (packagePaths.Length == 0)
        {
            return;
        }

        var policy = await ChooseImportConflictPolicyAsync("角色", packagePaths.Length);
        if (policy is null)
        {
            return;
        }

        await ImportPackagesAsync(WorkspaceTransferKind.Characters, packagePaths, policy.Value);
    }

    private async void ImportHandCardPackagesButton_Click(object sender, RoutedEventArgs e)
    {
        var packagePaths = await PickTransferPackagesAsync();
        if (packagePaths.Length == 0)
        {
            return;
        }

        var policy = await ChooseImportConflictPolicyAsync("手牌", packagePaths.Length);
        if (policy is null)
        {
            return;
        }

        await ImportPackagesAsync(WorkspaceTransferKind.HandCards, packagePaths, policy.Value);
    }

    private async void ImportHandCardsFromProjectButton_Click(object sender, RoutedEventArgs e)
    {
        await ImportFromProjectAsync(WorkspaceTransferKind.HandCards);
    }

    private void ExportCharactersButton_Click(object sender, RoutedEventArgs e)
    {
        BeginExportSelection(WorkspaceTransferKind.Characters);
    }

    private void ExportHandCardsButton_Click(object sender, RoutedEventArgs e)
    {
        BeginExportSelection(WorkspaceTransferKind.HandCards);
    }

    private async void OpenCharacterExportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenExportFolderAsync(WorkspaceTransferKind.Characters);
    }

    private async void OpenHandCardExportFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenExportFolderAsync(WorkspaceTransferKind.HandCards);
    }

    private async Task<string[]> PickTransferPackagesAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads
        };
        picker.FileTypeFilter.Add(".zip");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        return files.Select(file => file.Path).ToArray();
    }

    private async Task<string?> PickSourceProjectAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return null;
        }

        var selectedPath = folder.Path;
        if (Directory.Exists(Path.Combine(selectedPath, CharacterWorkspaceService.CharactersFolderName)) ||
            Directory.Exists(Path.Combine(selectedPath, HandCardWorkspaceService.HandCardsFolderName)))
        {
            return selectedPath;
        }

        var nestedProjectPath = Path.Combine(selectedPath, AppSettingsService.ProjectRootFolderName);
        return Directory.Exists(nestedProjectPath) ? nestedProjectPath : selectedPath;
    }

    private async Task<WorkspaceImportConflictPolicy?> ChooseImportConflictPolicyAsync(string displayName, int sourceCount)
    {
        var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            $"导入{displayName}？",
            new TextBlock
            {
                Width = 460,
                Text = $"已选择 {sourceCount} 个导入来源。导入会先校验元数据和卡面，全部通过后再写入当前项目。\n\n同英文代号内容可以覆盖或跳过；覆盖前会自动创建 PreImport 备份。",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText: "覆盖同名并导入",
            SecondaryButtonText: "仅导入新增项",
            CloseButtonText: "取消",
            DefaultButton: ContentDialogButton.Secondary));
        return result switch
        {
            DialogResultKind.Primary => WorkspaceImportConflictPolicy.Replace,
            DialogResultKind.Secondary => WorkspaceImportConflictPolicy.Skip,
            _ => null
        };
    }

    private async Task ImportPackagesAsync(
        WorkspaceTransferKind kind,
        string[] packagePaths,
        WorkspaceImportConflictPolicy conflictPolicy)
    {
        var displayName = GetTransferDisplayName(kind);
        ShowGlobalProgress($"导入{displayName}", $"已选择 {packagePaths.Length} 个数据包");
        try
        {
            var progress = new Progress<ProgressUpdate>(update =>
                UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            var result = await Task.Run(() => _workspaceTransferService.ImportPackages(
                _viewModel.Settings.ProjectRootPath,
                kind,
                packagePaths,
                conflictPolicy,
                progress));
            ReloadTransferredItems(kind);
            CompleteGlobalProgress($"{displayName}导入完成", result.Summary);
            ShowFloatingTip(InfoBarSeverity.Success, $"{displayName}已导入", result.Summary);
            await HideGlobalProgressAfterDelayAsync();
        }
        catch (Exception ex)
        {
            HideGlobalProgress();
            ShowFloatingTip(
                InfoBarSeverity.Error,
                $"导入{displayName}失败",
                ex.Message,
                $"导入{displayName}失败：{ex}");
        }
    }

    private async Task ImportFromProjectAsync(WorkspaceTransferKind kind)
    {
        var sourceProjectPath = await PickSourceProjectAsync();
        if (string.IsNullOrWhiteSpace(sourceProjectPath))
        {
            return;
        }

        if (Path.GetFullPath(sourceProjectPath).TrimEnd(Path.DirectorySeparatorChar)
            .Equals(Path.GetFullPath(_viewModel.Settings.ProjectRootPath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            ShowFloatingTip(InfoBarSeverity.Warning, "不能导入当前项目", "请选择另一个幻杀工具箱项目目录。");
            return;
        }

        var displayName = GetTransferDisplayName(kind);
        var policy = await ChooseImportConflictPolicyAsync(displayName, 1);
        if (policy is null)
        {
            return;
        }

        ShowGlobalProgress($"从其他项目导入{displayName}", sourceProjectPath);
        try
        {
            var progress = new Progress<ProgressUpdate>(update =>
                UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            var result = await Task.Run(() => _workspaceTransferService.ImportFromProject(
                _viewModel.Settings.ProjectRootPath,
                kind,
                sourceProjectPath,
                policy.Value,
                progress));
            ReloadTransferredItems(kind);
            CompleteGlobalProgress($"{displayName}导入完成", result.Summary);
            ShowFloatingTip(InfoBarSeverity.Success, $"{displayName}已导入", result.Summary);
            await HideGlobalProgressAfterDelayAsync();
        }
        catch (Exception ex)
        {
            HideGlobalProgress();
            ShowFloatingTip(
                InfoBarSeverity.Error,
                $"导入{displayName}失败",
                ex.Message,
                $"从其他项目导入{displayName}失败：{sourceProjectPath}；{ex}");
        }
    }

    private async Task ExportAllAsync(WorkspaceTransferKind kind)
    {
        var displayName = GetTransferDisplayName(kind);
        ShowGlobalProgress($"导出全部{displayName}", _viewModel.Settings.ProjectRootPath);
        try
        {
            FlushCharacterDetailSave();
            FlushHandCardDetailSave();
            var progress = new Progress<ProgressUpdate>(update =>
                UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            var exportPath = await Task.Run(() => kind == WorkspaceTransferKind.Characters
                ? _workspaceTransferService.ExportCharacters(_viewModel.Settings.ProjectRootPath, progress: progress)
                : _workspaceTransferService.ExportHandCards(_viewModel.Settings.ProjectRootPath, progress: progress));
            CompleteGlobalProgress($"全部{displayName}已导出", exportPath);
            ShowFloatingTip(InfoBarSeverity.Success, $"全部{displayName}已导出", exportPath);
            await HideGlobalProgressAfterDelayAsync();
            await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(exportPath)!);
        }
        catch (Exception ex)
        {
            HideGlobalProgress();
            ShowFloatingTip(
                InfoBarSeverity.Error,
                $"导出{displayName}失败",
                ex.Message,
                $"导出全部{displayName}失败：{ex}");
        }
    }

    private void BeginExportSelection(WorkspaceTransferKind kind)
    {
        EndExportSelection();
        var cards = GetExportSelectionCards(kind);
        if (cards.Count == 0)
        {
            var displayName = GetTransferDisplayName(kind);
            ShowFloatingTip(InfoBarSeverity.Warning, $"没有可导出的{displayName}", $"请先创建或导入{displayName}。");
            return;
        }

        _activeExportSelectionKind = kind;
        _isSyncingExportSelection = true;
        foreach (var card in cards)
        {
            card.IsExportSelected = false;
            card.IsExportSelectionVisible = true;
        }
        _isSyncingExportSelection = false;

        if (kind == WorkspaceTransferKind.Characters)
        {
            CharactersToolbar.Visibility = Visibility.Collapsed;
            CharacterExportSelectionToolbar.Visibility = Visibility.Visible;
        }
        else
        {
            HandCardsToolbar.Visibility = Visibility.Collapsed;
            HandCardExportSelectionToolbar.Visibility = Visibility.Visible;
        }

        UpdateExportSelectionState();
    }

    private void EndExportSelection()
    {
        if (_activeExportSelectionKind is { } kind)
        {
            _isSyncingExportSelection = true;
            foreach (var card in GetExportSelectionCards(kind))
            {
                card.IsExportSelected = false;
                card.IsExportSelectionVisible = false;
            }
            _isSyncingExportSelection = false;
        }

        _activeExportSelectionKind = null;
        CharactersToolbar.Visibility = Visibility.Visible;
        CharacterExportSelectionToolbar.Visibility = Visibility.Collapsed;
        HandCardsToolbar.Visibility = Visibility.Visible;
        HandCardExportSelectionToolbar.Visibility = Visibility.Collapsed;
    }

    private IReadOnlyList<IExportSelectableCard> GetExportSelectionCards(WorkspaceTransferKind kind)
    {
        return kind == WorkspaceTransferKind.Characters
            ? _viewModel.Characters.Cards.Where(card => !card.IsAddCard).Cast<IExportSelectableCard>().ToList()
            : _viewModel.HandCards.Cards.Where(card => !card.IsAddCard).Cast<IExportSelectableCard>().ToList();
    }

    private bool TryToggleExportSelection(IExportSelectableCard card)
    {
        if (_activeExportSelectionKind is null)
        {
            return false;
        }

        if (!card.IsAddCard && card.IsExportSelectionVisible)
        {
            card.IsExportSelected = !card.IsExportSelected;
            UpdateExportSelectionState();
        }

        return true;
    }

    private bool IsExportSelectionActive(WorkspaceTransferKind kind)
    {
        return _activeExportSelectionKind == kind;
    }

    private void ExportCardCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateExportSelectionState();
    }

    private void CharacterExportSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SetAllExportSelection(WorkspaceTransferKind.Characters, CharacterExportSelectAllCheckBox.IsChecked == true);
    }

    private void HandCardExportSelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SetAllExportSelection(WorkspaceTransferKind.HandCards, HandCardExportSelectAllCheckBox.IsChecked == true);
    }

    private void SetAllExportSelection(WorkspaceTransferKind kind, bool isSelected)
    {
        if (_isSyncingExportSelection || _activeExportSelectionKind != kind)
        {
            return;
        }

        _isSyncingExportSelection = true;
        foreach (var card in GetExportSelectionCards(kind))
        {
            card.IsExportSelected = isSelected;
        }
        _isSyncingExportSelection = false;
        UpdateExportSelectionState();
    }

    private void UpdateExportSelectionState()
    {
        if (_isSyncingExportSelection || _activeExportSelectionKind is not { } kind)
        {
            return;
        }

        var cards = GetExportSelectionCards(kind);
        var selectedCount = cards.Count(card => card.IsExportSelected);
        _isSyncingExportSelection = true;
        var selectAllState = selectedCount switch
        {
            0 => false,
            _ when selectedCount == cards.Count => true,
            _ => (bool?)null
        };
        if (kind == WorkspaceTransferKind.Characters)
        {
            CharacterExportSelectAllCheckBox.IsChecked = selectAllState;
            CharacterExportSelectedCountText.Text = $"已选 {selectedCount} / {cards.Count}";
            ConfirmCharacterExportSelectionButton.IsEnabled = selectedCount > 0;
        }
        else
        {
            HandCardExportSelectAllCheckBox.IsChecked = selectAllState;
            HandCardExportSelectedCountText.Text = $"已选 {selectedCount} / {cards.Count}";
            ConfirmHandCardExportSelectionButton.IsEnabled = selectedCount > 0;
        }
        _isSyncingExportSelection = false;
    }

    private void CancelCharacterExportSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        EndExportSelection();
    }

    private void CancelHandCardExportSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        EndExportSelection();
    }

    private async void ConfirmCharacterExportSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        await ConfirmExportSelectionAsync(WorkspaceTransferKind.Characters);
    }

    private async void ConfirmHandCardExportSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        await ConfirmExportSelectionAsync(WorkspaceTransferKind.HandCards);
    }

    private async Task ConfirmExportSelectionAsync(WorkspaceTransferKind kind)
    {
        if (_activeExportSelectionKind != kind)
        {
            return;
        }

        var selectedCodes = GetExportSelectionCards(kind)
            .Where(card => card.IsExportSelected)
            .Select(card => card.Code)
            .ToArray();
        if (selectedCodes.Length == 0)
        {
            return;
        }

        EndExportSelection();
        await ExportSelectedAsync(kind, selectedCodes);
    }

    private async Task ExportSelectedAsync(WorkspaceTransferKind kind, IReadOnlyCollection<string> selectedCodes)
    {
        var displayName = GetTransferDisplayName(kind);
        ShowGlobalProgress($"导出{displayName}", $"已选择 {selectedCodes.Count} 个{displayName}");
        try
        {
            FlushCharacterDetailSave();
            FlushHandCardDetailSave();
            var progress = new Progress<ProgressUpdate>(update =>
                UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            var exportPath = await Task.Run(() => kind == WorkspaceTransferKind.Characters
                ? _workspaceTransferService.ExportCharacters(_viewModel.Settings.ProjectRootPath, selectedCodes, progress)
                : _workspaceTransferService.ExportHandCards(_viewModel.Settings.ProjectRootPath, selectedCodes, progress));
            CompleteGlobalProgress($"{displayName}已导出", exportPath);
            ShowFloatingTip(InfoBarSeverity.Success, $"{displayName}已导出", $"已导出 {selectedCodes.Count} 个{displayName}：{exportPath}");
            await HideGlobalProgressAfterDelayAsync();
            await Launcher.LaunchFolderPathAsync(Path.GetDirectoryName(exportPath)!);
        }
        catch (Exception ex)
        {
            HideGlobalProgress();
            ShowFloatingTip(
                InfoBarSeverity.Error,
                $"导出{displayName}失败",
                ex.Message,
                $"导出所选{displayName}失败：{ex}");
        }
    }

    private async Task OpenExportFolderAsync(WorkspaceTransferKind kind)
    {
        var exportDirectory = _workspaceTransferService.GetExportDirectory(_viewModel.Settings.ProjectRootPath, kind);
        Directory.CreateDirectory(exportDirectory);
        await Launcher.LaunchFolderPathAsync(exportDirectory);
    }

    private void ReloadTransferredItems(WorkspaceTransferKind kind)
    {
        if (kind == WorkspaceTransferKind.Characters)
        {
            _viewModel.Characters.Load(_viewModel.Settings.ProjectRootPath);
        }
        else
        {
            _viewModel.HandCards.Load(_viewModel.Settings.ProjectRootPath);
        }
    }

    private static string GetTransferDisplayName(WorkspaceTransferKind kind)
    {
        return kind == WorkspaceTransferKind.Characters ? "角色" : "手牌";
    }
}
