using System.Collections.ObjectModel;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FantasyTools.Models;
using FantasyTools.Services;
using Microsoft.UI.Xaml.Controls;

namespace FantasyTools.ViewModels;

internal sealed class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _settingsService;
    private readonly LogService _logService;
    private readonly ProjectRootMigrationService _migrationService;
    private AppSettings _settings = new();
    private string _projectRootPath = AppSettingsService.DefaultProjectRootPath;
    private string _projectRootStatusTitle = "目录已就绪";
    private string _projectRootStatusMessage = string.Empty;
    private InfoBarSeverity _projectRootStatusSeverity = InfoBarSeverity.Success;
    private ThemePreference _themePreference = ThemePreference.Light;
    private bool _showWorkspacePath;
    private bool _showCurrentModule;
    private bool _logEnabled;
    private bool _logSaveToFileEnabled;
    private bool _logUserOperations = true;
    private bool _logWarnings = true;
    private bool _logErrors = true;
    private UpdateChannel _updateChannel = UpdateChannel.Stable;
    private bool _updateAutoCheckEnabled = true;
    private bool _updateCheckOnStartup = true;
    private double _updateConnectionTimeoutSeconds = 120;
    private bool _isLoadingSettings;

    public SettingsViewModel(
        AppSettingsService settingsService,
        LogService logService,
        ProjectRootMigrationService migrationService)
    {
        _settingsService = settingsService;
        _logService = logService;
        _migrationService = migrationService;
    }

    public string ProjectRootPath
    {
        get => _projectRootPath;
        private set
        {
            if (SetProperty(ref _projectRootPath, value))
            {
                OnPropertyChanged(nameof(WorkspaceStatusText));
                OnPropertyChanged(nameof(SettingsFilePath));
                OnPropertyChanged(nameof(LogDirectoryPath));
                OnPropertyChanged(nameof(BackupDirectoryPath));
            }
        }
    }

    public string WorkspaceStatusText => ShowWorkspacePath
        ? $"整体项目位置：{ProjectRootPath}"
        : "整体项目位置已隐藏";

    public string SettingsFilePath => _settingsService.GetSettingsFilePath(ProjectRootPath);

    public string LogDirectoryPath => _settingsService.GetLogsDirectoryPath(ProjectRootPath);

    public string BackupDirectoryPath => _settingsService.GetBackupsDirectoryPath(ProjectRootPath);

    public ObservableCollection<LogEntry> LogEntries => _logService.Entries;

    public ThemePreference ThemePreference
    {
        get => _themePreference;
        set => SetSettingProperty(ref _themePreference, value, nameof(ThemePreference), () => _settings.ThemePreference = value);
    }

    public bool ShowWorkspacePath
    {
        get => _showWorkspacePath;
        set => SetSettingProperty(ref _showWorkspacePath, value, nameof(ShowWorkspacePath), () => _settings.ShowWorkspacePath = value, nameof(WorkspaceStatusText));
    }

    public bool ShowCurrentModule
    {
        get => _showCurrentModule;
        set => SetSettingProperty(ref _showCurrentModule, value, nameof(ShowCurrentModule), () => _settings.ShowCurrentModule = value);
    }

    public bool LogEnabled
    {
        get => _logEnabled;
        set => SetSettingProperty(ref _logEnabled, value, nameof(LogEnabled), () => _settings.LogEnabled = value, nameof(IsLogOptionsEnabled));
    }

    public bool LogSaveToFileEnabled
    {
        get => _logSaveToFileEnabled;
        set => SetSettingProperty(ref _logSaveToFileEnabled, value, nameof(LogSaveToFileEnabled), () => _settings.LogSaveToFileEnabled = value);
    }

    public bool LogUserOperations
    {
        get => _logUserOperations;
        set => SetSettingProperty(ref _logUserOperations, value, nameof(LogUserOperations), () => _settings.LogUserOperations = value);
    }

    public bool LogWarnings
    {
        get => _logWarnings;
        set => SetSettingProperty(ref _logWarnings, value, nameof(LogWarnings), () => _settings.LogWarnings = value);
    }

    public bool LogErrors
    {
        get => _logErrors;
        set => SetSettingProperty(ref _logErrors, value, nameof(LogErrors), () => _settings.LogErrors = value);
    }

    public bool IsLogOptionsEnabled => LogEnabled;

    public UpdateChannel UpdateChannel
    {
        get => _updateChannel;
        set => SetSettingProperty(ref _updateChannel, value, nameof(UpdateChannel), () => _settings.UpdateChannel = value, nameof(UpdateChannelText));
    }

    public string UpdateChannelText => UpdateChannel == UpdateChannel.Beta ? "测试版 / prerelease" : "稳定版 / Release";

    public bool UpdateAutoCheckEnabled
    {
        get => _updateAutoCheckEnabled;
        set => SetSettingProperty(ref _updateAutoCheckEnabled, value, nameof(UpdateAutoCheckEnabled), () => _settings.UpdateAutoCheckEnabled = value);
    }

    public bool UpdateCheckOnStartup
    {
        get => _updateCheckOnStartup;
        set => SetSettingProperty(ref _updateCheckOnStartup, value, nameof(UpdateCheckOnStartup), () => _settings.UpdateCheckOnStartup = value);
    }

    public double UpdateConnectionTimeoutSeconds
    {
        get => _updateConnectionTimeoutSeconds;
        set
        {
            var normalized = Math.Clamp(double.IsNaN(value) ? 120 : Math.Round(value), 10, 600);
            SetSettingProperty(
                ref _updateConnectionTimeoutSeconds,
                normalized,
                nameof(UpdateConnectionTimeoutSeconds),
                () => _settings.UpdateConnectionTimeoutSeconds = (int)normalized,
                nameof(UpdateConnectionTimeoutText));
        }
    }

    public int UpdateConnectionTimeoutSecondsValue => (int)Math.Clamp(Math.Round(UpdateConnectionTimeoutSeconds), 10, 600);

    public string UpdateConnectionTimeoutText => $"最长连接时间：{UpdateConnectionTimeoutSecondsValue} 秒";

    public string UpdateReleaseApiUrl => _settings.UpdateReleaseApiUrl;

    public string UpdateReleasePageUrl => _settings.UpdateReleasePageUrl;

    public string UpdateLastCheckText => _settings.UpdateLastCheckAt is null
        ? "最近检查：从未检查"
        : $"最近检查：{_settings.UpdateLastCheckAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}";

    public string UpdateLastStatus => string.IsNullOrWhiteSpace(_settings.UpdateLastStatus)
        ? "更新状态：尚未检查。"
        : _settings.UpdateLastStatus;

    public string UnrealEnginePath
    {
        get => _settings.UnrealEnginePath ?? string.Empty;
        set
        {
            if (_settings.UnrealEnginePath == value)
            {
                return;
            }

            _settings.UnrealEnginePath = value;
            Save();
            AppendLog(LogVerbosity.Display, "已更新 Unreal Engine 路径。");
            OnPropertyChanged();
        }
    }

    public string UnrealProjectPath
    {
        get => _settings.UnrealProjectPath ?? string.Empty;
        set
        {
            if (_settings.UnrealProjectPath == value)
            {
                return;
            }

            _settings.UnrealProjectPath = value;
            Save();
            AppendLog(LogVerbosity.Display, "已更新 Unreal 项目路径。");
            OnPropertyChanged();
        }
    }

    public string ProjectRootStatusTitle
    {
        get => _projectRootStatusTitle;
        private set => SetProperty(ref _projectRootStatusTitle, value);
    }

    public string ProjectRootStatusMessage
    {
        get => _projectRootStatusMessage;
        private set => SetProperty(ref _projectRootStatusMessage, value);
    }

    public InfoBarSeverity ProjectRootStatusSeverity
    {
        get => _projectRootStatusSeverity;
        private set => SetProperty(ref _projectRootStatusSeverity, value);
    }

    public void LoadAndEnsureProjectRoot()
    {
        _settings = _settingsService.Load();
        ProjectRootPath = _settingsService.ResolveProjectRootPath(_settings);

        _isLoadingSettings = true;
        try
        {
            ThemePreference = _settings.ThemePreference;
            ShowWorkspacePath = _settings.ShowWorkspacePath;
            ShowCurrentModule = _settings.ShowCurrentModule;
            LogEnabled = _settings.LogEnabled;
            LogSaveToFileEnabled = _settings.LogSaveToFileEnabled;
            LogUserOperations = _settings.LogUserOperations;
            LogWarnings = _settings.LogWarnings;
            LogErrors = _settings.LogErrors;
            UpdateChannel = _settings.UpdateChannel;
            UpdateAutoCheckEnabled = _settings.UpdateAutoCheckEnabled;
            UpdateCheckOnStartup = _settings.UpdateCheckOnStartup;
            UpdateConnectionTimeoutSeconds = _settings.UpdateConnectionTimeoutSeconds;
        }
        finally
        {
            _isLoadingSettings = false;
        }

        EnsureCurrentProjectRoot();
        AppendLog(LogVerbosity.Log, "程序启动，已检查整体项目目录。");
        OnPropertyChanged(nameof(UnrealEnginePath));
        OnPropertyChanged(nameof(UnrealProjectPath));
        OnPropertyChanged(nameof(UpdateLastCheckText));
        OnPropertyChanged(nameof(UpdateLastStatus));
    }

    public string BuildProjectRootPathFromParent(string parentPath)
    {
        return _settingsService.BuildProjectRootPathFromParent(parentPath);
    }

    public bool IsCurrentProjectRoot(string candidateProjectRootPath)
    {
        return ProjectRootMigrationService.PathsEqual(ProjectRootPath, candidateProjectRootPath);
    }

    public bool IsCandidateInsideCurrentRoot(string candidateProjectRootPath)
    {
        return ProjectRootMigrationService.IsPathInsideDirectory(candidateProjectRootPath, ProjectRootPath);
    }

    public bool IsCurrentProjectRootInsideCandidate(string candidateProjectRootPath)
    {
        return ProjectRootMigrationService.IsPathInsideDirectory(ProjectRootPath, candidateProjectRootPath);
    }

    public async Task<MigrationResult> ChangeProjectRootAsync(
        string newProjectRootPath,
        IProgress<ProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var oldProjectRootPath = Path.GetFullPath(ProjectRootPath);
        var normalizedNewProjectRootPath = Path.GetFullPath(newProjectRootPath);
        var result = await Task.Run(
            () => _migrationService.Migrate(oldProjectRootPath, normalizedNewProjectRootPath, progress, cancellationToken),
            cancellationToken);

        ProjectRootPath = normalizedNewProjectRootPath;
        _settings.ProjectRootPath = ProjectRootPath;
        Save();
        var oldDirectoryDeleted = _migrationService.TryDeleteOldProjectRoot(
            oldProjectRootPath,
            progress,
            out var cleanupError);
        EnsureCurrentProjectRoot();
        AppendLog(LogVerbosity.Display, $"已迁移整体项目位置：{oldProjectRootPath} -> {ProjectRootPath}");
        if (!oldDirectoryDeleted)
        {
            AppendLog(LogVerbosity.Warning, $"整体项目位置已迁移，但旧目录清理失败：{oldProjectRootPath}；{cleanupError}");
        }

        return result with
        {
            OldDirectoryDeleted = oldDirectoryDeleted,
            CleanupError = cleanupError
        };
    }

    public void EnsureCurrentProjectRoot()
    {
        _settingsService.EnsureProjectRootDirectory(ProjectRootPath);
        SetProjectRootStatus(InfoBarSeverity.Success, "目录已就绪", $"已确认目录存在：{ProjectRootPath}");
    }

    public void RestoreRecommendedDefaults()
    {
        ThemePreference = ThemePreference.Light;
        ShowWorkspacePath = false;
        ShowCurrentModule = false;
        LogEnabled = false;
        LogSaveToFileEnabled = false;
        LogUserOperations = true;
        LogWarnings = true;
        LogErrors = true;
        UpdateChannel = UpdateChannel.Stable;
        UpdateAutoCheckEnabled = true;
        UpdateCheckOnStartup = true;
        UpdateConnectionTimeoutSeconds = 120;
        AppendLog(LogVerbosity.Display, "已恢复整体设置推荐值。");
    }

    public void ClearLog()
    {
        _logService.Clear(_settings);
    }

    public void AppendLog(LogVerbosity verbosity, string message)
    {
        _logService.Append(_settings, verbosity, message);
    }

    public void SetProjectRootStatus(InfoBarSeverity severity, string title, string message)
    {
        ProjectRootStatusSeverity = severity;
        ProjectRootStatusTitle = title;
        ProjectRootStatusMessage = message;
    }

    public void SetUpdateStatus(string message)
    {
        _settings.UpdateLastCheckAt = DateTimeOffset.Now;
        _settings.UpdateLastStatus = message;
        Save();
        OnPropertyChanged(nameof(UpdateLastCheckText));
        OnPropertyChanged(nameof(UpdateLastStatus));
    }

    private bool SetSettingProperty<T>(ref T field, T value, string propertyName, System.Action updateSettings, string? dependentPropertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        updateSettings();
        if (!_isLoadingSettings)
        {
            Save();
        }

        if (dependentPropertyName is not null)
        {
            OnPropertyChanged(dependentPropertyName);
        }

        return true;
    }

    private void Save()
    {
        _settingsService.Save(_settings);
    }
}
