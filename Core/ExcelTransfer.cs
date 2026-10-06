using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace Profit.Core;

public static class ExcelTransfer
{
    static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void ExportLedgerMonth(Ledger ledger, Month month, string file, byte[]? companyLogo = null)
    {
        var calculation = LedgerCalculator.Calculate(ledger);
        var total = LedgerCalculator.SummarizeMonth(ledger, month, calculation);
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<object?[]>
        {
            new object?[] { "تاریخ", "کد کالا", "برند", "کالا", "مشتری", "تعداد", "تعداد برگشتی", "تعداد خالص", "قیمت فروش واحد", "مبلغ فاکتور", "کسورات", "دریافتی واقعی", "قیمت خرید اولیه تخمینی", "هزینه خالص تخمینی", "هزینه کل", "سود خالص فروش", "وضعیت محاسبه" }
        };
        foreach (var sale in ledger.Sales.Where(x => Rules.MonthOf(x.Date) == month.Key).OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            var item = items[sale.Code];
            var settled = calculation.Sales.GetValueOrDefault(sale.Id);
            rows.Add(settled is null
                ? new object?[] { sale.Date, sale.Code, string.IsNullOrWhiteSpace(item.Brand) ? "تعیین‌نشده" : item.Brand, item.Name, sale.Customer, sale.Quantity, null, null, sale.UnitPrice, sale.Total, sale.Deductions, null, null, null, null, null, "در انتظار تعیین برند یا تأیید ماه" }
                : new object?[] { sale.Date, sale.Code, item.Brand, item.Name, sale.Customer, sale.Quantity, settled.ReturnedQuantity, sale.Quantity - settled.ReturnedQuantity, sale.UnitPrice, sale.Total, sale.Deductions, settled.Sales, settled.GrossPurchaseUnit, settled.NetCostUnit, settled.Cost, settled.Profit, settled.ReturnCount > 0 ? $"{settled.ReturnCount} برگشت اعمال‌شده" : settled.HasManualCost ? "هزینه دستی" : "محاسبه‌شده" });
        }
        var summary = new List<object?[]>
        {
            new object?[] { "عنوان", "مقدار" }, new object?[] { "ماه شمسی", month.Key }, new object?[] { "واحد پول", "ریال" },
            new object?[] { "بهای تمام‌شده فروش‌ها", total.Cost }, new object?[] { "فروش واقعی", total.Sales }, new object?[] { "سود خالص فروش", total.Profit },
            new object?[] { "هزینه ثابت ماه", total.FixedCost }, new object?[] { "نتیجه پس از هزینه ثابت", total.Net }, new object?[] { "حاشیه سود", total.Margin },
            new object?[] { "فروش‌های معلق", total.PendingSalesCount }, new object?[] { "مبلغ فاکتور فروش‌های معلق", total.PendingInvoiceSales },
            new object?[] { "تعداد برگشتی اعمال‌شده", total.ReturnedQuantity }, new object?[] { "برگشت‌های معلق", total.PendingReturnsCount },
            new object?[] { "آفرهای نیازمند بررسی", calculation.Offers.Rows.Count(x => Rules.MonthOf(x.Entry.Date) == month.Key && !x.UnitPrice.HasValue) },
            new object?[] { "اصلاح دستی برگشت آفر بدون فروش اصلی", total.UnlinkedOfferCredit },
            new object?[] { "توضیح", "هزینه خرید از قیمت همان فروش و درصدهای Snapshot‌شده استخراج شده است." }
        };
        WriteWorkbook(file, rows, summary, "فروش‌های ماه", "خلاصه ماه", "گزارش جزئیات فروش", $"ماه {month.Key}", companyLogo);
    }

    public static void ExportLedgerRange(Ledger ledger, IReadOnlyList<Month> months, string file, byte[]? companyLogo = null)
    {
        if (months.Count == 0) throw new InvalidDataException("برای خروجی بازه، حداقل یک ماه لازم است.");
        var totals = months.OrderBy(x => x.Key).Select(x => (Month: x, Total: LedgerCalculator.SummarizeMonth(ledger, x))).ToList();
        var rows = new List<object?[]> { new object?[] { "ماه", "بهای تمام‌شده", "تعداد برگشتی", "فروش واقعی", "سود خالص فروش", "هزینه ثابت", "نتیجه", "حاشیه سود", "فروش معلق", "مبلغ فاکتور معلق", "برگشت معلق" } };
        foreach (var x in totals) rows.Add(new object?[] { x.Month.Key, x.Total.Cost, x.Total.ReturnedQuantity, x.Total.Sales, x.Total.Profit, x.Total.FixedCost, x.Total.Net, x.Total.Margin, x.Total.PendingSalesCount, x.Total.PendingInvoiceSales, x.Total.PendingReturnsCount });
        var sales = totals.Sum(x => x.Total.Sales);
        var summary = new List<object?[]>
        {
            new object?[] { "عنوان", "مقدار" }, new object?[] { "از ماه", totals.First().Month.Key }, new object?[] { "تا ماه", totals.Last().Month.Key }, new object?[] { "تعداد ماه", totals.Count },
            new object?[] { "بهای تمام‌شده", totals.Sum(x => x.Total.Cost) }, new object?[] { "تعداد برگشتی", totals.Sum(x => x.Total.ReturnedQuantity) }, new object?[] { "فروش واقعی", sales }, new object?[] { "سود خالص فروش", totals.Sum(x => x.Total.Profit) },
            new object?[] { "هزینه ثابت", totals.Sum(x => x.Total.FixedCost) }, new object?[] { "نتیجه", totals.Sum(x => x.Total.Net) }, new object?[] { "حاشیه سود وزنی", sales == 0 ? null : totals.Sum(x => x.Total.Profit) / sales }
        };
        summary.Add(new object?[] { "اصلاح دستی برگشت آفر بدون فروش اصلی", totals.Sum(x => x.Total.UnlinkedOfferCredit) });
        WriteWorkbook(file, rows, summary, "گزارش بازه", "خلاصه بازه", "گزارش عملکرد ماهانه", $"از {totals.First().Month.Key} تا {totals.Last().Month.Key}", companyLogo);
    }

    static void WriteWorkbook(string file, List<object?[]> rows, List<object?[]> summary, string firstName, string secondName, string reportTitle, string period, byte[]? companyLogo)
    {
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                void Put(string name, string text) { using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
                void PutBytes(string name, byte[] bytes) { using var stream = archive.CreateEntry(name).Open(); stream.Write(bytes, 0, bytes.Length); }
                var hasLogo = companyLogo is { Length: > 0 };
                Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    (hasLogo ? "<Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>" : "") +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Put("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put("xl/workbook.xml", $"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{SecurityElement.Escape(firstName)}\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"{SecurityElement.Escape(secondName)}\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Put("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Put("xl/styles.xml", Styles());
                if (hasLogo)
                {
                    PutBytes("xl/media/company-logo.png", companyLogo!);
                    Put("xl/worksheets/_rels/sheet1.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/></Relationships>");
                    Put("xl/drawings/_rels/drawing1.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/company-logo.png\"/></Relationships>");
                    Put("xl/drawings/drawing1.xml", "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><xdr:oneCellAnchor><xdr:from><xdr:col>15</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:ext cx=\"762000\" cy=\"762000\"/><xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"1\" name=\"لوگوی شرکت\"/><xdr:cNvPicPr/></xdr:nvPicPr><xdr:blipFill><a:blip r:embed=\"rId1\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill><xdr:spPr><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic><xdr:clientData/></xdr:oneCellAnchor></xdr:wsDr>");
                }
                XElement Sheet(IReadOnlyList<object?[]> data, string title, string subtitle, bool summarySheet, bool includeLogo)
                {
                    var sheetData = new XElement(S + "sheetData");
                    var columnCount = Math.Max(2, data.Max(x => x.Length));
                    XElement Row(int index, double height, params XElement[] cells) => new(S + "row", new XAttribute("r", index), new XAttribute("ht", height), new XAttribute("customHeight", 1), cells);
                    XElement Cell(int column, int row, object? value, int style)
                    {
                        var cell = new XElement(S + "c", new XAttribute("r", Column(column) + row), new XAttribute("s", style));
                        if (value is decimal or int)
                        {
                            cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
                        }
                        else if (value is not null)
                        {
                            cell.Add(new XAttribute("t", "inlineStr"));
                            cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value.ToString() ?? "")));
                        }
                        return cell;
                    }
                    sheetData.Add(Row(1, 32, Cell(1, 1, "شرکت متحد توزیع ایرانیان", 1)));
                    sheetData.Add(Row(2, 28, Cell(1, 2, title, 2)));
                    sheetData.Add(Row(3, 21, Cell(1, 3, subtitle + "  |  واحد پول: ریال", 3)));
                    sheetData.Add(Row(4, 10));
                    var headerRow = 5;
                    for (var i = 0; i < data.Count; i++)
                    {
                        var values = data[i]; var styleBase = i % 2 == 0 ? 7 : 5;
                        var cells = Enumerable.Range(0, values.Length).Select(column =>
                        {
                            var label = values[0]?.ToString() ?? "";
                            var style = i == 0 ? 4
                                : summarySheet && column == 0 ? 9
                                : summarySheet && values[column] is decimal or int ? (label.Contains("حاشیه", StringComparison.Ordinal) ? 11 : 10)
                                : values[column] is decimal or int ? styleBase + 1 : styleBase;
                            return Cell(column + 1, headerRow + i, values[column], style);
                        }).ToArray();
                        sheetData.Add(Row(headerRow + i, i == 0 ? 24 : 21, cells));
                    }
                    var columns = new XElement(S + "cols", Enumerable.Range(1, columnCount).Select(i => new XElement(S + "col", new XAttribute("min", i), new XAttribute("max", i), new XAttribute("width", summarySheet ? i == 1 ? 34 : 28 : i <= 2 ? 15 : i is 4 or 5 ? 25 : 16), new XAttribute("customWidth", 1))));
                    var merges = new XElement(S + "mergeCells", new XAttribute("count", 3), new XElement(S + "mergeCell", new XAttribute("ref", $"A1:{Column(columnCount)}1")), new XElement(S + "mergeCell", new XAttribute("ref", $"A2:{Column(columnCount)}2")), new XElement(S + "mergeCell", new XAttribute("ref", $"A3:{Column(columnCount)}3")));
                    return new XElement(S + "worksheet",
                        new XAttribute(XNamespace.Xmlns + "r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"),
                        new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", "0"), new XAttribute("rightToLeft", "1"), new XElement(S + "pane", new XAttribute("ySplit", headerRow), new XAttribute("topLeftCell", "A6"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                        columns, sheetData,
                        new XElement(S + "autoFilter", new XAttribute("ref", $"A{headerRow}:{Column(data[0].Length)}{headerRow + data.Count - 1}")),
                        merges,
                        new XElement(S + "pageMargins", new XAttribute("left", ".25"), new XAttribute("right", ".25"), new XAttribute("top", ".45"), new XAttribute("bottom", ".45"), new XAttribute("header", ".2"), new XAttribute("footer", ".2")),
                        new XElement(S + "pageSetup", new XAttribute("orientation", "landscape"), new XAttribute("fitToWidth", "1"), new XAttribute("fitToHeight", "0")),
                        includeLogo ? new XElement(S + "drawing", new XAttribute(XNamespace.Get("http://schemas.openxmlformats.org/officeDocument/2006/relationships") + "id", "rId1")) : null);
                }
                Put("xl/worksheets/sheet1.xml", Sheet(rows, reportTitle, period, false, hasLogo).ToString());
                Put("xl/worksheets/sheet2.xml", Sheet(summary, "خلاصه " + reportTitle, period, true, false).ToString());
            }
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    static string Styles() => """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
  <numFmts count="2"><numFmt numFmtId="164" formatCode="#,##0;[Red]-#,##0"/><numFmt numFmtId="165" formatCode="0.00%"/></numFmts>
  <fonts count="3"><font><sz val="10"/><color rgb="FF172033"/><name val="Vazirmatn"/><family val="2"/></font><font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Vazirmatn"/><family val="2"/></font><font><b/><sz val="16"/><color rgb="FF172033"/><name val="Vazirmatn"/><family val="2"/></font></fonts>
  <fills count="6"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF151B28"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEAF2FF"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF8FAFC"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFF1F5F9"/><bgColor indexed="64"/></patternFill></fill></fills>
  <borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD9E1EC"/></left><right style="thin"><color rgb="FFD9E1EC"/></right><top style="thin"><color rgb="FFD9E1EC"/></top><bottom style="thin"><color rgb="FFD9E1EC"/></bottom><diagonal/></border></borders>
  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
  <cellXfs count="12">
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="0" applyFill="1" applyFont="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="0" fontId="2" fillId="3" borderId="0" applyFill="1" applyFont="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="0" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="2" borderId="1" applyFill="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
    <xf numFmtId="0" fontId="0" fillId="0" borderId="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="164" fontId="0" fillId="0" borderId="1" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
    <xf numFmtId="0" fontId="0" fillId="4" borderId="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="164" fontId="0" fillId="4" borderId="1" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
    <xf numFmtId="0" fontId="1" fillId="5" borderId="1" applyFill="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="right" vertical="center"/></xf>
    <xf numFmtId="164" fontId="1" fillId="5" borderId="1" applyNumberFormat="1" applyFill="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
    <xf numFmtId="165" fontId="1" fillId="5" borderId="1" applyNumberFormat="1" applyFill="1" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="left" vertical="center"/></xf>
  </cellXfs>
</styleSheet>
""";

    static string Column(int number)
    {
        var value = "";
        while (number > 0) { number--; value = (char)('A' + number % 26) + value; number /= 26; }
        return value;
    }
}
