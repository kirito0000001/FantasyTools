using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using FantasyTools.Models;
using FantasyTools.Services;

namespace FantasyTools.ViewModels;

internal sealed class CharacterDetailViewModel : ObservableObject
{
    private CharacterInfo? _currentCharacter;
    private string _code = string.Empty;
    private string _codeEditText = string.Empty;
    private string _name = string.Empty;
    private string _cardFacePath = string.Empty;
    private string _description = string.Empty;
    private double _health = 30;
    private double _phase = 1;
    private string _saveStatusText = "未打开角色。";
    private string _lastUpdatedText = "上次修改：--";
    private string _noticeTitle = "角色资料";
    private string _noticeMessage = "从角色卡进入后，在这里维护角色基础数据。";
    private bool _isNoticeOpen = true;
    private bool _isLoading;
    private bool _isDirty;

    public ObservableCollection<EditableTextEntry> Tags { get; } = [];

    public ObservableCollection<EditableTextEntry> SkillGroups { get; } = [];

    public ObservableCollection<EditableSkillEntry> Skills { get; } = [];

    public ObservableCollection<EditableTextEntry> CarryCards { get; } = [];

    public bool HasCharacter => _currentCharacter is not null;

    public bool IsDirty => _isDirty;

    public string Code
    {
        get => _code;
        private set => SetProperty(ref _code, value);
    }

    public string CodeEditText
    {
        get => _codeEditText;
        set => SetProperty(ref _codeEditText, value);
    }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string Title => string.IsNullOrWhiteSpace(Name) ? Code : Name;

    public string Subtitle => string.IsNullOrWhiteSpace(Code)
        ? "未选择角色"
        : $"角色英文代号：{Code}";

    public string BreadcrumbText => string.IsNullOrWhiteSpace(Code)
        ? "角色"
        : $"角色 {Title} / {Code}";

    public string CardFacePath
    {
        get => _cardFacePath;
        private set => SetProperty(ref _cardFacePath, value);
    }

    public string Description
    {
        get => _description;
        set
        {
            if (SetProperty(ref _description, value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public double Health
    {
        get => _health;
        set
        {
            var normalized = double.IsNaN(value) ? 1 : Math.Max(1, Math.Round(value));
            if (SetProperty(ref _health, normalized) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public double Phase
    {
        get => _phase;
        set
        {
            var normalized = double.IsNaN(value) ? 1 : Math.Max(1, Math.Round(value));
            if (SetProperty(ref _phase, normalized) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string SaveStatusText
    {
        get => _saveStatusText;
        private set => SetProperty(ref _saveStatusText, value);
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => SetProperty(ref _lastUpdatedText, value);
    }

    public string NoticeTitle
    {
        get => _noticeTitle;
        private set => SetProperty(ref _noticeTitle, value);
    }

    public string NoticeMessage
    {
        get => _noticeMessage;
        private set => SetProperty(ref _noticeMessage, value);
    }

    public bool IsNoticeOpen
    {
        get => _isNoticeOpen;
        private set => SetProperty(ref _isNoticeOpen, value);
    }

    public void Load(CharacterInfo character)
    {
        _isLoading = true;
        try
        {
            _currentCharacter = character;
            Code = character.Code;
            CodeEditText = character.Code;
            Name = character.Meta.Name;
            CardFacePath = character.CardFacePath;
            Description = character.Meta.Description;
            Health = character.Meta.Health;
            Phase = ParsePhase(character.Meta.Phase);
            ReplaceEntries(Tags, character.Meta.Tags);
            ReplaceEntries(SkillGroups, character.Meta.SkillGroups);
            ReplaceSkills(character.Meta.Skills);
            ReplaceEntries(CarryCards, character.Meta.CarryCards);
            NoticeTitle = "角色资料已打开";
            NoticeMessage = "修改会自动保存；技能英文代号按角色英文代号和顺序自动生成。";
            IsNoticeOpen = true;
            _isDirty = false;
            SaveStatusText = "修改会自动保存。";
            LastUpdatedText = BuildLastUpdatedText(character.Meta.UpdatedAt);
            NotifyHeaderChanged();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public CharacterMeta BuildSnapshot()
    {
        var current = _currentCharacter?.Meta ?? new CharacterMeta();
        current.Code = Code;
        current.Name = string.IsNullOrWhiteSpace(Name) ? Code : Name.Trim();
        current.CardFaceFileName = current.CardFaceFileName;
        current.Description = Description;
        current.Health = Math.Max(1, (int)Math.Round(Health));
        current.Phase = Math.Max(1, (int)Math.Round(Phase)).ToString(CultureInfo.InvariantCulture);
        current.Tags = NormalizeEntries(Tags);
        current.SkillGroups = NormalizeEntries(SkillGroups);
        current.Skills = NormalizeSkills(Skills, Code);
        current.CarryCards = NormalizeEntries(CarryCards);
        return current;
    }

    public void ApplySavedCharacter(CharacterInfo character)
    {
        _currentCharacter = character;
        Code = character.Code;
        CodeEditText = character.Code;
        Name = character.Meta.Name;
        CardFacePath = character.CardFacePath;
        _isDirty = false;
        SaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        LastUpdatedText = BuildLastUpdatedText(character.Meta.UpdatedAt);
        NotifyHeaderChanged();
    }

    public void ApplyImportedCardFace(CharacterInfo character)
    {
        _currentCharacter = character;
        CardFacePath = character.CardFacePath;
        SaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        LastUpdatedText = BuildLastUpdatedText(character.Meta.UpdatedAt);
    }

    public void ApplyRenamedCharacter(CharacterInfo character)
    {
        Load(character);
    }

    public void AddTag()
    {
        Tags.Add(CreateEntry(string.Empty));
        MarkEdited();
    }

    public void RemoveTag(EditableTextEntry entry)
    {
        if (Tags.Remove(entry))
        {
            MarkEdited();
        }
    }

    public void AddSkillGroup()
    {
        SkillGroups.Add(CreateEntry(string.Empty));
        MarkEdited();
    }

    public void RemoveSkillGroup(EditableTextEntry entry)
    {
        if (SkillGroups.Remove(entry))
        {
            MarkEdited();
        }
    }

    public void AddSkill()
    {
        Skills.Add(CreateSkill(new CharacterSkillMeta(), Skills.Count));
        RenumberSkills();
        MarkEdited();
    }

    public void RemoveSkill(EditableSkillEntry entry)
    {
        if (Skills.Remove(entry))
        {
            RenumberSkills();
            MarkEdited();
        }
    }

    public void AddCarryCard()
    {
        CarryCards.Add(CreateEntry(string.Empty));
        MarkEdited();
    }

    public void RemoveCarryCard(EditableTextEntry entry)
    {
        if (CarryCards.Remove(entry))
        {
            MarkEdited();
        }
    }

    public void NotifyEntryEdited()
    {
        if (!_isLoading)
        {
            MarkEdited();
        }
    }

    private void MarkEdited()
    {
        _isDirty = true;
        SaveStatusText = "有修改，等待自动保存...";
        NotifyHeaderChanged();
    }

    private void NotifyHeaderChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(BreadcrumbText));
        OnPropertyChanged(nameof(HasCharacter));
    }

    private static string BuildLastUpdatedText(DateTimeOffset updatedAt)
    {
        return $"上次修改：{updatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";
    }

    private static double ParsePhase(string value)
    {
        return double.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var phase)
            ? Math.Max(1, Math.Round(phase))
            : 1;
    }

    private EditableTextEntry CreateEntry(string value)
    {
        var entry = new EditableTextEntry(value);
        entry.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditableTextEntry.Value))
            {
                NotifyEntryEdited();
            }
        };
        return entry;
    }

    private void ReplaceEntries(ObservableCollection<EditableTextEntry> target, List<string> entries)
    {
        target.Clear();
        foreach (var entry in entries.DefaultIfEmpty(string.Empty))
        {
            target.Add(CreateEntry(entry));
        }
    }

    private void ReplaceSkills(List<CharacterSkillMeta> skills)
    {
        Skills.Clear();
        foreach (var skill in skills)
        {
            Skills.Add(CreateSkill(skill, Skills.Count));
        }
    }

    private EditableSkillEntry CreateSkill(CharacterSkillMeta skill, int index)
    {
        var entry = new EditableSkillEntry(
            skill.Name,
            CharacterWorkspaceService.BuildSkillCode(Code, index),
            skill.Description,
            skill.Function,
            skill.Type);
        entry.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(EditableSkillEntry.Name) or
                nameof(EditableSkillEntry.Description) or
                nameof(EditableSkillEntry.Function) or
                nameof(EditableSkillEntry.Type))
            {
                NotifyEntryEdited();
            }
        };
        return entry;
    }

    private void RenumberSkills()
    {
        for (var index = 0; index < Skills.Count; index++)
        {
            Skills[index].Code = CharacterWorkspaceService.BuildSkillCode(Code, index);
        }
    }

    private static List<string> NormalizeEntries(ObservableCollection<EditableTextEntry> entries)
    {
        return entries
            .Select(entry => entry.Value.Trim())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<CharacterSkillMeta> NormalizeSkills(ObservableCollection<EditableSkillEntry> skills, string characterCode)
    {
        return skills
            .Where(skill =>
                !string.IsNullOrWhiteSpace(skill.Name) ||
                !string.IsNullOrWhiteSpace(skill.Description) ||
                !string.IsNullOrWhiteSpace(skill.Function) ||
                !string.IsNullOrWhiteSpace(skill.Type))
            .Select((skill, index) => new CharacterSkillMeta
            {
                Name = skill.Name.Trim(),
                Code = CharacterWorkspaceService.BuildSkillCode(characterCode, index),
                Description = skill.Description.Trim(),
                Function = skill.Function.Trim(),
                Type = skill.Type.Trim()
            })
            .ToList();
    }
}

internal sealed class EditableTextEntry : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _value;

    public EditableTextEntry(string value)
    {
        _value = value;
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

internal sealed class EditableSkillEntry : ObservableObject
{
    public Guid Id { get; } = Guid.NewGuid();

    private string _name;
    private string _code;
    private string _description;
    private string _function;
    private string _type;

    public EditableSkillEntry(string name, string code, string description, string function, string type)
    {
        _name = name;
        _code = code;
        _description = description;
        _function = function;
        _type = type;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Code
    {
        get => _code;
        set => SetProperty(ref _code, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string Function
    {
        get => _function;
        set => SetProperty(ref _function, value);
    }

    public string Type
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }
}
