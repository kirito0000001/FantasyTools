using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class AppSettingsService
{
    public const string ToolboxStableKey = "FantasyTools";
    public const string ProjectRootFolderName = "幻杀工具箱项目";
    public const string SettingsFileName = "FantasyTools.settings.json";
    public const string BootstrapFileName = "bootstrap.json";
    public const string DefaultProjectRootPath = @"D:\幻杀工具箱项目";

    public string BootstrapDirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ToolboxStableKey);

    public string BootstrapFilePath => Path.Combine(BootstrapDirectoryPath, BootstrapFileName);

    public AppSettings Load()
    {
        var projectRootPath = ResolveProjectRootPathFromBootstrap();
        EnsureProjectRootDirectory(projectRootPath);
        var settingsFilePath = GetSettingsFilePath(projectRootPath);

        if (!File.Exists(settingsFilePath))
        {
            var settings = new AppSettings { ProjectRootPath = projectRootPath };
            Save(settings);
            return settings;
        }

        try
        {
            var settingsJson = File.ReadAllText(settingsFilePath);
            var settings = JsonSerializer.Deserialize(settingsJson, AppJsonSerializerContext.Default.AppSettings) ?? new AppSettings();
            settings.ProjectRootPath = string.IsNullOrWhiteSpace(settings.ProjectRootPath)
                ? projectRootPath
                : settings.ProjectRootPath;
            WriteBootstrap(settings.ProjectRootPath);
            return settings;
        }
        catch
        {
            BackupBrokenSettings(settingsFilePath);
            var settings = new AppSettings { ProjectRootPath = projectRootPath };
            Save(settings);
            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        settings.ProjectRootPath = ResolveProjectRootPath(settings);
        EnsureProjectRootDirectory(settings.ProjectRootPath);
        WriteBootstrap(settings.ProjectRootPath);

        var settingsFilePath = GetSettingsFilePath(settings.ProjectRootPath);
        var settingsJson = JsonSerializer.Serialize(settings, AppJsonSerializerContext.Default.AppSettings);
        File.WriteAllText(settingsFilePath, settingsJson);
    }

    public string ResolveProjectRootPath(AppSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.ProjectRootPath)
            ? DefaultProjectRootPath
            : settings.ProjectRootPath;
    }

    public string GetSettingsFilePath(string projectRootPath)
    {
        return Path.Combine(projectRootPath, SettingsFileName);
    }

    public string GetLogsDirectoryPath(string projectRootPath)
    {
        return Path.Combine(projectRootPath, "Logs");
    }

    public string GetBackupsDirectoryPath(string projectRootPath)
    {
        return Path.Combine(projectRootPath, "Backups");
    }

    public void EnsureProjectRootDirectory(string projectRootPath)
    {
        Directory.CreateDirectory(projectRootPath);
        Directory.CreateDirectory(GetBackupsDirectoryPath(projectRootPath));
    }

    public string BuildProjectRootPathFromParent(string parentPath)
    {
        return Path.GetFullPath(Path.Combine(parentPath, ProjectRootFolderName));
    }

    private string ResolveProjectRootPathFromBootstrap()
    {
        if (!File.Exists(BootstrapFilePath))
        {
            return DefaultProjectRootPath;
        }

        try
        {
            var json = File.ReadAllText(BootstrapFilePath);
            var bootstrap = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettingsBootstrap);
            return string.IsNullOrWhiteSpace(bootstrap?.ProjectRootPath)
                ? DefaultProjectRootPath
                : bootstrap.ProjectRootPath;
        }
        catch
        {
            return DefaultProjectRootPath;
        }
    }

    private void WriteBootstrap(string projectRootPath)
    {
        Directory.CreateDirectory(BootstrapDirectoryPath);
        var json = JsonSerializer.Serialize(
            new AppSettingsBootstrap(projectRootPath),
            AppJsonSerializerContext.Default.AppSettingsBootstrap);
        File.WriteAllText(BootstrapFilePath, json);
    }

    private static void BackupBrokenSettings(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return;
        }

        var backupPath = $"{settingsFilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(settingsFilePath, backupPath, overwrite: true);
    }
}

internal sealed record AppSettingsBootstrap(
    [property: JsonPropertyName("projectRootPath")] string ProjectRootPath);
