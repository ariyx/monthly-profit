using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Profit.Core;

public sealed record Product
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Brand { get; init; } = "";
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public decimal Quantity { get; init; } = 1;
    // Rates are decimal fractions, not whole percentage values.
    public decimal Discount { get; init; } = .25m;
    public decimal Offer { get; init; } = .055m;
    public decimal Markup { get; init; } = .04m;
    public decimal CreditShare { get; init; } = .7m;
    public decimal CashShare { get; init; } = .3m;
    public decimal CashDiscount { get; init; } = .05m;
}

public sealed record Month
{
    public string Key { get; init; } = "";
    public decimal FixedCost { get; set; } = 52_000_000m;
    public List<Product> Products { get; set; } = [];
    public int Revision { get; set; }
    public int FormulaVersion { get; init; } = 1;
    public Month CopyTo(string key) => this with { Key = key, Revision = 0, Products = Products.Select(p => p with { Id = Guid.NewGuid().ToString("N") }).ToList() };
}

public sealed record Result(decimal Purchase, decimal Discount, decimal AfterDiscount, decimal Offer,
    decimal Cost, decimal AddedPrice, decimal UnitSale, decimal Cash, decimal Credit)
{
    public decimal Sales => Cash + Credit;
    public decimal Profit => Sales - Cost;
    public decimal? Margin => Sales == 0 ? null : Profit / Sales;
}
public sealed record Total(decimal Cost, decimal Cash, decimal Credit, decimal Profit, decimal FixedCost, decimal Quantity, int Products, int Brands)
{
    public decimal Sales => Cash + Credit;
    public decimal Net => Profit - FixedCost;
    public decimal? Margin => Sales == 0 ? null : Profit / Sales;
}

public static class Rules
{
    public static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormKC).Replace('ي', 'ی').Replace('ك', 'ک');
    public static Product Clean(Product p) => p with { Brand = Normalize(p.Brand), Name = Normalize(p.Name) };
    public static List<string> Validate(Product p)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(p.Brand) || string.IsNullOrWhiteSpace(p.Name)) e.Add("نام برند و کالا الزامی است.");
        if (p.Brand.Length > 120 || p.Name.Length > 200) e.Add("نام برند یا کالا بیش از حد طولانی است.");
        if (p.Price <= 0 || p.Price > 1_000_000_000_000m) e.Add("قیمت خرید باید مثبت و حداکثر یک تریلیون ریال باشد.");
        if (p.Quantity <= 0 || p.Quantity > 1_000_000_000m) e.Add("مقدار باید مثبت و حداکثر یک میلیارد باشد.");
        if (new[] { p.Discount, p.Offer, p.Markup, p.CreditShare, p.CashShare, p.CashDiscount }.Any(x => x < 0 || x > 1)) e.Add("درصدها باید بین صفر و ۱۰۰ باشند.");
        if (p.Discount + p.Offer > 1) e.Add("جمع تخفیف خرید و آفر نباید از ۱۰۰٪ بیشتر باشد.");
        if (p.CashShare + p.CreditShare != 1) e.Add("جمع سهم نقدی و چکی باید دقیقاً ۱۰۰٪ باشد.");
        return e;
    }
    public static void Validate(Month m)
    {
        if (!ValidMonth(m.Key)) throw new InvalidDataException("ماه باید با قالب شمسی ۱۴۰۵/۰۶ وارد شود.");
        if (m.FormulaVersion != 1) throw new InvalidDataException("نسخه قواعد این پرونده با برنامه سازگار نیست.");
        if (m.FixedCost < 0 || m.FixedCost > 1_000_000_000_000_000m) throw new InvalidDataException("هزینه ثابت خارج از محدوده مجاز است.");
        if (m.Products.Count > 10000) throw new InvalidDataException("حداکثر ده هزار کالا در هر ماه مجاز است.");
        var names = new HashSet<string>(); var ids = new HashSet<string>();
        foreach (var p in m.Products)
        {
            var e = Validate(p); if (e.Count > 0) throw new InvalidDataException($"{p.Brand} / {p.Name}: {string.Join(" ", e)}");
            if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id)) throw new InvalidDataException("شناسه کالا تکراری یا خالی است.");
            if (!names.Add(Normalize(p.Brand) + "\u001f" + Normalize(p.Name))) throw new InvalidDataException("نام کالا در یک برند تکراری است: " + p.Name);
        }
    }
    public static bool ValidMonth(string key) => key.Length == 7 && key[4] == '/' && int.TryParse(key[..4], out var y) && y is >= 1300 and <= 1600 && int.TryParse(key[5..], out var m) && m is >= 1 and <= 12;
    public static Result Calculate(Product p)
    {
        var errors = Validate(p); if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors));
        var purchase = p.Price * p.Quantity;
        var discount = purchase * p.Discount;
        var offer = purchase * p.Offer;
        var unitSale = p.Price * (1 + p.Markup);
        return new(purchase, discount, purchase - discount, offer, purchase - discount - offer,
            purchase * p.Markup, unitSale, unitSale * p.Quantity * p.CashShare * (1 - p.CashDiscount), unitSale * p.Quantity * p.CreditShare);
    }
    public static Total Summarize(Month m)
    {
        Validate(m); var r = m.Products.Select(Calculate).ToList();
        return new(r.Sum(x => x.Cost), r.Sum(x => x.Cash), r.Sum(x => x.Credit), r.Sum(x => x.Profit), m.FixedCost,
            m.Products.Sum(x => x.Quantity), m.Products.Count, m.Products.Select(p => Normalize(p.Brand)).Distinct().Count());
    }
    public static string Digits(string s)
    {
        for (int i = 0; i < 10; i++) s = s.Replace((char)('۰' + i), (char)('0' + i)).Replace((char)('٠' + i), (char)('0' + i));
        return s.Replace("٬", "").Replace(",", "").Replace('٫', '.').Trim();
    }
    public static decimal Number(string s) => decimal.TryParse(Digits(s), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException("عدد نامعتبر است: " + s);
    public static string Money(decimal n) => n.ToString("#,##0.##", CultureInfo.InvariantCulture);
    public static string Percent(decimal? n) => n.HasValue ? (n.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "٪" : "تعریف‌نشده";
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static Month Sample(string key) => new() { Key = key, Products = new[] { "فیکورس", "سالومه", "لاکچری", "کاسه ای", "شامپو لاکچری", "ضد آفتاب" }.Select((b, i) => new Product { Brand = b, Name = "کالای نمونه " + (i + 1), Price = (i + 1) * 1_000_000m, Quantity = i == 4 ? 2 : 1 }).ToList() };
}
