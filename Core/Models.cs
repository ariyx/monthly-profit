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

// کالا با کد شناخته می‌شود؛ این رکورد هویت برند و مقادیر اولیهٔ اولین ماه را نگه می‌دارد.
public sealed record Brand
{
    public string Name { get; init; } = "";
    // هر مورد فقط رقم است؛ برای نمونه «106». چند پیشوند برای یک برند مجاز است.
    public List<string> CodePrefixes { get; init; } = [];
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; } = .04m;
    public decimal CreditShare { get; init; } = .70m;
    public decimal CashShare { get; init; } = .30m;
    public decimal CashDiscount { get; init; } = .05m;
}

// درصدهای مالی برند از هویت و پیشوندهای آن جدا و برای هر ماه نسخه‌بندی می‌شوند.
// مقادیر روی Brand فقط الگوی اولیهٔ اولین ماه هستند؛ محاسبات جدید صرفاً از
// BrandMonthSettings تأییدشده استفاده می‌کنند.
public sealed record BrandRate
{
    public string BrandName { get; init; } = "";
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; } = .04m;
    public decimal CreditShare { get; init; } = .70m;
    public decimal CashShare { get; init; } = .30m;
    public decimal CashDiscount { get; init; } = .05m;
}

public sealed record BrandMonthSettings
{
    public string MonthKey { get; init; } = "";
    public string SourceMonthKey { get; init; } = "";
    public bool IsConfirmed { get; init; }
    public DateTime? ConfirmedAtUtc { get; init; }
    public List<BrandRate> Rates { get; init; } = [];
}

public static class BrandMonthRules
{
    public static BrandRate FromBrand(Brand brand) => new()
    {
        BrandName = brand.Name,
        PurchaseDiscount = brand.PurchaseDiscount,
        Offer = brand.Offer,
        Markup = brand.Markup,
        CreditShare = brand.CreditShare,
        CashShare = brand.CashShare,
        CashDiscount = brand.CashDiscount
    };

    public static void Validate(BrandRate rate)
    {
        if (string.IsNullOrWhiteSpace(rate.BrandName) || rate.BrandName.Trim().Length > 120) throw new InvalidDataException("نام برند در تنظیمات ماهانه نامعتبر است.");
        if (new[] { rate.PurchaseDiscount, rate.Offer, rate.Markup, rate.CreditShare, rate.CashShare, rate.CashDiscount }.Any(x => x < 0 || x > 1)) throw new InvalidDataException("درصدهای ماهانه برند باید بین صفر و ۱۰۰ باشند.");
        if (rate.PurchaseDiscount + rate.Offer > 1) throw new InvalidDataException("جمع تخفیف خرید و آفر نباید از ۱۰۰٪ بیشتر باشد.");
        if (rate.CashShare + rate.CreditShare != 1) throw new InvalidDataException("جمع سهم نقدی و چکی باید دقیقاً ۱۰۰٪ باشد.");
    }

    public static BrandMonthSettings DraftFor(Ledger ledger, string monthKey)
    {
        if (!Rules.ValidMonth(monthKey)) throw new InvalidDataException("ماه تنظیمات برند نامعتبر است.");
        var exact = ledger.BrandMonths.FirstOrDefault(x => x.MonthKey == monthKey);
        if (exact != null)
        {
            var exactRates = exact.Rates.ToDictionary(x => Rules.Normalize(x.BrandName), StringComparer.OrdinalIgnoreCase);
            var merged = ledger.Brands.Select(brand => exactRates.TryGetValue(Rules.Normalize(brand.Name), out var saved)
                ? saved with { BrandName = brand.Name }
                : FromBrand(brand)).ToList();
            var complete = merged.Count == exact.Rates.Count && merged.All(x => exactRates.ContainsKey(Rules.Normalize(x.BrandName)));
            return exact with { IsConfirmed = exact.IsConfirmed && complete, Rates = merged };
        }
        var source = ledger.BrandMonths.Where(x => x.IsConfirmed && string.CompareOrdinal(x.MonthKey, monthKey) < 0)
            .OrderByDescending(x => x.MonthKey, StringComparer.Ordinal).FirstOrDefault();
        var sourceRates = source?.Rates.ToDictionary(x => Rules.Normalize(x.BrandName), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, BrandRate>(StringComparer.OrdinalIgnoreCase);
        var rates = ledger.Brands.Select(brand => sourceRates.TryGetValue(Rules.Normalize(brand.Name), out var inherited)
            ? inherited with { BrandName = brand.Name }
            : FromBrand(brand)).ToList();
        return new BrandMonthSettings { MonthKey = monthKey, SourceMonthKey = source?.MonthKey ?? "تنظیمات اولیه", Rates = rates };
    }

    public static bool IsConfirmed(Ledger ledger, string monthKey)
        => DraftFor(ledger, monthKey).IsConfirmed;

    public static BrandRate RequireConfirmed(Ledger ledger, string brandName, string monthKey)
    {
        var settings = DraftFor(ledger, monthKey);
        if (!settings.IsConfirmed) throw new InvalidOperationException($"تنظیمات برندهای ماه {monthKey} هنوز تأیید نشده است.");
        return settings.Rates.FirstOrDefault(x => Rules.Normalize(x.BrandName).Equals(Rules.Normalize(brandName), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"در تنظیمات تأییدشده ماه {monthKey}، برند «{brandName}» وجود ندارد.");
    }

    public static BrandRate? TryConfirmed(Ledger ledger, string brandName, string monthKey)
    {
        var settings = DraftFor(ledger, monthKey);
        return settings.IsConfirmed ? settings.Rates.FirstOrDefault(x => Rules.Normalize(x.BrandName).Equals(Rules.Normalize(brandName), StringComparison.OrdinalIgnoreCase)) : null;
    }

    public static Ledger Confirm(Ledger ledger, string monthKey, IEnumerable<BrandRate> values)
    {
        var rates = values.Select(x => x with { BrandName = Rules.Normalize(x.BrandName) }).ToList();
        foreach (var rate in rates) Validate(rate);
        var expected = ledger.Brands.Select(x => Rules.Normalize(x.Name)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var actual = rates.Select(x => Rules.Normalize(x.BrandName)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        if (!expected.SequenceEqual(actual, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("جدول ماهانه باید دقیقاً یک ردیف برای هر برند فعال داشته باشد.");
        if (actual.Distinct(StringComparer.OrdinalIgnoreCase).Count() != actual.Count) throw new InvalidDataException("یک برند در جدول ماهانه تکرار شده است.");
        var draft = DraftFor(ledger, monthKey);
        var confirmed = new BrandMonthSettings { MonthKey = monthKey, SourceMonthKey = draft.SourceMonthKey, IsConfirmed = true, ConfirmedAtUtc = DateTime.UtcNow, Rates = rates };
        var staged = ledger with { BrandMonths = [.. ledger.BrandMonths.Where(x => x.MonthKey != monthKey), confirmed] };
        var items = staged.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        BrandRate RateForCode(string code)
        {
            if (!items.TryGetValue(code, out var item)) throw new InvalidDataException($"کالای کد {code} برای اعمال تنظیمات ماهانه یافت نشد.");
            return RequireConfirmed(staged, item.Brand, monthKey);
        }
        var sales = staged.Sales.Select(sale => Rules.MonthOf(sale.Date) != monthKey ? sale : Apply(sale, RateForCode(sale.Code), monthKey)).ToList();
        var saleById = sales.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var purchases = staged.Purchases.Select(purchase =>
        {
            if (Rules.MonthOf(purchase.Date) != monthKey) return purchase;
            if (!string.IsNullOrWhiteSpace(purchase.AdjustmentSaleId) && saleById.TryGetValue(purchase.AdjustmentSaleId, out var target))
            {
                var gross = target.UnitPrice / (1 + target.Markup);
                return purchase with { UnitPrice = gross, Total = purchase.Quantity * gross, BrandDiscount = target.PurchaseDiscount, Offer = target.Offer, RateMonthKey = monthKey };
            }
            if (purchase.IsAdjustment) return purchase;
            var rate = RateForCode(purchase.Code);
            return purchase with { BrandDiscount = rate.PurchaseDiscount, Offer = rate.Offer, RateMonthKey = monthKey };
        }).ToList();
        return staged with { Sales = sales, Purchases = purchases };
    }

    static Sale Apply(Sale sale, BrandRate rate, string monthKey) => sale with
    {
        CashShare = rate.CashShare,
        CreditShare = rate.CreditShare,
        CashDiscount = rate.CashDiscount,
        PurchaseDiscount = rate.PurchaseDiscount,
        Offer = rate.Offer,
        Markup = rate.Markup,
        RateMonthKey = monthKey
    };
}

public static class BrandPrefixRules
{
    // نگاشت پیشوند سراسری است؛ درصدهای مالی از این بخش جدا و ماهانه‌اند.
    // تشخیص در زمان ورود، تنها از CodePrefixes ذخیره‌شده در خود برند استفاده می‌کند.
    static readonly IReadOnlyDictionary<string, string[]> DefaultPrefixes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["Fikores"] = ["106"], ["پیکشن"] = ["108"], ["آذر بیوتی"] = ["112"], ["2080"] = ["115", "145"], ["سالومه"] = ["118"],
        ["مکسی بل"] = ["128"], ["پلیس"] = ["137"], ["سوپکس"] = ["147"], ["پانته‌آ"] = ["161"], ["Blue Night"] = ["162"]
    };

    public static List<string> Parse(string? value) => (value ?? "").Split([',', '،', ';', '؛', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Rules.Digits).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    public static string Display(IEnumerable<string> prefixes) => string.Join("، ", prefixes);

    public static Brand? Detect(IEnumerable<Brand> brands, string code)
    {
        var normalizedCode = Rules.Digits(code);
        return brands.SelectMany(brand => brand.CodePrefixes.Select(prefix => (Brand: brand, Prefix: Rules.Digits(prefix))))
            .Where(x => x.Prefix.Length > 0 && normalizedCode.StartsWith(x.Prefix, StringComparison.Ordinal))
            .OrderByDescending(x => x.Prefix.Length).Select(x => x.Brand).FirstOrDefault();
    }

    public static void ValidateUnique(IEnumerable<Brand> brands)
    {
        var duplicate = brands.SelectMany(brand => brand.CodePrefixes.Select(prefix => (Prefix: Rules.Digits(prefix), Brand: Rules.Normalize(brand.Name))))
            .Where(x => x.Prefix.Length > 0).GroupBy(x => x.Prefix).FirstOrDefault(x => x.Select(y => y.Brand).Distinct().Count() > 1);
        if (duplicate != null) throw new InvalidDataException("پیشوند کد «" + duplicate.Key + "» برای بیش از یک برند ثبت شده است.");
    }

    public static Ledger EnsureDefaults(Ledger ledger)
    {
        if (ledger.BrandRulesInitialized) return ledger;
        var brands = ledger.Brands.ToList();
        foreach (var (name, prefixes) in DefaultPrefixes)
        {
            var index = brands.FindIndex(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(name), StringComparison.OrdinalIgnoreCase));
            if (index < 0) brands.Add(new Brand { Name = name, CodePrefixes = [.. prefixes] });
            else if (brands[index].CodePrefixes.Count == 0) brands[index] = brands[index] with { CodePrefixes = [.. prefixes] };
        }
        return ledger with { Brands = brands, BrandRulesInitialized = true };
    }
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
    // تعدیل مغایرت به یک فروش مشخص متصل است تا قیمت فروش‌های دیگر روی آن اثر نگذارد.
    public string AdjustmentSaleId { get; init; } = "";
    public string RateMonthKey { get; init; } = "";
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
    // Snapshot کامل قواعد برند برای محاسبهٔ بعدی مغایرت همین فروش.
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; }
    public string RateMonthKey { get; init; } = "";
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
    public bool BrandRulesInitialized { get; init; }
    public List<Brand> Brands { get; init; } = [];
    public List<BrandMonthSettings> BrandMonths { get; init; } = [];
    public List<CatalogItem> Items { get; init; } = [];
    public List<Purchase> Purchases { get; init; } = [];
    public List<Sale> Sales { get; init; } = [];
    public List<OpeningLot> OpeningLots { get; init; } = [];
    public List<string> ImportedRows { get; init; } = [];
    public List<PendingBrandTransaction> PendingBrandTransactions { get; init; } = [];
}

// ردیف واردشده‌ای که برندش هنوز قاعدهٔ سراسری ندارد؛ تا زمان تعیین برند در دفتر فروش/خرید ثبت نمی‌شود.
public sealed record PendingBrandTransaction
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public TransactionKind Kind { get; init; }
    public ImportedTransaction Transaction { get; init; } = new(0, "", "", "", "", "", 0, 0, 0, 0);
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
        var unitSale = SuggestedSaleUnitPrice(p.Price, p.Markup);
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
    // برای ورودی‌ها و مقدار/تعداد، دقت اصلی حفظ می‌شود. گزارش‌های مالی اما به ریال
    // نمایش داده می‌شوند و اعشار صرفاً نویزِ محاسباتی است.
    public static string Money(decimal n) => n.ToString("#,##0.##", CultureInfo.InvariantCulture);
    public static string ReportMoney(decimal n) => decimal.Round(n, 0, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture);
    public static string Percent(decimal? n) => n.HasValue ? (n.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "٪" : "تعریف‌نشده";
    public static bool ValidDate(string value) => value.Length == 8 && value.All(char.IsDigit) && ValidMonth(value[..4] + "/" + value[4..6]) && int.TryParse(value[6..], out var day) && day is >= 1 and <= 31;
    public static string MonthOf(string date)
    {
        date = Digits(date);
        if (!ValidDate(date)) throw new InvalidDataException("تاریخ باید مانند 14050421 باشد.");
        return date[..4] + "/" + date[4..6];
    }
    public static decimal NetPurchase(Purchase p) => p.Total - p.Deductions - (p.Total * p.BrandDiscount) - (p.Total * p.Offer);
    public static decimal SuggestedSaleUnitPrice(decimal grossPurchaseUnitPrice, decimal markup) => grossPurchaseUnitPrice * (1 + markup);
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
        if (b.CodePrefixes.Any(x => x.Length < 2 || x.Length > 12 || !x.All(char.IsDigit))) throw new InvalidDataException("پیشوند کد برند باید فقط رقم و بین ۲ تا ۱۲ رقم باشد.");
        if (b.CodePrefixes.Select(Rules.Digits).Distinct(StringComparer.Ordinal).Count() != b.CodePrefixes.Count) throw new InvalidDataException("پیشوند کد در یک برند تکراری است.");
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
        if (!string.IsNullOrWhiteSpace(p.RateMonthKey) && (!ValidMonth(p.RateMonthKey) || p.RateMonthKey != MonthOf(p.Date))) throw new InvalidDataException("ماه Snapshot خرید با تاریخ آن سازگار نیست.");
        if (!string.IsNullOrWhiteSpace(p.AdjustmentSaleId) && !p.IsAdjustment) throw new InvalidDataException("فقط تعدیل مغایرت می‌تواند به فروش متصل باشد.");
    }
    public static void Validate(Sale s)
    {
        if (!ValidDate(s.Date) || string.IsNullOrWhiteSpace(s.Code) || s.Quantity <= 0 || s.Total <= 0 || s.Deductions < 0 || s.CashShare < 0 || s.CreditShare < 0 || s.CashDiscount < 0 || s.PurchaseDiscount < 0 || s.Offer < 0 || s.Markup < 0 || s.CashShare + s.CreditShare != 1 || s.CashDiscount > 1 || s.PurchaseDiscount + s.Offer > 1 || s.Markup > 1 || NetSaleBase(s) < 0) throw new InvalidDataException("اطلاعات فروش نامعتبر است.");
        if (!string.IsNullOrWhiteSpace(s.RateMonthKey) && (!ValidMonth(s.RateMonthKey) || s.RateMonthKey != MonthOf(s.Date))) throw new InvalidDataException("ماه Snapshot فروش با تاریخ آن سازگار نیست.");
    }
    public static void Validate(OpeningLot lot)
    {
        if (string.IsNullOrWhiteSpace(lot.Id) || string.IsNullOrWhiteSpace(lot.Code) || lot.Code.Length > 60 || !lot.Code.All(char.IsLetterOrDigit) || !ValidDate(lot.SourceDate) || lot.Quantity <= 0 || lot.UnitCost < 0)
            throw new InvalidDataException("موجودی افتتاحیه نامعتبر است.");
    }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static Month Sample(string key) => new() { Key = key, Products = new[] { "فیکورس", "سالومه", "لاکچری", "کاسه ای", "شامپو لاکچری", "ضد آفتاب" }.Select((b, i) => new Product { Brand = b, Name = "کالای نمونه " + (i + 1), Price = (i + 1) * 1_000_000m, Quantity = i == 4 ? 2 : 1 }).ToList() };
}
