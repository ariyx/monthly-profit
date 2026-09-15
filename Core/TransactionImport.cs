using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace Profit.Core;

public enum TransactionKind { Purchase, Sale }
public sealed record ImportedTransaction(int Row, string ImportKey, string Date, string Code, string Name, string Account, decimal Quantity, decimal UnitPrice, decimal Total, decimal Deductions);
public sealed record TransactionImportReview(List<ImportedTransaction> Rows, List<ImportIssue> Issues, int Ignored);

public static class TransactionImport
{
    static readonly XNamespace Ss = "urn:schemas-microsoft-com:office:spreadsheet";
    static readonly XNamespace X = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static string Header(string text) => Rules.Normalize(text).Replace(" ", "").Replace("‌", "");

    public static TransactionImportReview Review(string file)
    {
        var size = new FileInfo(file).Length;
        if (size > 90_000_000) throw new InvalidDataException("حداکثر حجم فایل ۹۰ مگابایت است.");
        var sourceRows = Path.GetExtension(file).Equals(".xls", StringComparison.OrdinalIgnoreCase) ? ReadSpreadsheetMl(file) : ReadXlsx(file);
        if (sourceRows.Count == 0) throw new InvalidDataException("فایل اکسل ردیفی ندارد.");
        var hash = Hash(file); var valid = new List<ImportedTransaction>(); var issues = new List<ImportIssue>(); var ignored = 0;
        foreach (var (number, cells) in sourceRows)
        {
            try
            {
                string Value(string name) => cells.GetValueOrDefault(Header(name), "");
                var unitText = Value("قیمت"); var totalText = Value("قیمت کل");
                // آفر/تستر با قیمت یک یا دو رقمی و ردیف‌های بدون مبلغ، وارد گردش واقعی نمی‌شوند.
                if (string.IsNullOrWhiteSpace(unitText) || string.IsNullOrWhiteSpace(totalText)) { ignored++; continue; }
                var unit = Rules.Number(unitText); var total = Rules.Number(totalText);
                if (unit <= 99 || total <= 99) { ignored++; continue; }
                var date = Rules.Digits(Value("تاریخ")); var code = Rules.Normalize(Value("کد کالا")); var name = Rules.Normalize(Value("نام کالا"));
                var account = Rules.Normalize(Value("نام حساب")); var quantity = Rules.Number(Value("تعداد واحد اصلی"));
                var deductionsText = Value("کسورات"); var deductions = string.IsNullOrWhiteSpace(deductionsText) ? 0m : Rules.Number(deductionsText);
                if (!Rules.ValidDate(date) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || quantity <= 0 || total - deductions < 0) throw new InvalidDataException("ستون‌های اجباری ردیف نامعتبر هستند.");
                valid.Add(new ImportedTransaction(number, hash + ":" + number, date, code, name, account, quantity, unit, total, deductions));
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or OverflowException)
            {
                issues.Add(new ImportIssue(number, ex.Message));
            }
        }
        return new(valid, issues, ignored);
    }

    public static string? DetectBrand(string code, string name)
    {
        var n = Rules.Normalize(name);
        if (code.StartsWith("106", StringComparison.Ordinal)) return "Fikores";
        if (code.StartsWith("108", StringComparison.Ordinal)) return "پیکشن";
        if (code.StartsWith("115", StringComparison.Ordinal) || code.StartsWith("145", StringComparison.Ordinal)) return "2080";
        if (code.StartsWith("118", StringComparison.Ordinal)) return "سالومه";
        if (code.StartsWith("128", StringComparison.Ordinal)) return "مکسی بل";
        if (code.StartsWith("137", StringComparison.Ordinal)) return "پلیس";
        if (code.StartsWith("147", StringComparison.Ordinal)) return "سوپکس";
        if (code.StartsWith("161", StringComparison.Ordinal)) return "پانته‌آ";
        if (code.StartsWith("162", StringComparison.Ordinal)) return "Blue Night";
        if (code.StartsWith("112", StringComparison.Ordinal))
        {
            if (n.Contains("بیوتی نام")) return "بیوتی نام";
            if (n.Contains("آذر بیوتی") || n.Contains("آذربیوتی")) return "آذر بیوتی";
        }
        return null;
    }

    static string Hash(string file)
    {
        using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream));
    }
    static List<(int Number, Dictionary<string, string> Cells)> ReadSpreadsheetMl(string file)
    {
        using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 120_000_000 });
        var doc = XDocument.Load(reader); var rows = doc.Descendants(Ss + "Row").ToList(); if (rows.Count == 0) return [];
        List<string> Row(XElement row)
        {
            var values = new List<string>(); var index = 1;
            foreach (var cell in row.Elements(Ss + "Cell"))
            {
                index = int.TryParse((string?)cell.Attribute(Ss + "Index"), out var explicitIndex) ? explicitIndex : index;
                while (values.Count < index - 1) values.Add("");
                values.Add(cell.Element(Ss + "Data")?.Value ?? ""); index++;
            }
            return values;
        }
        var headers = Row(rows[0]).Select(Header).ToList(); var result = new List<(int, Dictionary<string, string>)>();
        for (var i = 1; i < rows.Count; i++)
        {
            var values = Row(rows[i]); var cells = headers.Select((h, j) => (h, Value: j < values.Count ? values[j] : "")).Where(x => !string.IsNullOrWhiteSpace(x.h)).ToDictionary(x => x.h, x => x.Value);
            result.Add((i + 1, cells));
        }
        return result;
    }
    static List<(int Number, Dictionary<string, string> Cells)> ReadXlsx(string file)
    {
        using var zip = ZipFile.OpenRead(file); XDocument Xml(string path)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException("بخش اکسل موجود نیست: " + path);
            using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 120_000_000 }); return XDocument.Load(reader);
        }
        var strings = zip.GetEntry("xl/sharedStrings.xml") is null ? [] : Xml("xl/sharedStrings.xml").Descendants(X + "si").Select(x => string.Concat(x.Descendants(X + "t").Select(t => t.Value))).ToArray();
        var workbook = Xml("xl/workbook.xml"); XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheet = workbook.Descendants(X + "sheet").FirstOrDefault() ?? throw new InvalidDataException("شیت اکسل یافت نشد.");
        var relation = Xml("xl/_rels/workbook.xml.rels").Root!.Elements().First(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(rel + "id"));
        var target = (string?)relation.Attribute("Target") ?? throw new InvalidDataException("مسیر شیت نامعتبر است.");
        var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
        var rows = Xml(path).Descendants(X + "row").ToList(); if (rows.Count == 0) return [];
        Dictionary<string, string> Cells(XElement row) => row.Elements(X + "c").ToDictionary(c => new string(((string?)c.Attribute("r") ?? "").TakeWhile(char.IsLetter).ToArray()), c =>
        {
            var type = (string?)c.Attribute("t"); var value = c.Element(X + "v")?.Value ?? "";
            return type == "s" ? strings[int.Parse(value, CultureInfo.InvariantCulture)] : type == "inlineStr" ? string.Concat(c.Descendants(X + "t").Select(t => t.Value)) : value;
        });
        var first = Cells(rows[0]); var headers = first.ToDictionary(x => x.Key, x => Header(x.Value)); var result = new List<(int, Dictionary<string, string>)>();
        foreach (var row in rows.Skip(1))
        {
            var values = Cells(row); var normalized = values.Where(x => headers.ContainsKey(x.Key)).ToDictionary(x => headers[x.Key], x => x.Value);
            result.Add((int.TryParse((string?)row.Attribute("r"), out var number) ? number : 0, normalized));
        }
        return result;
    }
}
