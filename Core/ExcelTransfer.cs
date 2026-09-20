using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Profit.Core;

public static class ExcelTransfer
{
    static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void ExportLedgerMonth(Ledger ledger, Month month, string file)
    {
        var calculation = LedgerCalculator.Calculate(ledger);
        var total = LedgerCalculator.SummarizeMonth(ledger, month, calculation);
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<object?[]>
        {
            ["تاریخ", "کد کالا", "برند", "کالا", "مشتری", "تعداد", "قیمت فروش واحد", "مبلغ فاکتور", "کسورات", "دریافتی واقعی", "قیمت خرید اولیه تخمینی", "هزینه خالص تخمینی", "هزینه کل", "سود خالص فروش", "روش هزینه"]
        };
        foreach (var sale in ledger.Sales.Where(x => Rules.MonthOf(x.Date) == month.Key).OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            var item = items[sale.Code];
            var settled = calculation.Sales[sale.Id];
            rows.Add([sale.Date, sale.Code, item.Brand, item.Name, sale.Customer, sale.Quantity, sale.UnitPrice, sale.Total, sale.Deductions, settled.Sales, settled.GrossPurchaseUnit, settled.NetCostUnit, settled.Cost, settled.Profit, settled.HasManualCost ? "دستی" : "خودکار"]);
        }
        var summary = new List<object?[]>
        {
            ["عنوان", "مقدار"], ["ماه شمسی", month.Key], ["واحد پول", "ریال"],
            ["هزینه تخمینی فروش‌ها", total.Cost], ["فروش واقعی", total.Sales], ["سود خالص فروش", total.Profit],
            ["هزینه ثابت ماه", total.FixedCost], ["نتیجه پس از هزینه ثابت", total.Net], ["حاشیه سود", total.Margin],
            ["توضیح", "هزینه خرید از قیمت همان فروش و درصدهای Snapshot‌شده استخراج شده است."]
        };
        WriteWorkbook(file, rows, summary, "فروش‌های ماه", "خلاصه ماه");
    }

    public static void ExportLedgerRange(Ledger ledger, IReadOnlyList<Month> months, string file)
    {
        if (months.Count == 0) throw new InvalidDataException("برای خروجی بازه، حداقل یک ماه لازم است.");
        var totals = months.OrderBy(x => x.Key).Select(x => (Month: x, Total: LedgerCalculator.SummarizeMonth(ledger, x))).ToList();
        var rows = new List<object?[]> { ["ماه", "هزینه تخمینی", "فروش واقعی", "سود خالص فروش", "هزینه ثابت", "نتیجه", "حاشیه سود"] };
        foreach (var x in totals) rows.Add([x.Month.Key, x.Total.Cost, x.Total.Sales, x.Total.Profit, x.Total.FixedCost, x.Total.Net, x.Total.Margin]);
        var sales = totals.Sum(x => x.Total.Sales);
        var summary = new List<object?[]>
        {
            ["عنوان", "مقدار"], ["از ماه", totals.First().Month.Key], ["تا ماه", totals.Last().Month.Key], ["تعداد ماه", totals.Count],
            ["هزینه تخمینی", totals.Sum(x => x.Total.Cost)], ["فروش واقعی", sales], ["سود خالص فروش", totals.Sum(x => x.Total.Profit)],
            ["هزینه ثابت", totals.Sum(x => x.Total.FixedCost)], ["نتیجه", totals.Sum(x => x.Total.Net)], ["حاشیه سود وزنی", sales == 0 ? null : totals.Sum(x => x.Total.Profit) / sales]
        };
        WriteWorkbook(file, rows, summary, "گزارش بازه", "خلاصه بازه");
    }

    static void WriteWorkbook(string file, List<object?[]> rows, List<object?[]> summary, string firstName, string secondName)
    {
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                void Put(string name, string text) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
                Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Put("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put("xl/workbook.xml", $"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{SecurityElement.Escape(firstName)}\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"{SecurityElement.Escape(secondName)}\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Put("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                XElement Sheet(IEnumerable<object?[]> data) => new XElement(S + "worksheet",
                    new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", "0"), new XAttribute("rightToLeft", "1"))),
                    new XElement(S + "sheetData", data.Select((row, rowIndex) => new XElement(S + "row", new XAttribute("r", rowIndex + 1), row.Select((value, colIndex) =>
                    {
                        var cell = new XElement(S + "c", new XAttribute("r", Column(colIndex + 1) + (rowIndex + 1)));
                        if (value is decimal or int) cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
                        else { cell.Add(new XAttribute("t", "inlineStr")); cell.Add(new XElement(S + "is", new XElement(S + "t", value?.ToString() ?? ""))); }
                        return cell;
                    }))));
                Put("xl/worksheets/sheet1.xml", Sheet(rows).ToString());
                Put("xl/worksheets/sheet2.xml", Sheet(summary).ToString());
            }
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static string Column(int number)
    {
        var value = "";
        while (number > 0) { number--; value = (char)('A' + number % 26) + value; number /= 26; }
        return value;
    }
}
