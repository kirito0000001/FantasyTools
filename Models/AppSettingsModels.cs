namespace FantasyTools.Models;

internal enum ThemePreference
{
    Light,
    System,
    Dark
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
}
