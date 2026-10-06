namespace Profit.Core;

// Percentages other than markup remain monthly. A period ends at the next start
// in the same month; the final markup becomes the next month's confirmation draft.
public sealed record MarkupPeriod
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string BrandName { get; init; } = "";
    public string StartDate { get; init; } = "";
    public decimal Markup { get; init; }
}

public static class MarkupRules
{
    public static bool SameBrand(string a, string b) => Rules.Normalize(a).Equals(Rules.Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static void Validate(Ledger ledger)
    {
        foreach (var period in ledger.MarkupPeriods)
        {
            if (!Rules.ValidDate(period.StartDate) || period.StartDate.EndsWith("01", StringComparison.Ordinal) || period.Markup < 0 || string.IsNullOrWhiteSpace(period.Id)
                || !ledger.Brands.Any(b => SameBrand(b.Name, period.BrandName)))
                throw new InvalidDataException("بازه مارک‌آپ نامعتبر است؛ روز اول ماه از تنظیمات ماهانه ویرایش می‌شود.");
        }
        if (ledger.MarkupPeriods.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != ledger.MarkupPeriods.Count
            || ledger.MarkupPeriods.GroupBy(x => (Rules.Normalize(x.BrandName).ToUpperInvariant(), x.StartDate)).Any(g => g.Count() > 1))
            throw new InvalidDataException("برای یک برند و تاریخ، تنها یک مارک‌آپ قابل ثبت است.");
    }

    public static decimal Resolve(Ledger ledger, string brand, string date, decimal monthlyMarkup)
    {
        var month = Rules.MonthOf(date);
        return ledger.MarkupPeriods.Where(x => SameBrand(x.BrandName, brand) && Rules.MonthOf(x.StartDate) == month && string.CompareOrdinal(x.StartDate, date) <= 0)
            .OrderByDescending(x => x.StartDate, StringComparer.Ordinal).Select(x => (decimal?)x.Markup).FirstOrDefault() ?? monthlyMarkup;
    }

    public static Ledger SetPeriod(Ledger ledger, MarkupPeriod period, bool replace)
    {
        var brand = ledger.Brands.FirstOrDefault(b => SameBrand(b.Name, period.BrandName)) ?? throw new InvalidDataException("برند یافت نشد.");
        if (!brand.IsActive) throw new InvalidOperationException("ابتدا برند را فعال کنید.");
        var month = Rules.MonthOf(period.StartDate);
        BrandMonthRules.RequireConfirmed(ledger, brand.Name, month);
        var old = ledger.MarkupPeriods.FirstOrDefault(x => x.Id == period.Id);
        if (replace && (old is null || old.StartDate != period.StartDate || !SameBrand(old.BrandName, period.BrandName))) throw new InvalidDataException("در اصلاح بازه، تاریخ شروع و برند قابل تغییر نیست.");
        if (!replace && old is not null) throw new InvalidDataException("بازه قبلاً ثبت شده است.");
        var next = ledger with { MarkupPeriods = [.. ledger.MarkupPeriods.Where(x => x.Id != period.Id), period with { BrandName = brand.Name }] };
        Validate(next);
        var codes = ledger.Items.Where(x => SameBrand(x.Brand, brand.Name)).Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return next with { Sales = next.Sales.Select(s =>
        {
            if (!codes.Contains(s.Code) || Rules.MonthOf(s.Date) != month || !Rules.HasRateSnapshot(s) || s.MarkupOverride.HasValue) return s;
            var rate = BrandMonthRules.RequireConfirmed(next, brand.Name, month);
            return s with { Markup = Resolve(next, brand.Name, s.Date, rate.Markup) };
        }).ToList() };
    }
}

public static class BrandLifecycle
{
    public static Brand RequireActive(Ledger ledger, string name) => ledger.Brands.FirstOrDefault(b => b.IsActive && MarkupRules.SameBrand(b.Name, name))
        ?? throw new InvalidOperationException($"برند «{name}» غیرفعال است؛ ابتدا آن را فعال کنید.");

    public static HashSet<string> Codes(Ledger ledger, string name) => ledger.Items.Where(i => MarkupRules.SameBrand(i.Brand, name)).Select(i => i.Code)
        .Concat(ledger.Sales.Select(s => s.Code).Concat(ledger.Returns.Select(r => r.Code)).Concat(ledger.Offers.Select(o => o.Code))
            .Where(code => MarkupRules.SameBrand(BrandPrefixRules.Detect(ledger.Brands, code)?.Name ?? "", name)))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static int RelatedTransactions(Ledger ledger, string name)
    {
        var codes = Codes(ledger, name);
        return ledger.Sales.Count(s => codes.Contains(s.Code)) + ledger.Returns.Count(r => codes.Contains(r.Code)) + ledger.Offers.Count(o => codes.Contains(o.Code));
    }

    public static Ledger RemoveOrDeactivate(Ledger ledger, string name)
    {
        var brand = ledger.Brands.FirstOrDefault(b => MarkupRules.SameBrand(b.Name, name)) ?? throw new InvalidDataException("برند یافت نشد.");
        if (RelatedTransactions(ledger, name) > 0) return ledger with { Brands = ledger.Brands.Select(b => b == brand ? b with { IsActive = false } : b).ToList() };
        return ledger with
        {
            BrandRulesInitialized = true,
            Brands = ledger.Brands.Where(b => b != brand).ToList(),
            Items = ledger.Items.Select(i => MarkupRules.SameBrand(i.Brand, name) ? i with { Brand = "" } : i).ToList(),
            BrandMonths = ledger.BrandMonths.Select(m => m with { Rates = m.Rates.Where(r => !MarkupRules.SameBrand(r.BrandName, name)).ToList() }).ToList(),
            MarkupPeriods = ledger.MarkupPeriods.Where(p => !MarkupRules.SameBrand(p.BrandName, name)).ToList()
        };
    }
}
