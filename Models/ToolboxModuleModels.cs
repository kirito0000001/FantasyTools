namespace FantasyTools.Models;

internal enum ToolboxModuleKey
{
    Characters,
    HandCards,
    UnrealSync,
    Settings
}

internal enum ToolboxModuleCategory
{
    Production,
    Integration,
    Settings
}

internal sealed record ToolboxModuleDefinition(
    ToolboxModuleKey Key,
    string Tag,
    string DisplayName,
    ToolboxModuleCategory Category,
    string Description);
