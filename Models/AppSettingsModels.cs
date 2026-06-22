using System;

namespace FantasyTools.Models;

internal enum ThemePreference
{
    Light,
    System,
    Dark
}

internal enum UpdateChannel
{
    Stable,
    Beta
}

internal sealed class AppSettings
{
    public string? ProjectRootPath { get; set; }

    public string? UnrealEnginePath { get; set; }

    public string? UnrealProjectPath { get; set; }

    public ThemePreference ThemePreference { get; set; } = ThemePreference.Light;

    public bool ShowWorkspacePath { get; set; }

    public bool ShowCurrentModule { get; set; }

    public bool LogEnabled { get; set; }

    public bool LogSaveToFileEnabled { get; set; }

    public bool LogUserOperations { get; set; } = true;

    public bool LogWarnings { get; set; } = true;

    public bool LogErrors { get; set; } = true;

    public int SmallFileBackupLimit { get; set; } = 60;

    public int WorkUnitBackupLimit { get; set; } = 20;

    public int MediumAssetAutoBackupLimit { get; set; } = 5;

    public int UnrealSyncBackupLimit { get; set; } = 2;

    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;

    public bool UpdateAutoCheckEnabled { get; set; } = true;

    public bool UpdateCheckOnStartup { get; set; } = true;

    public int UpdateConnectionTimeoutSeconds { get; set; } = 120;

    public DateTimeOffset? UpdateLastCheckAt { get; set; }

    public string? UpdateLastStatus { get; set; }

    public string UpdateReleaseApiUrl { get; set; } = "https://api.github.com/repos/kirito0000001/FantasyTools/releases";

    public string UpdateReleasePageUrl { get; set; } = "https://github.com/kirito0000001/FantasyTools/releases";
}
