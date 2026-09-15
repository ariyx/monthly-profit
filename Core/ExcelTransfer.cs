using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Profit.Core;

public sealed record ImportIssue(int Row, string Message);
public sealed record ImportReview(List<Product> Products, List<ImportIssue> Issues);

// Narrow, dependency-free OOXML transfer. No macros, external links or formula execution.
public static class ExcelTransfer
{
    static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly string[] Headers = ["برند", "کالا", "قیمت خرید واحد (ریال)", "تعداد/مقدار", "تخفیف خرید %", "آفر خرید %", "سود اضافه %", "فروش چکی %", "فروش نقدی %", "تخفیف نقدی %", "بهای تمام‌شده", "قیمت فروش واحد", "فروش نقدی", "فروش چکی", "فروش کل", "سود ناخالص", "حاشیه سود"];
    static string Col(int n) { var s = ""; for (; n > 0; n = (n - 1) / 26) s = (char)('A' + (n - 1) % 26) + s; return s; }
    static XDocument Xml(ZipArchive z, string name)
    {
        var entry = z.GetEntry(name) ?? throw new InvalidDataException("بخش اکسل موجود نیست: " + name);
        if (entry.Length > 30_000_000) throw new InvalidDataException("بخش اکسل بیش از حد بزرگ است.");
        using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 30_000_000 });
        return XDocument.Load(reader);
    }
    public static List<Product> Import(string file)
    {
        var review = Review(file);
        if (review.Issues.Count > 0) throw new InvalidDataException("هیچ ردیفی وارد نشد.\n" + string.Join("\n", review.Issues.Take(15).Select(x => "ردیف " + x.Row + ": " + x.Message)));
        Rules.Validate(new Month { Key = "1405/01", Products = review.Products }); return review.Products;
    }
    public static ImportReview Review(string file)
    {
        if (new FileInfo(file).Length > 30_000_000) throw new InvalidDataException("حداکثر حجم اکسل ۳۰ مگابایت است.");
        using var z = ZipFile.OpenRead(file);
        var strings = z.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml(z, "xl/sharedStrings.xml").Descendants(S + "si").Select(x => string.Concat(x.Descendants(S + "t").Select(t => t.Value))).ToArray();
        var workbook = Xml(z, "xl/workbook.xml"); XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheets = workbook.Descendants(S + "sheet").ToList();
        var sheet = sheets.FirstOrDefault(x => (string?)x.Attribute("name") == "کالاها") ?? sheets.FirstOrDefault(x => (string?)x.Attribute("name") == "ورودی برندها") ?? sheets.FirstOrDefault() ?? throw new InvalidDataException("شیت خالی است.");
        var relationship = Xml(z, "xl/_rels/workbook.xml.rels").Root!.Elements().First(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(rel + "id"));
        if ((string?)relationship.Attribute("TargetMode") == "External") throw new InvalidDataException("پیوند خارجی قابل ورود نیست.");
        var target = (string?)relationship.Attribute("Target") ?? "";
        var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        var cells = Xml(z, path).Descendants(S + "row").ToList(); if (cells.Count == 0) return new([], []);
        Dictionary<string, string> Row(XElement row) => row.Elements(S + "c").ToDictionary(c => new string(((string?)c.Attribute("r") ?? "").TakeWhile(char.IsLetter).ToArray()), c => {
            var type = (string?)c.Attribute("t"); var value = c.Element(S + "v")?.Value ?? "";
            if (type == "e") throw new InvalidDataException("سلول خطادار در اکسل: " + (string?)c.Attribute("r"));
            return type == "s" ? strings[int.Parse(value, CultureInfo.InvariantCulture)] : type == "inlineStr" ? string.Concat(c.Descendants(S + "t").Select(t => t.Value)) : value;
        });
        var first = Row(cells[0]); var modern = first.GetValueOrDefault("B") == "کالا";
        if (first.GetValueOrDefault("A") != "برند" || (!modern && !(first.GetValueOrDefault("B") ?? "").Contains("قیمت خرید"))) throw new InvalidDataException("قالب ناشناخته است. از خروجی برنامه یا شیت «ورودی برندها» استفاده کنید.");
        var result = new List<Product>(); var issues = new List<ImportIssue>();
        foreach (var row in cells.Skip(1))
        {
            try
            {
                var d = Row(row); var brand = d.GetValueOrDefault("A", "");
                if (string.IsNullOrWhiteSpace(brand) && string.IsNullOrWhiteSpace(d.GetValueOrDefault(modern ? "C" : "B")) && string.IsNullOrWhiteSpace(d.GetValueOrDefault(modern ? "D" : "I"))) continue;
                decimal Num(string c) => Rules.Number(d.GetValueOrDefault(c, ""));
                var p = modern ? new Product { Brand = brand, Name = d.GetValueOrDefault("B", ""), Price = Num("C"), Quantity = Num("D"), Discount = Num("E"), Offer = Num("F"), Markup = Num("G"), CreditShare = Num("H"), CashShare = Num("I"), CashDiscount = Num("J") }
                    : new Product { Brand = brand, Name = "کالای واردشده " + (string?)row.Attribute("r"), Price = Num("B"), Quantity = Num("I"), Discount = Num("C"), Offer = Num("D"), Markup = Num("E"), CreditShare = Num("F"), CashShare = Num("G"), CashDiscount = Num("H") };
                p = Rules.Clean(p); var errors = Rules.Validate(p); if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors)); result.Add(p);
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or OverflowException or ArgumentException) { issues.Add(new ImportIssue(int.TryParse((string?)row.Attribute("r"), out var rowNumber) ? rowNumber : 0, ex.Message)); }
            if (result.Count + issues.Count > 10000) throw new InvalidDataException("بیش از ده هزار ردیف قابل ورود نیست.");
        }
        return new(result, issues);
    }
    public static void Export(Month month, string file)
    {
        Rules.Validate(month); var total = Rules.Summarize(month);
        var rows = new List<object?[]> { Headers.Cast<object?>().ToArray() };
        foreach (var p in month.Products) { var r = Rules.Calculate(p); rows.Add([p.Brand, p.Name, p.Price, p.Quantity, p.Discount, p.Offer, p.Markup, p.CreditShare, p.CashShare, p.CashDiscount, r.Cost, r.UnitSale, r.Cash, r.Credit, r.Sales, r.Profit, r.Margin]); }
        List<object?[]> summary = [["عنوان", "مقدار"], ["ماه شمسی", month.Key], ["واحد پول", "ریال"], ["بهای تمام‌شده", total.Cost], ["فروش کل", total.Sales], ["سود ناخالص", total.Profit], ["هزینه ثابت ماه", total.FixedCost], ["سود / زیان پس از هزینه ثابت", total.Net], ["حاشیه سود", total.Margin], ["نوع گزارش", "خروجی مقادیر محاسبه‌شده؛ درصدها به‌صورت اعشاری ذخیره شده‌اند"], ["نسخه قواعد", month.FormulaVersion]];
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var z = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                void Put(string name, string text) { using var writer = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
                Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Put("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"کالاها\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"خلاصه ماه\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Put("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                Put("xl/styles.xml", "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Tahoma\"/></font><font><b/><color rgb=\"FFFFFFFF\"/><sz val=\"11\"/><name val=\"Tahoma\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF234AB5\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs><cellXfs count=\"4\"><xf fontId=\"0\" fillId=\"0\" borderId=\"0\"/><xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" applyNumberFormat=\"1\"/><xf numFmtId=\"10\" fontId=\"0\" fillId=\"0\" borderId=\"0\" applyNumberFormat=\"1\"/><xf fontId=\"1\" fillId=\"2\" borderId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" horizontal=\"center\"/></xf></cellXfs></styleSheet>");
                XElement Sheet(List<object?[]> data, bool products)
                {
                    var width = products ? 22 : 48;
                    return new XElement(S + "worksheet", new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", "0"), new XAttribute("rightToLeft", "1"), new XElement(S + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))), new XElement(S + "cols", new XElement(S + "col", new XAttribute("min", "1"), new XAttribute("max", data[0].Length), new XAttribute("width", width), new XAttribute("customWidth", "1"))), new XElement(S + "sheetData", data.Select((row, i) => new XElement(S + "row", new XAttribute("r", i + 1), new XAttribute("ht", i == 0 ? 40 : 24), new XAttribute("customHeight", "1"), row.Select((v, j) => {
                        bool numeric = v is decimal or int; int style = i == 0 ? 3 : !numeric ? 0 : products && ((j >= 4 && j <= 9) || j == 16) || !products && i == 8 && j == 1 ? 2 : 1;
                        return new XElement(S + "c", new XAttribute("r", Col(j + 1) + (i + 1)), new XAttribute("s", style), numeric ? new XElement(S + "v", Convert.ToString(v, CultureInfo.InvariantCulture)) : new object[] { new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", v?.ToString() ?? "")) });
                    })))));
                }
                Put("xl/worksheets/sheet1.xml", Sheet(rows, true).ToString()); Put("xl/worksheets/sheet2.xml", Sheet(summary, false).ToString());
            }
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void ExportRange(IReadOnlyList<Month> months, string file)
    {
        if (months.Count == 0) throw new InvalidDataException("برای خروجی بازه، حداقل یک ماه لازم است.");
        foreach (var m in months) Rules.Validate(m);
        var rows = new List<object?[]> { new object?[] { "ماه", "بهای تمام‌شده", "فروش", "سود ناخالص", "هزینه ثابت", "نتیجه", "حاشیه سود" } };
        foreach (var m in months.OrderBy(x => x.Key)) { var t = Rules.Summarize(m); rows.Add([m.Key, t.Cost, t.Sales, t.Profit, t.FixedCost, t.Net, t.Margin]); }
        var totals = months.Select(Rules.Summarize).ToList(); var sales = totals.Sum(x => x.Sales); var profit = totals.Sum(x => x.Profit);
        var summary = new List<object?[]>
        {
            new object?[] { "عنوان", "مقدار" }, new object?[] { "از ماه", months.Min(x => x.Key) }, new object?[] { "تا ماه", months.Max(x => x.Key) }, new object?[] { "تعداد ماه", months.Count }, new object?[] { "بهای تمام‌شده", totals.Sum(x => x.Cost) }, new object?[] { "فروش کل", sales }, new object?[] { "سود ناخالص", profit }, new object?[] { "هزینه ثابت", totals.Sum(x => x.FixedCost) }, new object?[] { "نتیجه", totals.Sum(x => x.Net) }, new object?[] { "حاشیه سود وزنی", sales == 0 ? null : profit / sales }
        };
        WriteSimpleWorkbook(file, rows, summary, "گزارش بازه", "خلاصه بازه");
    }
    public static void ExportLedgerMonth(Ledger ledger, Month month, string file)
    {
        var calc = LedgerCalculator.Calculate(ledger); var total = LedgerCalculator.SummarizeMonth(ledger, month);
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var rows = new List<object?[]> { ["نوع", "تاریخ", "کد کالا", "برند", "کالا", "حساب", "تعداد", "قیمت واحد", "مبلغ کل", "کسورات", "مبلغ پس از کسورات", "بهای تمام‌شده", "سود ناخالص"] };
        foreach (var p in ledger.Purchases.Where(x => Rules.MonthOf(x.Date) == month.Key).OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            var item = items[p.Code]; rows.Add([p.IsAdjustment ? "تعدیل موجودی" : "خرید", p.Date, p.Code, item.Brand, item.Name, p.Supplier, p.Quantity, p.UnitPrice, p.Total, p.Deductions, Rules.NetPurchase(p), null, null]);
        }
        foreach (var s in ledger.Sales.Where(x => Rules.MonthOf(x.Date) == month.Key).OrderBy(x => x.Date).ThenBy(x => x.Id))
        {
            var item = items[s.Code]; var settled = calc.Sales[s.Id]; rows.Add(["فروش", s.Date, s.Code, item.Brand, item.Name, s.Customer, s.Quantity, s.UnitPrice, s.Total, s.Deductions, settled.Sales, settled.Cost, settled.Profit]);
        }
        var summary = new List<object?[]> { ["عنوان", "مقدار"], ["ماه شمسی", month.Key], ["واحد پول", "ریال"], ["بهای تمام‌شده فروش‌رفته", total.Cost], ["فروش کل", total.Sales], ["سود ناخالص", total.Profit], ["هزینه ثابت ماه", total.FixedCost], ["نتیجه پس از هزینه ثابت", total.Net], ["حاشیه سود", total.Margin] };
        WriteSimpleWorkbook(file, rows, summary, "تراکنش‌های ماه", "خلاصه ماه");
    }
    public static void ExportLedgerRange(Ledger ledger, IReadOnlyList<Month> months, string file)
    {
        if (months.Count == 0) throw new InvalidDataException("برای خروجی بازه، حداقل یک ماه لازم است.");
        var rows = new List<object?[]> { ["ماه", "بهای تمام‌شده", "فروش", "سود ناخالص", "هزینه ثابت", "نتیجه", "حاشیه سود"] };
        var totals = months.OrderBy(x => x.Key).Select(x => (Month: x, Total: LedgerCalculator.SummarizeMonth(ledger, x))).ToList();
        foreach (var x in totals) rows.Add([x.Month.Key, x.Total.Cost, x.Total.Sales, x.Total.Profit, x.Total.FixedCost, x.Total.Net, x.Total.Margin]);
        var sales = totals.Sum(x => x.Total.Sales); var profit = totals.Sum(x => x.Total.Profit);
        var summary = new List<object?[]> { ["عنوان", "مقدار"], ["از ماه", totals.First().Month.Key], ["تا ماه", totals.Last().Month.Key], ["تعداد ماه", totals.Count], ["بهای تمام‌شده", totals.Sum(x => x.Total.Cost)], ["فروش کل", sales], ["سود ناخالص", profit], ["هزینه ثابت", totals.Sum(x => x.Total.FixedCost)], ["نتیجه", totals.Sum(x => x.Total.Net)], ["حاشیه سود وزنی", sales == 0 ? null : profit / sales] };
        WriteSimpleWorkbook(file, rows, summary, "گزارش بازه", "خلاصه بازه");
    }
    static void WriteSimpleWorkbook(string file, List<object?[]> rows, List<object?[]> summary, string firstName, string secondName)
    {
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var z = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                void Put(string name, string text) { using var writer = new StreamWriter(z.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
                Put("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Put("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Put("xl/workbook.xml", $"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"{SecurityElement.Escape(firstName)}\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"{SecurityElement.Escape(secondName)}\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Put("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                XElement Sheet(List<object?[]> data)
                {
                    var sheetData = new XElement(S + "sheetData");
                    foreach (var (row, i) in data.Select((row, i) => (row, i)))
                    {
                        var xmlRow = new XElement(S + "row", new XAttribute("r", i + 1));
                        foreach (var (value, j) in row.Select((value, j) => (value, j)))
                        {
                            var cell = new XElement(S + "c", new XAttribute("r", Col(j + 1) + (i + 1)));
                            if (value is decimal or int) cell.Add(new XElement(S + "v", Convert.ToString(value, CultureInfo.InvariantCulture)));
                            else { cell.Add(new XAttribute("t", "inlineStr")); cell.Add(new XElement(S + "is", new XElement(S + "t", value?.ToString() ?? ""))); }
                            xmlRow.Add(cell);
                        }
                        sheetData.Add(xmlRow);
                    }
                    return new XElement(S + "worksheet", new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", "0"), new XAttribute("rightToLeft", "1"))), sheetData);
                }
                Put("xl/worksheets/sheet1.xml", Sheet(rows).ToString()); Put("xl/worksheets/sheet2.xml", Sheet(summary).ToString());
            }
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
