using System.Collections.Generic;
using FantasyTools.Models;

namespace FantasyTools.ViewModels;

internal sealed class ApplicationViewModel : ObservableObject
{
    private ToolboxModuleKey _selectedModule = ToolboxModuleKey.Characters;

    public ApplicationViewModel(
        SettingsViewModel settings,
        GlobalProgressViewModel globalProgress,
        CharactersViewModel characters,
        CharacterDetailViewModel characterDetail,
        HandCardsViewModel handCards,
        HandCardDetailViewModel handCardDetail)
    {
        Settings = settings;
        GlobalProgress = globalProgress;
        Characters = characters;
        CharacterDetail = characterDetail;
        HandCards = handCards;
        HandCardDetail = handCardDetail;
    }

    public SettingsViewModel Settings { get; }

    public GlobalProgressViewModel GlobalProgress { get; }

    public CharactersViewModel Characters { get; }

    public CharacterDetailViewModel CharacterDetail { get; }

    public HandCardsViewModel HandCards { get; }

    public HandCardDetailViewModel HandCardDetail { get; }

    public IReadOnlyList<ToolboxModuleDefinition> Modules { get; } =
    [
        new(ToolboxModuleKey.Characters, "Characters", "角色", ToolboxModuleCategory.Production, "武将与角色卡的资料、立绘、技能和制作状态入口"),
        new(ToolboxModuleKey.HandCards, "HandCards", "手牌", ToolboxModuleCategory.Production, "基础牌、锦囊牌、装备牌与卡面素材的制作入口"),
        new(ToolboxModuleKey.UnrealSync, "UnrealSync", "虚幻同步台", ToolboxModuleCategory.Integration, "连接 FantasyProject，检查路径并准备同步卡牌/角色数据"),
        new(ToolboxModuleKey.Settings, "Settings", "整体设置", ToolboxModuleCategory.Settings, "全局路径、显示偏好和工具箱运行设置")
    ];

    public ToolboxModuleKey SelectedModule
    {
        get => _selectedModule;
        set => SetProperty(ref _selectedModule, value);
    }

    public ToolboxModuleDefinition? FindModuleByTag(string tag)
    {
        foreach (var module in Modules)
        {
            if (string.Equals(module.Tag, tag, System.StringComparison.Ordinal))
            {
                return module;
            }
        }

        return null;
    }
}
