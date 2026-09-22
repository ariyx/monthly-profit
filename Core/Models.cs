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
    public string Name { get; init; } = "";
    public List<string> CodePrefixes { get; init; } = [];
    public decimal PurchaseDiscount { get; init; }
    public decimal Offer { get; init; }
    public decimal Markup { get; init; } = .04m;
    public decimal CreditShare { get; init; } = .70m;
    public decimal CashShare { get; init; } = .30m;
    public decimal CashDiscount { get; init; } = .05m;
}

public sealed record BrandRate
{
    public string BrandName { get; init; } = "";
    // تأیید درصدها مستقل از برندهای دیگر و فقط برای همین ماه است.
    public bool IsConfirmed { get; init; }
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
        BrandName = brand.Name, PurchaseDiscount = brand.PurchaseDiscount, Offer = brand.Offer,
        Markup = brand.Markup, CreditShare = brand.CreditShare, CashShare = brand.CashShare, CashDiscount = brand.CashDiscount
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
            var saved = exact.Rates.ToDictionary(x => Rules.Normalize(x.BrandName), StringComparer.OrdinalIgnoreCase);
            // تنظیمات ذخیره‌شده پیش از این نسخه، تأییدشان در سطح ماه بوده است؛
            // آن‌ها را یک‌بار معادل تأیید همهٔ برندهای موجود همان ماه در نظر می‌گیریم.
            var rates = ledger.Brands.Select(b => saved.TryGetValue(Rules.Normalize(b.Name), out var rate)
                ? rate with { BrandName = b.Name, IsConfirmed = rate.IsConfirmed || exact.IsConfirmed }
                : FromBrand(b)).ToList();
            return exact with { Rates = rates, IsConfirmed = rates.All(x => x.IsConfirmed) };
        }
        var source = ledger.BrandMonths.Where(x => x.IsConfirmed && string.CompareOrdinal(x.MonthKey, monthKey) < 0).OrderByDescending(x => x.MonthKey, StringComparer.Ordinal).FirstOrDefault();
        var previous = source?.Rates.ToDictionary(x => Rules.Normalize(x.BrandName), StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, BrandRate>(StringComparer.OrdinalIgnoreCase);
        // ماهی که هنوز تنظیمات مستقل ندارد، با آخرین مقادیر معتبر پر می‌شود
        // اما تأیید درصدها برای همان ماه لازم است.
        var draft = ledger.Brands.Select(b => previous.TryGetValue(Rules.Normalize(b.Name), out var rate) ? rate with { BrandName = b.Name, IsConfirmed = false } : FromBrand(b)).ToList();
        return new BrandMonthSettings { MonthKey = monthKey, SourceMonthKey = source?.MonthKey ?? "تنظیمات پیش‌فرض", Rates = draft };
    }

    public static bool IsConfirmed(Ledger ledger, string monthKey) => DraftFor(ledger, monthKey).IsConfirmed;

    public static IReadOnlyList<BrandRate> UnconfirmedRates(Ledger ledger, string monthKey) =>
        DraftFor(ledger, monthKey).Rates.Where(x => !x.IsConfirmed).ToList();

    public static BrandRate RequireConfirmed(Ledger ledger, string brandName, string monthKey)
    {
        return TryConfirmed(ledger, brandName, monthKey)
            ?? throw new InvalidOperationException($"درصدهای برند «{brandName}» در ماه {monthKey} هنوز تأیید نشده است.");
    }

    public static BrandRate? TryConfirmed(Ledger ledger, string brandName, string monthKey)
    {
        var month = DraftFor(ledger, monthKey);
        return month.Rates.FirstOrDefault(x => x.IsConfirmed && Rules.Normalize(x.BrandName).Equals(Rules.Normalize(brandName), StringComparison.OrdinalIgnoreCase));
    }

    public static Ledger Confirm(Ledger ledger, string monthKey, IEnumerable<BrandRate> values)
    {
        var draft = DraftFor(ledger, monthKey);
        var submitted = values.Select(x => x with { BrandName = Rules.Normalize(x.BrandName), IsConfirmed = true }).ToList();
        foreach (var rate in submitted) Validate(rate);
        var names = ledger.Brands.Select(x => Rules.Normalize(x.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (submitted.Count == 0 || submitted.Any(x => !names.Contains(x.BrandName)) || submitted.Select(x => x.BrandName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != submitted.Count)
            throw new InvalidDataException("تنظیمات تأییدشدهٔ برند نامعتبر است.");
        var replacements = submitted.ToDictionary(x => x.BrandName, StringComparer.OrdinalIgnoreCase);
        var rates = draft.Rates.Select(x => replacements.TryGetValue(Rules.Normalize(x.BrandName), out var next) ? next with { BrandName = x.BrandName } : x).ToList();
        var allConfirmed = rates.All(x => x.IsConfirmed);
        var confirmed = new BrandMonthSettings { MonthKey = monthKey, SourceMonthKey = draft.SourceMonthKey, IsConfirmed = allConfirmed, ConfirmedAtUtc = allConfirmed ? DateTime.UtcNow : draft.ConfirmedAtUtc, Rates = rates };
        var staged = ledger with { BrandMonths = [.. ledger.BrandMonths.Where(x => x.MonthKey != monthKey), confirmed] };
        var items = staged.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var sales = staged.Sales.Select(sale =>
        {
            if (Rules.MonthOf(sale.Date) != monthKey) return sale;
            if (!items.TryGetValue(sale.Code, out var item)) throw new InvalidDataException($"کالای کد {sale.Code} یافت نشد.");
            // فروشِ کالای بی‌برند در دفتر باقی می‌ماند، اما تا زمان تعیین برند محاسبه نمی‌شود.
            if (string.IsNullOrWhiteSpace(item.Brand)) return sale;
            var rate = TryConfirmed(staged, item.Brand, monthKey);
            return rate is null ? sale : Apply(sale, rate, monthKey);
        }).ToList();
        return staged with { Sales = sales };
    }

    public static Ledger AddBrandToMonths(Ledger ledger, IEnumerable<string> monthKeys)
    {
        var keys = monthKeys.Where(Rules.ValidMonth).Concat(ledger.BrandMonths.Select(x => x.MonthKey)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var settings = ledger.BrandMonths.Where(x => !keys.Contains(x.MonthKey, StringComparer.Ordinal)).ToList();
        foreach (var key in keys)
        {
            var draft = DraftFor(ledger, key);
            settings.Add(new BrandMonthSettings
            {
                MonthKey = key,
                SourceMonthKey = draft.SourceMonthKey,
                IsConfirmed = draft.Rates.All(x => x.IsConfirmed),
                ConfirmedAtUtc = draft.Rates.All(x => x.IsConfirmed) ? draft.ConfirmedAtUtc : null,
                Rates = draft.Rates
            });
        }
        return ledger with { BrandMonths = settings };
    }

    // پس از تعیین برندِ یک کد ناشناخته، فروش‌های معلقِ ماه‌های تأییدشده همان کد Snapshot می‌گیرند.
    public static Ledger ApplyPendingSalesForItem(Ledger ledger, string code)
    {
        var item = ledger.Items.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"کالای کد {code} یافت نشد.");
        if (string.IsNullOrWhiteSpace(item.Brand)) return ledger;
        var sales = ledger.Sales.Select(sale =>
        {
            if (!sale.Code.Equals(code, StringComparison.OrdinalIgnoreCase) || HasSnapshot(sale)) return sale;
            var monthKey = Rules.MonthOf(sale.Date);
            var rate = TryConfirmed(ledger, item.Brand, monthKey);
            return rate is null ? sale : Apply(sale, rate, monthKey);
        }).ToList();
        return ledger with { Sales = sales };
    }

    static bool HasSnapshot(Sale sale) => Rules.HasRateSnapshot(sale);

    public static Sale Apply(Sale sale, BrandRate rate, string monthKey) => sale with
    {
        CashShare = rate.CashShare, CreditShare = rate.CreditShare, CashDiscount = rate.CashDiscount,
        PurchaseDiscount = rate.PurchaseDiscount, Offer = rate.Offer, Markup = rate.Markup, RateMonthKey = monthKey
    };
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
    public static string ExtractPrefix(string code)
    {
        var digits = Rules.Digits(code);
        if (digits.Length < 3) throw new InvalidDataException("کد کالا باید حداقل سه رقم داشته باشد تا پیشوند آن استخراج شود.");
        return digits[..3];
    }
    public static Brand? Detect(IEnumerable<Brand> brands, string code) => brands.SelectMany(b => b.CodePrefixes.Select(p => (Brand: b, Prefix: Rules.Digits(p))))
        .Where(x => x.Prefix.Length > 0 && Rules.Digits(code).StartsWith(x.Prefix, StringComparison.Ordinal)).OrderByDescending(x => x.Prefix.Length).Select(x => x.Brand).FirstOrDefault();
    public static void ValidateUnique(IEnumerable<Brand> brands)
    {
        var duplicate = brands.SelectMany(b => b.CodePrefixes.Select(p => (Prefix: Rules.Digits(p), Brand: Rules.Normalize(b.Name)))).Where(x => x.Prefix.Length > 0).GroupBy(x => x.Prefix).FirstOrDefault(x => x.Select(y => y.Brand).Distinct().Count() > 1);
        if (duplicate != null) throw new InvalidDataException($"پیشوند کد «{duplicate.Key}» برای بیش از یک برند ثبت شده است.");
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

public sealed record Ledger
{
    public bool BrandRulesInitialized { get; init; }
    public List<Brand> Brands { get; init; } = [];
    public List<BrandMonthSettings> BrandMonths { get; init; } = [];
    public List<CatalogItem> Items { get; init; } = [];
    public List<Sale> Sales { get; init; } = [];
    public List<string> ImportedRows { get; init; } = [];
}

public sealed record SaleSettlement(decimal Cost, decimal Cash, decimal Credit, decimal GrossPurchaseUnit, decimal NetCostUnit, bool HasManualCost)
{
    public decimal Sales => Cash + Credit;
    public decimal Profit => Sales - Cost;
}
public sealed record LedgerTotal(decimal Cost, decimal Cash, decimal Credit, decimal Profit, decimal FixedCost, decimal Quantity, int Products, int Brands)
{
    public decimal Sales => Cash + Credit;
    public decimal InvoiceSales { get; init; }
    public decimal CashDiscountAmount { get; init; }
    public int PendingSalesCount { get; init; }
    public decimal PendingInvoiceSales { get; init; }
    public decimal Net => Profit - FixedCost;
    public decimal? Margin => Sales == 0 ? null : Profit / Sales;
}

public static class Rules
{
    public static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormKC).Replace('ي', 'ی').Replace('ك', 'ک');
    public static string Digits(string s) { for (var i = 0; i < 10; i++) s = s.Replace((char)('۰' + i), (char)('0' + i)).Replace((char)('٠' + i), (char)('0' + i)); return s.Replace("٬", "").Replace(",", "").Replace('٫', '.').Trim(); }
    public static decimal Number(string s) => decimal.TryParse(Digits(s), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) ? n : throw new FormatException("عدد نامعتبر است: " + s);
    public static string Money(decimal n) => n.ToString("#,##0.##", CultureInfo.InvariantCulture);
    public static string ReportMoney(decimal n) => decimal.Round(n, 0, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture);
    public static string Percent(decimal? n) => n.HasValue ? (n.Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "٪" : "تعریف‌نشده";
    public static bool ValidMonth(string key) => key.Length == 7 && key[4] == '/' && int.TryParse(key[..4], out var y) && y is >= 1300 and <= 1600 && int.TryParse(key[5..], out var m) && m is >= 1 and <= 12;
    public static bool ValidDate(string value) => value.Length == 8 && value.All(char.IsDigit) && ValidMonth(value[..4] + "/" + value[4..6]) && int.TryParse(value[6..], out var day) && day is >= 1 and <= 31;
    public static string MonthOf(string date) { date = Digits(date); if (!ValidDate(date)) throw new InvalidDataException("تاریخ باید مانند 14050421 باشد."); return date[..4] + "/" + date[4..6]; }
    public static decimal NetSaleBase(Sale sale) => sale.Total - sale.Deductions;
    public static decimal CashDiscountAmount(Sale sale) => NetSaleBase(sale) * sale.CashShare * sale.CashDiscount;
    public static (decimal Cash, decimal Credit) SplitSale(Sale sale) => (NetSaleBase(sale) * sale.CashShare * (1 - sale.CashDiscount), NetSaleBase(sale) * sale.CreditShare);
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
        if (string.IsNullOrWhiteSpace(brand.Name) || brand.Name.Trim().Length > 120 || brand.CodePrefixes.Any(x => x.Length < 2 || x.Length > 12 || !x.All(char.IsDigit))) throw new InvalidDataException("مشخصات برند نامعتبر است.");
        BrandMonthRules.Validate(BrandMonthRules.FromBrand(brand));
    }
    public static void Validate(CatalogItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Code) || item.Code.Length > 60 || !item.Code.All(char.IsLetterOrDigit) || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 250) throw new InvalidDataException("مشخصات کالا نامعتبر است.");
    }
    public static void Validate(Sale sale)
    {
        if (!ValidDate(sale.Date) || string.IsNullOrWhiteSpace(sale.Code) || sale.Quantity <= 0 || sale.UnitPrice <= 0 || sale.Total <= 0 || sale.Deductions < 0 || NetSaleBase(sale) < 0) throw new InvalidDataException("اطلاعات فروش نامعتبر است.");
        if (!HasRateSnapshot(sale))
        {
            if (sale.EstimatedCostOverride.HasValue) throw new InvalidDataException("هزینهٔ دستی فقط برای فروشِ دارای برند و تنظیمات تأییدشده مجاز است.");
            return;
        }
        if (sale.CashShare < 0 || sale.CreditShare < 0 || sale.CashDiscount < 0 || sale.PurchaseDiscount < 0 || sale.Offer < 0 || sale.Markup < 0 || sale.CashShare + sale.CreditShare != 1 || sale.CashDiscount > 1 || sale.PurchaseDiscount + sale.Offer > 1 || sale.Markup > 1 || sale.EstimatedCostOverride < 0) throw new InvalidDataException("اطلاعات فروش نامعتبر است.");
        if (sale.RateMonthKey != MonthOf(sale.Date)) throw new InvalidDataException("ماه Snapshot فروش با تاریخ آن سازگار نیست.");
        if (sale.EstimatedCostOverride.HasValue && string.IsNullOrWhiteSpace(sale.EstimatedCostOverrideNote)) throw new InvalidDataException("برای هزینهٔ دستی فروش، دلیل را وارد کنید.");
    }
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
}
