using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Inspecto;

public static class Sheets
{
    public static readonly ExcelSheet<Item> ItemSheet;
    public static readonly ExcelSheet<Title> TitleSheet;
    public static readonly ExcelSheet<ClassJob> ClassJobSheet;

    static Sheets()
    {
        ItemSheet = Plugin.Data.GetExcelSheet<Item>();
        TitleSheet = Plugin.Data.GetExcelSheet<Title>();
        ClassJobSheet = Plugin.Data.GetExcelSheet<ClassJob>();
    }

    public static bool TryGetItem(uint itemId, out Item itemRow) => ItemSheet.TryGetRow(Utils.NormalizeItemId(itemId), out itemRow);
}
