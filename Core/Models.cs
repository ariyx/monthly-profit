using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Profit.Core;

public sealed record Month
{
    public string Key { get; init; } = "";
    public decimal FixedCost { get; set; } = 52_000_000m;
    public List<FixedExpense> FixedExpenses { get; set; } = [];
    public int Revision { get; set; }
    public bool IsClosed { get; set; }
    public List<AuditEntry> Audit { get; set; } = [];
    public int FormulaVersion { get; init; } = 2;
}

public sealed record FixedExpense { public string Id { get; init; } = Guid.NewGuid().ToString("N"); public string Title { get; init; } = ""; public decimal Amount { get; init; } }
public sealed record AuditEntry { public DateTime AtUtc { get; init; } = DateTime.UtcNow; public string Action { get; init; } = ""; }
public sealed record Preferences { public bool AutoBackupOnExit { get; init; } public int AutoBackupKeep { get; init; } = 8; public DateTime? LastAutoBackupUtc { get; init; } public DateTime? LastBackupUtc { get; init; } public string? LastBackupPath { get; init; } }

// هویت برند و مقادیر پایه برای اولین تنظیم ماهانه.
public sealed record Brand
{
    public bool IsActive { get; init; } = true;
    public string Name { get; init; } = "";
    public List<string> CodePrefixes { get; init; } = [];
    public List<string> ExactProductCodes { get; init; } = [];
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; } = .04m;
    public decimal CreditShare { get; init; } = .70m;
    public decimal CashShare { get; init; } = .30m;
    public decimal CashDiscount { get; init; } = .05m;
}

public sealed record BrandRate
{
    public bool? IsConfirmed { get; init; }
    public string SourceMonthKey { get; init; } = "";
    public DateTime? ConfirmedAtUtc { get; init; }
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


public static class BrandPrefixRules
{
    public static IReadOnlyList<Brand> Defaults { get; } =
    [
        new() { Name = "2080", CodePrefixes = ["115", "145"], Markup = .50m },
        new() { Name = "Blue Night", CodePrefixes = ["162"], Markup = .40m },
        new() { Name = "Fikores", CodePrefixes = ["106"], PurchaseDiscount = .25m, Offer = .05m, Markup = .04m },
        new() { Name = "آذر بیوتی", CodePrefixes = ["112"], Markup = .35m },
        new() { Name = "پانتا", CodePrefixes = ["161"], Markup = .40m },
        new() { Name = "پلیس", CodePrefixes = ["137"], Markup = .30m },
        new() { Name = "پیکش", CodePrefixes = ["108"], PurchaseDiscount = .20m, Offer = .25m, Markup = .05m },
        new() { Name = "سالومه", CodePrefixes = ["118"], Markup = .04m },
        new() { Name = "سویکس", CodePrefixes = ["147"], Markup = .40m },
        new() { Name = "مکسی بل", CodePrefixes = ["128"], Markup = .40m }
    ];

    public static List<string> Parse(string? value) => (value ?? "").Split([',', '،', ';', '؛', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Rules.Digits).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    public static string Display(IEnumerable<string> prefixes) => string.Join("، ", prefixes);
    public static Brand? Detect(IEnumerable<Brand> brands, string code)
    {
        var normalizedCode = Rules.Digits(code);
        var allBrands = brands.ToList();
        var exact = DetectExact(allBrands, normalizedCode);
        return exact ?? allBrands.SelectMany(b => b.CodePrefixes.Select(p => (Brand: b, Prefix: Rules.Digits(p))))
            .Where(x => x.Prefix.Length > 0 && normalizedCode.StartsWith(x.Prefix, StringComparison.Ordinal)).OrderByDescending(x => x.Prefix.Length).Select(x => x.Brand).FirstOrDefault();
    }
    public static Brand? DetectExact(IEnumerable<Brand> brands, string code)
    {
        var normalizedCode = Rules.Digits(code);
        return brands.FirstOrDefault(b => b.ExactProductCodes.Any(x => Rules.Digits(x).Equals(normalizedCode, StringComparison.Ordinal)));
    }
    public static Brand? DetectActive(IEnumerable<Brand> brands, string code)
    {
        var brand = Detect(brands, code);
        return brand?.IsActive == true ? brand : null;
    }
    public static void ValidateUnique(IEnumerable<Brand> brands)
    {
        var duplicate = brands.SelectMany(b => b.CodePrefixes.Select(p => (Prefix: Rules.Digits(p), Brand: Rules.Normalize(b.Name)))).Where(x => x.Prefix.Length > 0).GroupBy(x => x.Prefix).FirstOrDefault(x => x.Select(y => y.Brand).Distinct().Count() > 1);
        if (duplicate != null) throw new InvalidDataException($"پیشوند کد «{duplicate.Key}» برای بیش از یک برند ثبت شده است.");
        var duplicateExact = brands.SelectMany(b => b.ExactProductCodes.Select(c => (Code: Rules.Digits(c), Brand: Rules.Normalize(b.Name)))).Where(x => x.Code.Length > 0).GroupBy(x => x.Code).FirstOrDefault(x => x.Select(y => y.Brand).Distinct().Count() > 1);
        if (duplicateExact != null) throw new InvalidDataException($"کد کالای دقیق «{duplicateExact.Key}» برای بیش از یک برند ثبت شده است.");
    }
    public static Ledger EnsureDefaults(Ledger ledger)
    {
        if (ledger.BrandRulesInitialized) return ledger;
        var brands = ledger.Brands.ToList();
        foreach (var seed in Defaults)
        {
            var index = brands.FindIndex(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(seed.Name), StringComparison.OrdinalIgnoreCase));
            if (index < 0) brands.Add(seed);
            else if (brands[index].CodePrefixes.Count == 0) brands[index] = brands[index] with { CodePrefixes = seed.CodePrefixes };
        }
        return ledger with { Brands = brands, BrandRulesInitialized = true };
    }
}

// Brand خالی یعنی کد ناشناخته است: ردیف فروش نگه‌داری می‌شود ولی هنوز قابل محاسبه نیست.
public sealed record CatalogItem { public string Code { get; init; } = ""; public string Name { get; init; } = ""; public string Brand { get; init; } = ""; }
public sealed record Sale
{
    public decimal? MarkupOverride { get; init; }
    public string MarkupOverrideReason { get; init; } = "";
    public string InvoiceNumber { get; init; } = "";
    public int SourceRow { get; init; }
    public string SourceIdentity { get; init; } = "";
    public string SourceFingerprint { get; init; } = "";
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Date { get; init; } = "";
    public string Code { get; init; } = "";
    public string Customer { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Total { get; init; }
    public decimal Deductions { get; init; }
    public decimal CreditShare { get; init; }
    public decimal CashShare { get; init; }
    public decimal CashDiscount { get; init; }
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; }
    public string RateMonthKey { get; init; } = "";
    public decimal? EstimatedCostOverride { get; init; }
    public string EstimatedCostOverrideNote { get; init; } = "";
    public string ImportKey { get; init; } = "";
}

// برگشت فروش جدا از فروش اصلی نگه‌داری می‌شود. اثر آن در زمان محاسبه و فقط
// روی فروش‌های قابل تطبیق اعمال می‌شود؛ بنابراین با اصلاح هر فروش، تطبیق‌ها
// نیز بدون نگه‌داری وضعیتِ ناسازگار دوباره ساخته می‌شوند.
public sealed record SaleReturn
{
    public string InvoiceNumber { get; init; } = "";
    public int SourceRow { get; init; }
    public string SourceIdentity { get; init; } = "";
    public string SourceFingerprint { get; init; } = "";
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Date { get; init; } = "";
    public string Code { get; init; } = "";
    public string Customer { get; init; } = "";
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal Total { get; init; }
    public decimal Deductions { get; init; }
    public string ImportKey { get; init; } = "";
}

public sealed record Ledger
{
    public List<BrandChangeEntry> BrandChanges { get; init; } = [];
    public List<MarkupPeriod> MarkupPeriods { get; init; } = [];
    public List<OfferEntry> Offers { get; init; } = [];
    public bool BrandRulesInitialized { get; init; }
    public List<Brand> Brands { get; init; } = [];
    public List<BrandMonthSettings> BrandMonths { get; init; } = [];
    public List<CatalogItem> Items { get; init; } = [];
    public List<Sale> Sales { get; init; } = [];
    public List<SaleReturn> Returns { get; init; } = [];
    public List<string> ImportedRows { get; init; } = [];
    public List<string> ImportedReturnRows { get; init; } = [];
}

public sealed record SaleSettlement(decimal Cost, decimal Cash, decimal Credit, decimal GrossPurchaseUnit, decimal NetCostUnit, bool HasManualCost)
{
    public decimal Sales => Cash + Credit;
    public decimal Profit => Sales - Cost;
    public decimal InvoiceBase { get; init; }
    public decimal ReturnedQuantity { get; init; }
    public int ReturnCount { get; init; }
}
public sealed record LedgerTotal(decimal Cost, decimal Cash, decimal Credit, decimal Profit, decimal FixedCost, decimal Quantity, int Products, int Brands)
{
    public decimal Sales => Cash + Credit + UnlinkedOfferCredit;
    public decimal UnlinkedOfferCredit { get; init; }
    public decimal InvoiceSales { get; init; }
    public decimal CashDiscountAmount { get; init; }
    public int PendingSalesCount { get; init; }
    public decimal PendingInvoiceSales { get; init; }
    public decimal ReturnedQuantity { get; init; }
    public int AppliedReturnsCount { get; init; }
    public int PendingReturnsCount { get; init; }
    public decimal Net => Profit - FixedCost;
    public decimal? Margin => Sales == 0 ? null : Profit / Sales;
}

public static class Rules
{
    public static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormKC).Replace('ي', 'ی').Replace('ك', 'ک');
    public static string MatchText(string value) => string.Join(" ", Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    public static string Digits(string s) { for (var i = 0; i < 10; i++) s = s.Replace((char)('۰' + i), (char)('0' + i)).Replace((char)('٠' + i), (char)('0' + i)); return s.Replace("٬", "").Replace(",", "").Replace('٫', '.').Trim(); }
    public static decimal Number(string s) => decimal.TryParse(Digits(s), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException("عدد نامعتبر است: " + s);
    public static string Money(decimal n) => n.ToString("#,##0.##", CultureInfo.InvariantCulture);
    public static string ReportMoney(decimal n) => decimal.Round(n, 0, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture);
    public static string Percent(decimal? n) => n.HasValue ? (n.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "٪" : "تعریف‌نشده";
    public static bool ValidMonth(string key) => key.Length == 7 && key[4] == '/' && int.TryParse(key[..4], out var y) && y is >= 1300 and <= 1600 && int.TryParse(key[5..], out var m) && m is >= 1 and <= 12;
    public static bool ValidDate(string value)
    {
        if (value.Length != 8 || !value.All(char.IsDigit) || !ValidMonth(value[..4] + "/" + value[4..6]) || !int.TryParse(value[6..], out var day)) return false;
        var calendar = new PersianCalendar();
        return day >= 1 && day <= calendar.GetDaysInMonth(int.Parse(value[..4], CultureInfo.InvariantCulture), int.Parse(value[4..6], CultureInfo.InvariantCulture));
    }
    public static string LastDate(string month)
    {
        if (!ValidMonth(month)) throw new InvalidDataException("ماه نامعتبر است.");
        var days = new PersianCalendar().GetDaysInMonth(int.Parse(month[..4], CultureInfo.InvariantCulture), int.Parse(month[5..], CultureInfo.InvariantCulture));
        return month.Replace("/", "") + days.ToString("00", CultureInfo.InvariantCulture);
    }
    public static string PreviousDate(string date)
    {
        if (!ValidDate(date)) throw new InvalidDataException("تاریخ نامعتبر است.");
        var calendar = new PersianCalendar();
        var previous = calendar.ToDateTime(int.Parse(date[..4], CultureInfo.InvariantCulture), int.Parse(date[4..6], CultureInfo.InvariantCulture), int.Parse(date[6..], CultureInfo.InvariantCulture), 0, 0, 0, 0).AddDays(-1);
        return $"{calendar.GetYear(previous):0000}{calendar.GetMonth(previous):00}{calendar.GetDayOfMonth(previous):00}";
    }
    public static string MonthOf(string date) { date = Digits(date); if (!ValidDate(date)) throw new InvalidDataException("تاریخ باید مانند 14050421 باشد."); return date[..4] + "/" + date[4..6]; }
    public static decimal NetSaleBase(Sale sale) => sale.Total - sale.Deductions;
    public static decimal ReturnNetBase(SaleReturn saleReturn) => saleReturn.Total - saleReturn.Deductions;
    public static decimal CashDiscountAmount(Sale sale, decimal? invoiceBase = null) => (invoiceBase ?? NetSaleBase(sale)) * sale.CashShare * sale.CashDiscount;
    public static (decimal Cash, decimal Credit) SplitSale(Sale sale, decimal? invoiceBase = null)
    {
        var baseAmount = invoiceBase ?? NetSaleBase(sale);
        return (baseAmount * sale.CashShare * (1 - sale.CashDiscount), baseAmount * sale.CreditShare);
    }
    public static decimal GrossPurchaseUnitFromSale(Sale sale) => sale.UnitPrice / (1 + sale.Markup);
    public static decimal EstimatedNetCostUnit(Sale sale) => GrossPurchaseUnitFromSale(sale) * (1 - sale.PurchaseDiscount - sale.Offer);
    public static decimal EstimatedCost(Sale sale) => sale.EstimatedCostOverride ?? EstimatedNetCostUnit(sale) * sale.Quantity;
    public static bool HasRateSnapshot(Sale sale) => !string.IsNullOrWhiteSpace(sale.RateMonthKey);
    public static void Validate(Month month)
    {
        if (!ValidMonth(month.Key) || month.FormulaVersion != 2 || month.FixedCost < 0 || month.FixedCost > 1_000_000_000_000_000m || month.FixedExpenses.Count > 100) throw new InvalidDataException("اطلاعات ماه نامعتبر است.");
        if (month.FixedExpenses.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Amount < 0) || (month.FixedExpenses.Count > 0 && month.FixedExpenses.Sum(x => x.Amount) != month.FixedCost)) throw new InvalidDataException("ریز هزینه‌های ثابت نامعتبر است.");
    }
    public static void Validate(Brand brand)
    {
        if (string.IsNullOrWhiteSpace(brand.Name) || brand.Name.Trim().Length > 120 || brand.CodePrefixes.Any(x => x.Length < 2 || x.Length > 12 || !x.All(char.IsDigit)) || brand.ExactProductCodes.Any(x => x.Length == 0 || x.Length > 60 || !x.All(char.IsDigit))) throw new InvalidDataException("مشخصات برند نامعتبر است.");
        BrandMonthRules.Validate(BrandMonthRules.FromBrand(brand));
    }
    public static void Validate(CatalogItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 60 || !item.Code.All(char.IsLetterOrDigit) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 250) throw new InvalidDataException("مشخصات کالا نامعتبر است.");
    }
    public static void Validate(Sale sale)
    {
        if (!ValidDate(sale.Date) || string.IsNullOrWhiteSpace(sale.Code) || sale.Quantity <= 0 || sale.UnitPrice <= 0 || sale.Total <= 0 || sale.Deductions < 0 || NetSaleBase(sale) < 0) throw new InvalidDataException("اطلاعات فروش نامعتبر است.");
        if (sale.MarkupOverride < 0 || (sale.MarkupOverride.HasValue && string.IsNullOrWhiteSpace(sale.MarkupOverrideReason))) throw new InvalidDataException("مارک‌آپ مخصوص فروش باید نامنفی باشد و دلیل داشته باشد.");
        if (!HasRateSnapshot(sale))
        {
            if (sale.EstimatedCostOverride.HasValue) throw new InvalidDataException("هزینهٔ دستی فقط برای فروشِ دارای برند و تنظیمات تأییدشده مجاز است.");
            return;
        }
        if (sale.CashShare < 0 || sale.CreditShare < 0 || sale.CashDiscount < 0 || sale.PurchaseDiscount < 0 || sale.Offer < 0 || sale.Markup < 0 || sale.CashShare + sale.CreditShare != 1 || sale.CashDiscount > 1 || sale.PurchaseDiscount + sale.Offer > 1 || sale.EstimatedCostOverride < 0) throw new InvalidDataException("اطلاعات فروش نامعتبر است.");
        if (sale.RateMonthKey != MonthOf(sale.Date)) throw new InvalidDataException("ماه Snapshot فروش با تاریخ آن سازگار نیست.");
        if (sale.MarkupOverride.HasValue && sale.Markup != sale.MarkupOverride.Value) throw new InvalidDataException("مارک‌آپ محاسبه با مارک‌آپ اختصاصی فروش سازگار نیست.");
        if (sale.EstimatedCostOverride.HasValue && string.IsNullOrWhiteSpace(sale.EstimatedCostOverrideNote)) throw new InvalidDataException("برای هزینهٔ دستی فروش، دلیل را وارد کنید.");
    }
    public static void Validate(SaleReturn saleReturn)
    {
        if (!ValidDate(saleReturn.Date) || string.IsNullOrWhiteSpace(saleReturn.Code) || saleReturn.Quantity <= 0 || saleReturn.UnitPrice <= 0 || saleReturn.Total <= 0 || saleReturn.Deductions < 0 || ReturnNetBase(saleReturn) < 0)
            throw new InvalidDataException("اطلاعات برگشت از فروش نامعتبر است.");
    }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
}
