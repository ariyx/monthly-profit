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
    public List<FixedExpense> FixedExpenses { get; set; } = [];
    public List<Product> Products { get; set; } = [];
    public int Revision { get; set; }
    public bool IsClosed { get; set; }
    public List<AuditEntry> Audit { get; set; } = [];
    public int FormulaVersion { get; init; } = 1;
    public Month CopyTo(string key) => this with { Key = key, Revision = 0, IsClosed = false, Products = Products.Select(p => p with { Id = Guid.NewGuid().ToString("N") }).ToList(), FixedExpenses = FixedExpenses.Select(x => x with { Id = Guid.NewGuid().ToString("N") }).ToList(), Audit = [] };
}

public sealed record FixedExpense
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; init; } = "";
    public decimal Amount { get; init; }
}

public sealed record AuditEntry
{
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
    public string Action { get; init; } = "";
}

public sealed record Preferences
{
    public bool AutoBackupOnExit { get; init; }
    public int AutoBackupKeep { get; init; } = 8;
    public DateTime? LastAutoBackupUtc { get; init; }
    public DateTime? LastBackupUtc { get; init; }
    public string? LastBackupPath { get; init; }
}

// نسخهٔ جدید برنامه، کالا را با کد آن می‌شناسد و درصدها را در سطح برند نگه می‌دارد.
public sealed record Brand
{
    public string Name { get; init; } = "";
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; } = .04m;
    public decimal CreditShare { get; init; } = .70m;
    public decimal CashShare { get; init; } = .30m;
    public decimal CashDiscount { get; init; } = .05m;
}

public sealed record CatalogItem
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Brand { get; init; } = "";
}

public sealed record Purchase
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Date { get; init; } = ""; // yyyyMMdd شمسی
    public string Code { get; init; } = "";
    public string Supplier { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Total { get; init; }
    public decimal Deductions { get; init; }
    public decimal BrandDiscount { get; init; }
    public decimal Offer { get; init; }
    public bool IsAdjustment { get; init; }
    public string Note { get; init; } = "";
    public string ImportKey { get; init; } = "";
}

public sealed record Sale
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Date { get; init; } = ""; // yyyyMMdd شمسی
    public string Code { get; init; } = "";
    public string Customer { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Total { get; init; }
    public decimal Deductions { get; init; }
    // Snapshot تنظیمات برند در زمان ثبت فروش، تا گزارش ماه‌های بسته تغییر نکند.
    public decimal CreditShare { get; init; }
    public decimal CashShare { get; init; }
    public decimal CashDiscount { get; init; }
    public string ImportKey { get; init; } = "";
}

// ماندهٔ محاسبه‌شده از فایل‌های سال قبل؛ این سطرها در گزارش ماه‌های ۱۴۰۵ نمایش داده نمی‌شوند.
public sealed record OpeningLot
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Code { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public string SourceDate { get; init; } = "";
}

public sealed record Ledger
{
    public List<Brand> Brands { get; init; } = [];
    public List<CatalogItem> Items { get; init; } = [];
    public List<Purchase> Purchases { get; init; } = [];
    public List<Sale> Sales { get; init; } = [];
    public List<OpeningLot> OpeningLots { get; init; } = [];
    public List<string> ImportedRows { get; init; } = [];
}

public sealed record SaleSettlement(decimal Cost, decimal Cash, decimal Credit, decimal Shortage)
{
    public decimal Sales => Cash + Credit;
    public decimal Profit => Sales - Cost;
}

public sealed record StockRow(string Code, string Name, string Brand, decimal Quantity, decimal Cost);
public sealed record LedgerTotal(decimal Cost, decimal Cash, decimal Credit, decimal Profit, decimal FixedCost, decimal Quantity, int Products, int Brands)
{
    public decimal Sales => Cash + Credit;
    // مبلغ فاکتور پس از کسورات، پیش از کسر تخفیف نقدی.
    public decimal InvoiceSales { get; init; }
    public decimal CashDiscountAmount { get; init; }
    public decimal Shortage { get; init; }
    public decimal Net => Profit - FixedCost;
    public decimal? Margin => Sales == 0 ? null : Profit / Sales;
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
        if (m.FixedExpenses.Count > 100) throw new InvalidDataException("حداکثر صد ریزهزینه ثابت مجاز است.");
        var expenseIds = new HashSet<string>();
        foreach (var item in m.FixedExpenses)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !expenseIds.Add(item.Id)) throw new InvalidDataException("شناسه ریزهزینه ثابت تکراری یا خالی است.");
            if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Trim().Length > 100) throw new InvalidDataException("عنوان ریزهزینه ثابت نامعتبر است.");
            if (item.Amount < 0 || item.Amount > 1_000_000_000_000_000m) throw new InvalidDataException("مبلغ ریزهزینه ثابت خارج از محدوده مجاز است.");
        }
        if (m.FixedExpenses.Count > 0 && m.FixedExpenses.Sum(x => x.Amount) != m.FixedCost) throw new InvalidDataException("جمع ریزهزینه‌ها باید با هزینه ثابت ماه برابر باشد.");
        if (m.Audit.Count > 500) throw new InvalidDataException("تاریخچه تغییرات بیش از حد مجاز است.");
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
    public static bool ValidDate(string value) => value.Length == 8 && value.All(char.IsDigit) && ValidMonth(value[..4] + "/" + value[4..6]) && int.TryParse(value[6..], out var day) && day is >= 1 and <= 31;
    public static string MonthOf(string date)
    {
        date = Digits(date);
        if (!ValidDate(date)) throw new InvalidDataException("تاریخ باید مانند 14050421 باشد.");
        return date[..4] + "/" + date[4..6];
    }
    public static decimal NetPurchase(Purchase p) => p.Total - p.Deductions - (p.Total * p.BrandDiscount) - (p.Total * p.Offer);
    public static decimal NetSaleBase(Sale s) => s.Total - s.Deductions;
    public static decimal CashDiscountAmount(Sale s) => NetSaleBase(s) * s.CashShare * s.CashDiscount;
    public static (decimal Cash, decimal Credit) SplitSale(Sale s)
    {
        var baseSale = NetSaleBase(s);
        var cash = baseSale * s.CashShare * (1 - s.CashDiscount);
        var credit = baseSale * s.CreditShare;
        return (cash, credit);
    }
    public static void Validate(Brand b)
    {
        if (string.IsNullOrWhiteSpace(b.Name) || b.Name.Trim().Length > 120) throw new InvalidDataException("نام برند نامعتبر است.");
        if (new[] { b.PurchaseDiscount, b.Offer, b.Markup, b.CreditShare, b.CashShare, b.CashDiscount }.Any(x => x < 0 || x > 1)) throw new InvalidDataException("درصدهای برند باید بین صفر و ۱۰۰ باشند.");
        if (b.PurchaseDiscount + b.Offer > 1) throw new InvalidDataException("جمع تخفیف خرید و آفر نباید از ۱۰۰٪ بیشتر باشد.");
        if (b.CashShare + b.CreditShare != 1) throw new InvalidDataException("جمع سهم نقدی و چکی باید دقیقاً ۱۰۰٪ باشد.");
    }
    public static void Validate(CatalogItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 60 || !item.Code.All(char.IsLetterOrDigit)) throw new InvalidDataException("کد کالا نامعتبر است.");
        if (string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 250 || string.IsNullOrWhiteSpace(item.Brand)) throw new InvalidDataException("نام کالا و برند الزامی هستند.");
    }
    public static void Validate(Purchase p)
    {
        if (!ValidDate(p.Date) || string.IsNullOrWhiteSpace(p.Code) || p.Quantity <= 0 || p.Total <= 0 || p.Deductions < 0 || p.BrandDiscount < 0 || p.Offer < 0 || NetPurchase(p) < 0) throw new InvalidDataException("اطلاعات خرید نامعتبر است.");
    }
    public static void Validate(Sale s)
    {
        if (!ValidDate(s.Date) || string.IsNullOrWhiteSpace(s.Code) || s.Quantity <= 0 || s.Total <= 0 || s.Deductions < 0 || s.CashShare < 0 || s.CreditShare < 0 || s.CashDiscount < 0 || s.CashShare + s.CreditShare != 1 || s.CashDiscount > 1 || NetSaleBase(s) < 0) throw new InvalidDataException("اطلاعات فروش نامعتبر است.");
    }
    public static void Validate(OpeningLot lot)
    {
        if (string.IsNullOrWhiteSpace(lot.Id) || string.IsNullOrWhiteSpace(lot.Code) || lot.Code.Length > 60 || !lot.Code.All(char.IsLetterOrDigit) || !ValidDate(lot.SourceDate) || lot.Quantity <= 0 || lot.UnitCost < 0)
            throw new InvalidDataException("موجودی افتتاحیه نامعتبر است.");
    }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static Month Sample(string key) => new() { Key = key, Products = new[] { "فیکورس", "سالومه", "لاکچری", "کاسه ای", "شامپو لاکچری", "ضد آفتاب" }.Select((b, i) => new Product { Brand = b, Name = "کالای نمونه " + (i + 1), Price = (i + 1) * 1_000_000m, Quantity = i == 4 ? 2 : 1 }).ToList() };
}
