namespace Profit.Core;

public static class BrandMonthRules
{
    public static BrandRate FromBrand(Brand b) => new() { BrandName = b.Name, PurchaseDiscount = b.PurchaseDiscount, Offer = b.Offer, Markup = b.Markup, CashShare = b.CashShare, CreditShare = b.CreditShare, CashDiscount = b.CashDiscount };
    public static bool SameValues(BrandRate a, BrandRate b) => MarkupRules.SameBrand(a.BrandName, b.BrandName) && a.PurchaseDiscount == b.PurchaseDiscount && a.Offer == b.Offer && a.Markup == b.Markup && a.CashShare == b.CashShare && a.CreditShare == b.CreditShare && a.CashDiscount == b.CashDiscount;
    static bool Confirmed(BrandRate rate, BrandMonthSettings setting) => rate.IsConfirmed ?? setting.IsConfirmed;

    public static void Validate(BrandRate rate)
    {
        if (string.IsNullOrWhiteSpace(rate.BrandName) || rate.BrandName.Trim().Length > 120) throw new InvalidDataException("نام برند نامعتبر است.");
        if (rate.Markup < 0) throw new InvalidDataException("مارک‌آپ نمی‌تواند منفی باشد.");
        if (new[] { rate.PurchaseDiscount, rate.Offer, rate.CashShare, rate.CreditShare, rate.CashDiscount }.Any(x => x < 0 || x > 1)) throw new InvalidDataException("درصدهای غیر از مارک‌آپ باید بین صفر و ۱۰۰ باشند.");
        if (rate.PurchaseDiscount + rate.Offer > 1) throw new InvalidDataException("جمع تخفیف خرید و آفر نباید بیشتر از ۱۰۰٪ باشد.");
        if (rate.CashShare + rate.CreditShare != 1) throw new InvalidDataException("جمع سهم نقدی و چکی باید دقیقاً ۱۰۰٪ باشد.");
    }

    static BrandRate Suggest(Ledger ledger, Brand brand, string month)
    {
        foreach (var setting in ledger.BrandMonths.Where(x => string.CompareOrdinal(x.MonthKey, month) < 0).OrderByDescending(x => x.MonthKey, StringComparer.Ordinal))
        {
            var rate = setting.Rates.FirstOrDefault(r => MarkupRules.SameBrand(r.BrandName, brand.Name));
            if (rate is null || !Confirmed(rate, setting)) continue;
            return rate with { BrandName = brand.Name, Markup = MarkupRules.Resolve(ledger, brand.Name, Rules.LastDate(setting.MonthKey), rate.Markup), IsConfirmed = false, ConfirmedAtUtc = null, SourceMonthKey = setting.MonthKey };
        }
        return FromBrand(brand) with { IsConfirmed = false, SourceMonthKey = "پیش‌فرض برند" };
    }

    public static BrandMonthSettings DraftFor(Ledger ledger, string month)
    {
        if (!Rules.ValidMonth(month)) throw new InvalidDataException("ماه نامعتبر است.");
        var exact = ledger.BrandMonths.FirstOrDefault(m => m.MonthKey == month);
        var rates = ledger.Brands.Select(b =>
        {
            var saved = exact?.Rates.FirstOrDefault(r => MarkupRules.SameBrand(r.BrandName, b.Name));
            // Unconfirmed rows persisted while approving another brand are still defaults.
            // Refresh them from the latest approved source; approved months remain fixed.
            return saved is null || !Confirmed(saved, exact!) ? Suggest(ledger, b, month) : saved with { BrandName = b.Name, IsConfirmed = true, SourceMonthKey = saved.SourceMonthKey.Length > 0 ? saved.SourceMonthKey : exact!.SourceMonthKey };
        }).ToList();
        var sources = rates.Select(r => r.SourceMonthKey).Distinct().ToList();
        return new BrandMonthSettings { MonthKey = month, Rates = rates, IsConfirmed = ledger.Brands.Where(b => b.IsActive).All(b => rates.Any(r => MarkupRules.SameBrand(r.BrandName, b.Name) && r.IsConfirmed == true)), SourceMonthKey = sources.Count == 1 ? sources[0] : "منبع جداگانه برای هر برند", ConfirmedAtUtc = exact?.ConfirmedAtUtc };
    }
    public static bool IsConfirmed(Ledger ledger, string month) => DraftFor(ledger, month).IsConfirmed;
    public static BrandRate? TryConfirmed(Ledger ledger, string brand, string month)
    {
        var setting = ledger.BrandMonths.FirstOrDefault(m => m.MonthKey == month);
        var rate = setting?.Rates.FirstOrDefault(r => MarkupRules.SameBrand(r.BrandName, brand));
        return rate is not null && Confirmed(rate, setting!) ? rate with { IsConfirmed = true } : null;
    }
    public static BrandRate RequireConfirmed(Ledger ledger, string brand, string month) => TryConfirmed(ledger, brand, month) ?? throw new InvalidOperationException($"درصدهای برند «{brand}» در ماه {month} هنوز تأیید نشده‌اند.");

    public static Ledger Confirm(Ledger ledger, string month, IEnumerable<BrandRate> values)
    {
        var requested = values.Select(r => r with { BrandName = Rules.Normalize(r.BrandName) }).ToList();
        foreach (var rate in requested) Validate(rate);
        if (requested.Select(r => r.BrandName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count || requested.Any(r => !ledger.Brands.Any(b => MarkupRules.SameBrand(b.Name, r.BrandName)))) throw new InvalidDataException("برند ناشناخته یا تکراری در جدول درصدها وجود دارد.");
        var draft = DraftFor(ledger, month);
        var changes = requested.Where(r => { var before = draft.Rates.First(x => MarkupRules.SameBrand(x.BrandName, r.BrandName)); return before.IsConfirmed != true || !SameValues(before, r); }).ToList();
        if (changes.Count == 0) return ledger;
        var rates = draft.Rates.Select(before =>
        {
            var edited = changes.FirstOrDefault(r => MarkupRules.SameBrand(r.BrandName, before.BrandName));
            return edited is null ? before : edited with { IsConfirmed = true, ConfirmedAtUtc = DateTime.UtcNow, SourceMonthKey = before.SourceMonthKey };
        }).ToList();
        var confirmed = draft with { Rates = rates, IsConfirmed = ledger.Brands.Where(b => b.IsActive).All(b => rates.Any(r => MarkupRules.SameBrand(r.BrandName, b.Name) && r.IsConfirmed == true)), ConfirmedAtUtc = DateTime.UtcNow };
        var staged = ledger with { BrandMonths = [.. ledger.BrandMonths.Where(m => m.MonthKey != month), confirmed] };
        var items = staged.Items.ToDictionary(i => i.Code, StringComparer.OrdinalIgnoreCase);
        return staged with { Sales = staged.Sales.Select(s =>
        {
            if (Rules.MonthOf(s.Date) != month || !items.TryGetValue(s.Code, out var item) || !changes.Any(r => MarkupRules.SameBrand(r.BrandName, item.Brand))) return s;
            return Apply(s, RequireConfirmed(staged, item.Brand, month), month, staged);
        }).ToList() };
    }

    public static Ledger ApplyPendingSalesForItem(Ledger ledger, string code)
    {
        var item = ledger.Items.FirstOrDefault(i => i.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("کالا یافت نشد.");
        if (string.IsNullOrWhiteSpace(item.Brand)) return ledger;
        return ledger with { Sales = ledger.Sales.Select(s =>
        {
            if (!s.Code.Equals(code, StringComparison.OrdinalIgnoreCase) || Rules.HasRateSnapshot(s)) return s;
            var month = Rules.MonthOf(s.Date); var rate = TryConfirmed(ledger, item.Brand, month);
            return rate is null ? s : Apply(s, rate, month, ledger);
        }).ToList() };
    }
    public static Ledger ApplyExactProductCodeAssignments(Ledger ledger)
    {
        var changes = ledger.Items.Select(i => (Item: i, Brand: BrandPrefixRules.DetectExact(ledger.Brands.Where(b => b.IsActive), i.Code)))
            .Where(x => x.Brand is not null && !MarkupRules.SameBrand(x.Item.Brand, x.Brand!.Name)).ToDictionary(x => x.Item.Code, x => x.Brand!.Name, StringComparer.OrdinalIgnoreCase);
        if (changes.Count == 0) return ledger;
        var staged = ledger with { Items = ledger.Items.Select(i => changes.TryGetValue(i.Code, out var b) ? i with { Brand = b } : i).ToList() };
        return staged with { Sales = staged.Sales.Select(s =>
        {
            if (!changes.TryGetValue(s.Code, out var b)) return s;
            var month = Rules.MonthOf(s.Date); var rate = TryConfirmed(staged, b, month);
            return rate is null ? s with { RateMonthKey = "", CashShare = 0, CreditShare = 0, CashDiscount = 0, PurchaseDiscount = 0, Offer = 0, Markup = 0, EstimatedCostOverride = null, EstimatedCostOverrideNote = "" } : Apply(s, rate, month, staged);
        }).ToList() };
    }
    public static Sale Apply(Sale sale, BrandRate rate, string month, Ledger? ledger = null) => sale with { CashShare = rate.CashShare, CreditShare = rate.CreditShare, CashDiscount = rate.CashDiscount, PurchaseDiscount = rate.PurchaseDiscount, Offer = rate.Offer, Markup = sale.MarkupOverride ?? (ledger is null ? rate.Markup : MarkupRules.Resolve(ledger, rate.BrandName, sale.Date, rate.Markup)), RateMonthKey = month };
}
