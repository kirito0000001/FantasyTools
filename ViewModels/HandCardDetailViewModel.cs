using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using FantasyTools.Models;

namespace FantasyTools.ViewModels;

internal sealed class HandCardDetailViewModel : ObservableObject
{
    public static readonly IReadOnlyList<OptionItem> SuitOptions =
    [
        new("Hearts", "红桃"),
        new("Diamonds", "方片"),
        new("Clubs", "梅花"),
        new("Spade", "黑桃")
    ];

    public static readonly IReadOnlyList<OptionItem> CardTypeOptions =
    [
        new("Base", "基本牌"),
        new("Zdy", "事件牌"),
        new("Equip", "装备牌"),
        new("Judge", "共鸣牌")
    ];

    public static readonly IReadOnlyList<OptionItem> EquipTypeOptions =
    [
        new("Weapon", "武器"),
        new("Armor", "防具"),
        new("Prop", "道具")
    ];

    private HandCardInfo? _currentHandCard;
    private string _code = string.Empty;
    private string _codeEditText = string.Empty;
    private string _name = string.Empty;
    private string _cardFacePath = string.Empty;
    private string _description = string.Empty;
    private string _suit = "Hearts";
    private double _pokerNumber = 1;
    private string _cardType = "Base";
    private double _remainingUseCount = -1;
    private string _equipType = "Weapon";
    private double _value;
    private string _expression = string.Empty;
    private string _saveStatusText = "未打开手牌。";
    private string _lastUpdatedText = "上次修改：--";
    private string _noticeTitle = "手牌资料";
    private string _noticeMessage = "从手牌卡进入后，在这里维护手牌基础数据。";
    private bool _isNoticeOpen = true;
    private bool _isLoading;
    private bool _isDirty;

    public ObservableCollection<EditableTextEntry> FunctionGroups { get; } = [];

    public bool HasHandCard => _currentHandCard is not null;

    public bool IsDirty => _isDirty;

    public IReadOnlyList<OptionItem> Suits => SuitOptions;

    public IReadOnlyList<OptionItem> CardTypes => CardTypeOptions;

    public IReadOnlyList<OptionItem> EquipTypes => EquipTypeOptions;

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
        ? "未选择手牌"
        : $"手牌英文代号：{Code}";

    public string BreadcrumbText => string.IsNullOrWhiteSpace(Code)
        ? "手牌"
        : $"手牌 {Title} / {Code}";

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

    public string Suit
    {
        get => _suit;
        set
        {
            if (SetProperty(ref _suit, string.IsNullOrWhiteSpace(value) ? "Hearts" : value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public double PokerNumber
    {
        get => _pokerNumber;
        set
        {
            var normalized = double.IsNaN(value) ? 1 : Math.Clamp(Math.Round(value), 1, 13);
            if (SetProperty(ref _pokerNumber, normalized) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string CardType
    {
        get => _cardType;
        set
        {
            if (SetProperty(ref _cardType, string.IsNullOrWhiteSpace(value) ? "Base" : value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public double RemainingUseCount
    {
        get => _remainingUseCount;
        set
        {
            var normalized = double.IsNaN(value) ? -1 : Math.Round(value);
            if (SetProperty(ref _remainingUseCount, normalized) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string EquipType
    {
        get => _equipType;
        set
        {
            if (SetProperty(ref _equipType, string.IsNullOrWhiteSpace(value) ? "Weapon" : value) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public double Value
    {
        get => _value;
        set
        {
            var normalized = double.IsNaN(value) ? 0 : Math.Round(value);
            if (SetProperty(ref _value, normalized) && !_isLoading)
            {
                MarkEdited();
            }
        }
    }

    public string Expression
    {
        get => _expression;
        set
        {
            if (SetProperty(ref _expression, value) && !_isLoading)
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

    public void Load(HandCardInfo handCard)
    {
        _isLoading = true;
        try
        {
            _currentHandCard = handCard;
            Code = handCard.Code;
            CodeEditText = handCard.Code;
            Name = handCard.Meta.Name;
            CardFacePath = handCard.CardFacePath;
            Description = handCard.Meta.Description;
            Suit = handCard.Meta.Suit;
            PokerNumber = handCard.Meta.PokerNumber;
            CardType = handCard.Meta.CardType;
            RemainingUseCount = handCard.Meta.RemainingUseCount;
            EquipType = handCard.Meta.EquipType;
            Value = handCard.Meta.Value;
            Expression = handCard.Meta.Expression;
            ReplaceEntries(FunctionGroups, handCard.Meta.FunctionGroups);
            NoticeTitle = "手牌资料已打开";
            NoticeMessage = "修改会自动保存；花色、卡牌类型和装备类型按 Unreal 枚举原名落盘。";
            IsNoticeOpen = true;
            _isDirty = false;
            SaveStatusText = "修改会自动保存。";
            LastUpdatedText = BuildLastUpdatedText(handCard.Meta.UpdatedAt);
            NotifyHeaderChanged();
        }
        finally
        {
            _isLoading = false;
        }
    }

    public HandCardMeta BuildSnapshot()
    {
        var current = _currentHandCard?.Meta ?? new HandCardMeta();
        current.Code = Code;
        current.Name = string.IsNullOrWhiteSpace(Name) ? Code : Name.Trim();
        current.CardFaceFileName = current.CardFaceFileName;
        current.Description = Description;
        current.Suit = Suit;
        current.PokerNumber = Math.Clamp((int)Math.Round(PokerNumber), 1, 13);
        current.CardType = CardType;
        current.FunctionGroups = NormalizeEntries(FunctionGroups);
        current.RemainingUseCount = (int)Math.Round(RemainingUseCount);
        current.EquipType = EquipType;
        current.Value = (int)Math.Round(Value);
        current.Expression = Expression;
        return current;
    }

    public void ApplySavedHandCard(HandCardInfo handCard)
    {
        _currentHandCard = handCard;
        Code = handCard.Code;
        CodeEditText = handCard.Code;
        Name = handCard.Meta.Name;
        CardFacePath = handCard.CardFacePath;
        _isDirty = false;
        SaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        LastUpdatedText = BuildLastUpdatedText(handCard.Meta.UpdatedAt);
        NotifyHeaderChanged();
    }

    public void ApplyImportedCardFace(HandCardInfo handCard)
    {
        _currentHandCard = handCard;
        CardFacePath = handCard.CardFacePath;
        SaveStatusText = $"已保存：{DateTime.Now:HH:mm:ss}";
        LastUpdatedText = BuildLastUpdatedText(handCard.Meta.UpdatedAt);
    }

    public void ApplyRenamedHandCard(HandCardInfo handCard)
    {
        Load(handCard);
    }

    public void AddFunctionGroup()
    {
        FunctionGroups.Add(CreateEntry(string.Empty));
        MarkEdited();
    }

    public void RemoveFunctionGroup(EditableTextEntry entry)
    {
        if (FunctionGroups.Remove(entry))
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
        OnPropertyChanged(nameof(HasHandCard));
    }

    private static string BuildLastUpdatedText(DateTimeOffset updatedAt)
    {
        return $"上次修改：{updatedAt.LocalDateTime:yyyy-MM-dd HH:mm}";
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

    private static List<string> NormalizeEntries(ObservableCollection<EditableTextEntry> entries)
    {
        return entries
            .Select(entry => entry.Value.Trim())
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal sealed record OptionItem(string Value, string DisplayName);
