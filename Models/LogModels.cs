using System;

namespace FantasyTools.Models;

internal enum LogVerbosity
{
    Display,
    Log,
    Warning,
    Error
}

internal sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Category,
    LogVerbosity Verbosity,
    string Message)
{
    public string Text => $"[{Timestamp:HH:mm:ss}] {Category}: {Verbosity}: {Message}";
}
