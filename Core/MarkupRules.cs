namespace Profit.Core;

// Only markup varies by date. Empty EndDate denotes an older, implicit interval.
public sealed record MarkupPeriod
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string BrandName { get; init; } = "";
    public string StartDate { get; init; } = "";
    public string EndDate { get; init; } = "";
    public decimal Markup { get; init; }
}

public static class MarkupRules
{
    public static bool SameBrand(string a, string b) => Rules.Normalize(a).Equals(Rules.Normalize(b), StringComparison.OrdinalIgnoreCase);

    public static string EndOf(Ledger ledger, MarkupPeriod period)
    {
        if (period.EndDate.Length > 0) return period.EndDate;
        var next = ledger.MarkupPeriods.Where(p => SameBrand(p.BrandName, period.BrandName) && Rules.MonthOf(p.StartDate) == Rules.MonthOf(period.StartDate) && string.CompareOrdinal(p.StartDate, period.StartDate) > 0).OrderBy(p => p.StartDate).FirstOrDefault();
        return next is null ? Rules.LastDate(Rules.MonthOf(period.StartDate)) : Rules.PreviousDate(next.StartDate);
    }
    public static Ledger UpgradeLegacy(Ledger ledger) => ledger.MarkupPeriods.Any(p => p.EndDate.Length == 0)
        ? ledger with { MarkupPeriods = ledger.MarkupPeriods.Select(p => p.EndDate.Length == 0 ? p with { EndDate = EndOf(ledger, p) } : p).ToList() } : ledger;

    public static void Validate(Ledger ledger)
    {
        var upgraded = UpgradeLegacy(ledger);
        foreach (var period in upgraded.MarkupPeriods)
        {
            if (!Rules.ValidDate(period.StartDate) || !Rules.ValidDate(period.EndDate) || string.CompareOrdinal(period.StartDate, period.EndDate) > 0 || Rules.MonthOf(period.StartDate) != Rules.MonthOf(period.EndDate) || period.Markup < 0 || string.IsNullOrWhiteSpace(period.Id)
                || !ledger.Brands.Any(b => SameBrand(b.Name, period.BrandName)))
                throw new InvalidDataException("بازه باید تاریخ شروع و پایان معتبر در یک ماه و مارک‌آپ نامنفی داشته باشد.");
        }
        if (ledger.MarkupPeriods.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != ledger.MarkupPeriods.Count
            || ledger.MarkupPeriods.GroupBy(x => (Rules.Normalize(x.BrandName).ToUpperInvariant(), x.StartDate)).Any(g => g.Count() > 1))
            throw new InvalidDataException("برای یک برند و تاریخ، تنها یک مارک‌آپ قابل ثبت است.");
        foreach (var group in upgraded.MarkupPeriods.GroupBy(p => Rules.Normalize(p.BrandName).ToUpperInvariant()))
        {
            var ordered = group.OrderBy(p => p.StartDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if (string.CompareOrdinal(ordered[i].StartDate, ordered[i - 1].EndDate) <= 0) throw new InvalidDataException($"بازه با {ordered[i - 1].StartDate} تا {ordered[i - 1].EndDate} هم‌پوشانی دارد.");
        }
    }

    public static decimal Resolve(Ledger ledger, string brand, string date, decimal monthlyMarkup)
    {
        var month = Rules.MonthOf(date);
        return ledger.MarkupPeriods.Where(x => SameBrand(x.BrandName, brand) && Rules.MonthOf(x.StartDate) == month && string.CompareOrdinal(x.StartDate, date) <= 0 && string.CompareOrdinal(date, EndOf(ledger, x)) <= 0)
            .OrderByDescending(x => x.StartDate, StringComparer.Ordinal).Select(x => (decimal?)x.Markup).FirstOrDefault() ?? monthlyMarkup;
    }

    public static Ledger SetPeriod(Ledger ledger, MarkupPeriod period, bool replace)
    {
        ledger = UpgradeLegacy(ledger);
        var brand = ledger.Brands.FirstOrDefault(b => SameBrand(b.Name, period.BrandName)) ?? throw new InvalidDataException("برند یافت نشد.");
        if (!brand.IsActive) throw new InvalidOperationException("ابتدا برند را فعال کنید.");
        var month = Rules.MonthOf(period.StartDate);
        BrandMonthRules.RequireConfirmed(ledger, brand.Name, month);
        var old = ledger.MarkupPeriods.FirstOrDefault(x => x.Id == period.Id);
        if (replace && (old is null || Rules.MonthOf(old.StartDate) != month || !SameBrand(old.BrandName, period.BrandName))) throw new InvalidDataException("بازه فقط داخل همان ماه و برای همان برند قابل ویرایش است.");
        if (!replace && old is not null) throw new InvalidDataException("بازه قبلاً ثبت شده است.");
        if (period.EndDate.Length == 0) throw new InvalidDataException("تاریخ پایان را وارد کنید.");
        if (old == period) return ledger;
        var next = ledger with { MarkupPeriods = [.. ledger.MarkupPeriods.Where(x => x.Id != period.Id), period with { BrandName = brand.Name }] };
        Validate(next);
        return Refresh(next, brand.Name, month);
    }
    public static Ledger RemovePeriod(Ledger ledger, string id)
    {
        ledger = UpgradeLegacy(ledger);
        var period = ledger.MarkupPeriods.FirstOrDefault(p => p.Id == id) ?? throw new InvalidDataException("بازه یافت نشد.");
        BrandLifecycle.RequireActive(ledger, period.BrandName);
        BrandMonthRules.RequireConfirmed(ledger, period.BrandName, Rules.MonthOf(period.StartDate));
        return Refresh(ledger with { MarkupPeriods = ledger.MarkupPeriods.Where(p => p.Id != id).ToList() }, period.BrandName, Rules.MonthOf(period.StartDate));
    }
    static Ledger Refresh(Ledger next, string brand, string month)
    {
        var codes = next.Items.Where(x => SameBrand(x.Brand, brand)).Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return next with { Sales = next.Sales.Select(s =>
        {
            if (!codes.Contains(s.Code) || Rules.MonthOf(s.Date) != month || !Rules.HasRateSnapshot(s) || s.MarkupOverride.HasValue) return s;
            var rate = BrandMonthRules.RequireConfirmed(next, brand, month);
            return s with { Markup = Resolve(next, brand, s.Date, rate.Markup) };
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
