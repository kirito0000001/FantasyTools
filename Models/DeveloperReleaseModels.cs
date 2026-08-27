using Microsoft.UI.Xaml.Controls;

namespace FantasyTools.Models;

internal enum DeveloperReleaseOperation
{
    PackStable,
    PackBeta,
    PublishLatest
}

internal sealed record DeveloperReleaseEnvironment(
    bool IsVisible,
    string ProjectRoot,
    string CurrentVersion,
    string PowerShellPath,
    bool IsGitHubReady,
    bool IsGiteeReady,
    bool IsBandizipReady,
    string OutputRoot,
    string ReleaseAssetRoot)
{
    public bool IsFullyConfigured =>
        !string.IsNullOrWhiteSpace(PowerShellPath) &&
        IsGitHubReady && IsGiteeReady && IsBandizipReady;

    public InfoBarSeverity ConfigurationSeverity =>
        IsFullyConfigured ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
}

internal sealed record DeveloperReleaseRunResult(
    bool Succeeded,
    int ExitCode,
    string Summary);
