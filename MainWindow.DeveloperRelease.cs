using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FantasyTools.Models;
using FantasyTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace FantasyTools;

public sealed partial class MainWindow
{
    private static readonly Regex DeveloperProgressPercentRegex = new(@"(?<percent>\d+(?:\.\d+)?)%", RegexOptions.Compiled);

    private async void RefreshDeveloperReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDeveloperReleaseAsync(showTip: true);
    }

    private async void PackStableDeveloperButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDeveloperReleaseOperationAsync(DeveloperReleaseOperation.PackStable);
    }

    private async void PackBetaDeveloperButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDeveloperReleaseOperationAsync(DeveloperReleaseOperation.PackBeta);
    }

    private async void PublishLatestDeveloperButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDeveloperReleaseOperationAsync(DeveloperReleaseOperation.PublishLatest);
    }

    private void SuggestStableVersionButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeveloperRelease.UseSuggestedVersion(DeveloperReleaseOperation.PackStable);
    }

    private void SuggestBetaVersionButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.DeveloperRelease.UseSuggestedVersion(DeveloperReleaseOperation.PackBeta);
    }

    private async void SetDeveloperGiteeTokenButton_Click(object sender, RoutedEventArgs e)
    {
        var tokenBox = new PasswordBox
        {
            Header = "Gitee 私人令牌",
            Width = 480,
            PlaceholderText = "粘贴新 Token；留空确认将清除现有配置"
        };
        var panel = new StackPanel
        {
            Width = 480,
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Token 只写入当前 Windows 用户环境变量 FANTASYTOOLS_GITEE_TOKEN，不会写入工程、Log 或发布包。",
                    TextWrapping = TextWrapping.Wrap
                },
                tokenBox
            }
        };
        var result = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            "配置 Gitee Token",
            panel,
            PrimaryButtonText: "保存配置",
            CloseButtonText: "取消",
            DefaultButton: ContentDialogButton.Primary,
            ConfigureDialog: dialog => dialog.Opened += (_, _) => tokenBox.Focus(FocusState.Programmatic)));
        if (result != DialogResultKind.Primary)
        {
            return;
        }

        try
        {
            await _viewModel.DeveloperRelease.SaveGiteeTokenAsync(tokenBox.Password);
            ShowFloatingTip(
                string.IsNullOrWhiteSpace(tokenBox.Password) ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                string.IsNullOrWhiteSpace(tokenBox.Password) ? "Gitee Token 已清除" : "Gitee Token 已保存",
                _viewModel.DeveloperRelease.ConfigurationMessage);
        }
        catch (Exception ex)
        {
            ShowFloatingTip(InfoBarSeverity.Error, "保存 Gitee Token 失败", ex.Message, $"保存 Gitee Token 失败：{ex}");
        }
    }

    private void ConfigureDeveloperGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.DeveloperRelease.LaunchGitHubLogin();
            ShowFloatingTip(InfoBarSeverity.Warning, "GitHub 登录已打开", "完成登录后点击“刷新配置”。");
        }
        catch (Exception ex)
        {
            ShowFloatingTip(InfoBarSeverity.Error, "无法打开 GitHub 登录", ex.Message, $"打开 GitHub 登录失败：{ex}");
        }
    }

    private async void OpenDeveloperOutputRootButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenDeveloperFolderAsync(_viewModel.DeveloperRelease.OutputRoot, "打包输出目录");
    }

    private async void OpenDeveloperReleaseAssetsButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenDeveloperFolderAsync(_viewModel.DeveloperRelease.ReleaseAssetRoot, "发布资产目录");
    }

    private async Task RefreshDeveloperReleaseAsync(bool showTip)
    {
        try
        {
            await _viewModel.DeveloperRelease.RefreshAsync();
            if (showTip)
            {
                ShowFloatingTip(
                    _viewModel.DeveloperRelease.ConfigurationSeverity,
                    _viewModel.DeveloperRelease.ConfigurationTitle,
                    _viewModel.DeveloperRelease.ConfigurationMessage);
            }
        }
        catch (Exception ex)
        {
            ShowFloatingTip(InfoBarSeverity.Error, "刷新发布配置失败", ex.Message, $"刷新发布配置失败：{ex}");
        }
    }

    private async Task RunDeveloperReleaseOperationAsync(DeveloperReleaseOperation operation)
    {
        var operationName = GetDeveloperOperationName(operation);
        var currentStage = "正在准备发布环境...";
        var lastProgressPercent = -1d;
        var progressThrottle = Stopwatch.StartNew();
        void HandleOutput(object? sender, string line)
        {
            var percentMatch = DeveloperProgressPercentRegex.Match(line);
            if (percentMatch.Success &&
                double.TryParse(percentMatch.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                if (Math.Abs(percent - lastProgressPercent) < 0.5 && progressThrottle.ElapsedMilliseconds < 120)
                {
                    return;
                }

                lastProgressPercent = percent;
                progressThrottle.Restart();
                UpdateGlobalProgress($"{currentStage} {percent:0.0}%", percent, operationName, false);
                return;
            }

            if (line.StartsWith(">>>", StringComparison.Ordinal) ||
                line.Contains("开始上传", StringComparison.Ordinal) ||
                line.Contains("正在使用 Bandizip", StringComparison.Ordinal) ||
                line.Contains("正在发布", StringComparison.Ordinal))
            {
                currentStage = line.Trim().TrimStart('>').Trim();
            }

            UpdateGlobalProgress(line, Math.Max(lastProgressPercent, 5), operationName, true);
        }

        _viewModel.DeveloperRelease.OutputReceived += HandleOutput;
        ShowGlobalProgress(operationName, _viewModel.DeveloperRelease.ProjectRoot);
        UpdateGlobalProgress(currentStage, 3, operationName, true);
        try
        {
            var result = await _viewModel.DeveloperRelease.RunAsync(operation, GetGlobalProgressCancellationToken());
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Summary);
            }

            CompleteGlobalProgress($"{operationName}完成", _viewModel.DeveloperRelease.OperationStatus);
            ShowFloatingTip(InfoBarSeverity.Success, $"{operationName}完成", _viewModel.DeveloperRelease.OperationStatus);
        }
        catch (OperationCanceledException)
        {
            CompleteGlobalProgress($"{operationName}已取消", "已停止 PowerShell 进程及其子进程。");
            ShowFloatingTip(InfoBarSeverity.Warning, $"{operationName}已取消", "未完成的发布步骤不会继续执行。");
        }
        catch (Exception ex)
        {
            CompleteGlobalProgress($"{operationName}失败", ex.Message);
            ShowFloatingTip(
                InfoBarSeverity.Error,
                $"{operationName}失败",
                ex.Message,
                $"{operationName}失败：{ex}\n最后输出：{_viewModel.DeveloperRelease.OperationStatus}");
        }
        finally
        {
            _viewModel.DeveloperRelease.OutputReceived -= HandleOutput;
            await HideGlobalProgressAfterDelayAsync();
        }
    }

    private static string GetDeveloperOperationName(DeveloperReleaseOperation operation)
    {
        return operation switch
        {
            DeveloperReleaseOperation.PackStable => "打包正式版工具箱",
            DeveloperReleaseOperation.PackBeta => "打包测试版工具箱",
            _ => "发布最新包"
        };
    }

    private async Task OpenDeveloperFolderAsync(string path, string displayName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException($"{displayName}尚未确定。");
            }

            System.IO.Directory.CreateDirectory(path);
            await Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception ex)
        {
            ShowFloatingTip(InfoBarSeverity.Error, $"打开{displayName}失败", ex.Message, $"打开{displayName}失败：{ex}");
        }
    }
}
