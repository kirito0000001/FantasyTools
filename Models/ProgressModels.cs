namespace FantasyTools.Models;

internal sealed record ProgressUpdate(
    string Message,
    double Percent,
    string? Detail = null,
    bool IsIndeterminate = false);

internal sealed record MigrationResult(
    int FileCount,
    int DirectoryCount,
    bool OldDirectoryDeleted = false,
    string? CleanupError = null);
