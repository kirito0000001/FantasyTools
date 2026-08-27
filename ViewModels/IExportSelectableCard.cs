namespace FantasyTools.ViewModels;

internal interface IExportSelectableCard
{
    bool IsAddCard { get; }

    string Code { get; }

    bool IsExportSelectionVisible { get; set; }

    bool IsExportSelected { get; set; }
}
