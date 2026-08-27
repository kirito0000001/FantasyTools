using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FantasyTools.Models;
using FantasyTools.Services;
using Microsoft.UI.Xaml.Controls;

namespace FantasyTools.ViewModels;

internal sealed class DeveloperReleaseViewModel : ObservableObject
{
    private static readonly Regex UploadProgressRegex = new(@"(?<percent>\d+(?:\.\d+)?)%", RegexOptions.Compiled);
    private static readonly Regex CurlProgressLineRegex = new(@"^[\s#=\-]*\d+(?:\.\d+)?%$", RegexOptions.Compiled);
    private readonly DeveloperReleaseService _service;
    private DeveloperReleaseEnvironment _environment = new(
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        false,
        false,
        false,
        @"D:\DabaoV",
        string.Empty);
    private string _targetVersion = string.Empty;
    private bool _isBusy;
    private string _operationStatus = "等待操作。";

    public DeveloperReleaseViewModel(DeveloperReleaseService service)
    {
        _service = service;
    }

    public ObservableCollection<string> OutputLines { get; } = [];

    public event EventHandler<string>? OutputReceived;

    public bool IsVisible => _environment.IsVisible;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanRun));
            }
        }
    }

    public bool CanRun => IsVisible && !IsBusy;

    public string ProjectRoot => _environment.ProjectRoot;

    public string CurrentVersionText => string.IsNullOrWhiteSpace(_environment.CurrentVersion)
        ? "当前版本：未检测"
        : $"当前版本：{_environment.CurrentVersion}";

    public string TargetVersion
    {
        get => _targetVersion;
        set => SetProperty(ref _targetVersion, value ?? string.Empty);
    }

    public string OutputRoot => _environment.OutputRoot;

    public string ReleaseAssetRoot => _environment.ReleaseAssetRoot;

    public InfoBarSeverity ConfigurationSeverity => _environment.ConfigurationSeverity;

    public string ConfigurationTitle => _environment.IsFullyConfigured ? "发布配置已就绪" : "发布配置待完善";

    public string ConfigurationMessage =>
        $"PowerShell 7：{FormatStatus(!string.IsNullOrWhiteSpace(_environment.PowerShellPath))}；GitHub：{FormatStatus(_environment.IsGitHubReady)}；Gitee：{FormatStatus(_environment.IsGiteeReady)}；Bandizip：{FormatStatus(_environment.IsBandizipReady)}";

    public string OperationStatus
    {
        get => _operationStatus;
        private set => SetProperty(ref _operationStatus, value);
    }

    public async Task RefreshAsync()
    {
        _environment = await _service.InspectAsync();
        if (string.IsNullOrWhiteSpace(TargetVersion) && _environment.IsVisible)
        {
            TargetVersion = DeveloperReleaseService.GetSuggestedVersion(
                _environment.CurrentVersion,
                DeveloperReleaseOperation.PackBeta);
        }

        NotifyEnvironmentChanged();
    }

    public async Task<DeveloperReleaseRunResult> RunAsync(
        DeveloperReleaseOperation operation,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("已有开发者操作正在执行。");
        }

        IsBusy = true;
        OutputLines.Clear();
        OperationStatus = "正在启动...";
        var progress = new Progress<string>(line =>
        {
            AppendOutput(line);
            OperationStatus = line;
        });
        try
        {
            var result = await _service.RunAsync(operation, _environment, TargetVersion, progress, cancellationToken);
            if (result.Succeeded)
            {
                OperationStatus = result.Summary;
            }
            else
            {
                var outputTail = string.Join(Environment.NewLine, OutputLines.TakeLast(12));
                var detailedSummary = string.IsNullOrWhiteSpace(outputTail)
                    ? result.Summary
                    : $"{result.Summary}{Environment.NewLine}{outputTail}";
                result = result with { Summary = detailedSummary };
                OperationStatus = OutputLines.LastOrDefault() ?? result.Summary;
            }
            await RefreshAsync();
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveGiteeTokenAsync(string token)
    {
        _service.SetGiteeToken(token);
        await RefreshAsync();
    }

    public void LaunchGitHubLogin()
    {
        _service.LaunchGitHubLogin(_environment);
    }

    public void UseSuggestedVersion(DeveloperReleaseOperation operation)
    {
        TargetVersion = DeveloperReleaseService.GetSuggestedVersion(_environment.CurrentVersion, operation);
    }

    private void AppendOutput(string line)
    {
        var progressMatch = UploadProgressRegex.Match(line);
        if (progressMatch.Success && CurlProgressLineRegex.IsMatch(line))
        {
            line = $"上传进度：{progressMatch.Groups["percent"].Value}%";
            if (OutputLines.Count > 0 && OutputLines[^1].StartsWith("上传进度：", StringComparison.Ordinal))
            {
                OutputLines[^1] = line;
                OutputReceived?.Invoke(this, line);
                return;
            }
        }

        while (OutputLines.Count >= 300)
        {
            OutputLines.RemoveAt(0);
        }

        OutputLines.Add(line);
        OutputReceived?.Invoke(this, line);
    }

    private void NotifyEnvironmentChanged()
    {
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(ProjectRoot));
        OnPropertyChanged(nameof(CurrentVersionText));
        OnPropertyChanged(nameof(OutputRoot));
        OnPropertyChanged(nameof(ReleaseAssetRoot));
        OnPropertyChanged(nameof(ConfigurationSeverity));
        OnPropertyChanged(nameof(ConfigurationTitle));
        OnPropertyChanged(nameof(ConfigurationMessage));
    }

    private static string FormatStatus(bool ready)
    {
        return ready ? "已配置" : "未配置";
    }
}
