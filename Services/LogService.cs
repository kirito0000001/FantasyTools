using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using FantasyTools.Models;

namespace FantasyTools.Services;

internal sealed class LogService
{
    private const string LogCategory = "LogFantasyTools";
    private const int MaxLogFileCount = 30;
    private string? _currentLogFilePath;

    public ObservableCollection<LogEntry> Entries { get; } = [];

    public void Append(AppSettings settings, LogVerbosity verbosity, string message)
    {
        if (!ShouldWrite(settings, verbosity))
        {
            return;
        }

        var entry = new LogEntry(DateTimeOffset.Now, LogCategory, verbosity, message);
        Entries.Add(entry);

        if (settings.LogSaveToFileEnabled && !string.IsNullOrWhiteSpace(settings.ProjectRootPath))
        {
            AppendToFile(settings.ProjectRootPath, entry.Text);
        }
    }

    public void Clear(AppSettings settings)
    {
        Entries.Clear();
        Append(settings, LogVerbosity.Display, "已清空输出日志。");
    }

    private static bool ShouldWrite(AppSettings settings, LogVerbosity verbosity)
    {
        if (!settings.LogEnabled)
        {
            return false;
        }

        return verbosity switch
        {
            LogVerbosity.Display => settings.LogUserOperations,
            LogVerbosity.Warning => settings.LogWarnings,
            LogVerbosity.Error => settings.LogErrors,
            _ => true
        };
    }

    private void AppendToFile(string projectRootPath, string text)
    {
        var logsDirectory = Path.Combine(projectRootPath, "Logs");
        Directory.CreateDirectory(logsDirectory);

        _currentLogFilePath ??= Path.Combine(logsDirectory, $"{LogCategory}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        File.AppendAllText(_currentLogFilePath, text + Environment.NewLine);
        PruneOldLogs(logsDirectory);
    }

    private static void PruneOldLogs(string logsDirectory)
    {
        var logFiles = Directory
            .EnumerateFiles(logsDirectory, $"{LogCategory}-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaxLogFileCount);

        foreach (var file in logFiles)
        {
            file.Delete();
        }
    }
}
