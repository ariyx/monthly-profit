namespace Profit.Core;

public sealed record BrandChangeEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime AtUtc { get; init; } = DateTime.UtcNow;
    public string MonthKey { get; init; } = "";
    public string BrandName { get; init; } = "";
    public string Action { get; init; } = "";
    public string Details { get; init; } = "";
}

// Called at the final commit, not by staging, viewing or migration.
public static class BrandChangeHistory
{
    public static Ledger Append(Ledger before, Ledger after, DateTime? atUtc = null)
    {
        before = MarkupRules.UpgradeLegacy(before);
        after = MarkupRules.UpgradeLegacy(after);
        var changes = new List<BrandChangeEntry>();
        var timestamp = atUtc ?? DateTime.UtcNow;
        void Add(string month, string brand, string action, string details) =>
            changes.Add(new BrandChangeEntry { AtUtc = timestamp, MonthKey = month, BrandName = brand, Action = action, Details = details });
        foreach (var month in after.BrandMonths)
        foreach (var rate in month.Rates)
        {
            var previous = BrandMonthRules.TryConfirmed(before, rate.BrandName, month.MonthKey);
            var next = BrandMonthRules.TryConfirmed(after, rate.BrandName, month.MonthKey);
            if (next is null || previous is not null && BrandMonthRules.SameValues(previous, next)) continue;
            var details = new List<string>();
            void Field(string label, decimal? oldValue, decimal newValue)
            {
                if (previous is null || oldValue != newValue)
                    details.Add(label + ": " + (oldValue.HasValue ? Rules.Percent(oldValue) + " ← " : "") + Rules.Percent(newValue));
            }
            Field("مارک‌آپ پایه", previous?.Markup, next.Markup);
            Field("تخفیف خرید", previous?.PurchaseDiscount, next.PurchaseDiscount);
            Field("آفر", previous?.Offer, next.Offer);
            Field("سهم نقدی", previous?.CashShare, next.CashShare);
            Field("سهم چکی", previous?.CreditShare, next.CreditShare);
            Field("تخفیف نقدی", previous?.CashDiscount, next.CashDiscount);
            details.Add("ماه اثر: " + month.MonthKey + "؛ بازه‌های اختصاصی و استثناهای فروش حفظ می‌شوند.");
            if (previous is null) details.Add("منبع مقدار اولیه: " + next.SourceMonthKey);
            Add(month.MonthKey, rate.BrandName, previous is null ? "تأیید درصدهای ماه" : "ویرایش درصدهای ماه", string.Join("\n", details));
        }
        foreach (var period in after.MarkupPeriods)
        {
            var previous = before.MarkupPeriods.FirstOrDefault(p => p.Id == period.Id);
            if (previous == period) continue;
            var month = Rules.MonthOf(period.StartDate);
            var baseline = BrandMonthRules.TryConfirmed(before, period.BrandName, month)?.Markup ?? 0;
            var oldText = previous is null
                ? $"مارک‌آپ قبلی در تاریخ شروع: {Rules.Percent(MarkupRules.Resolve(before, period.BrandName, period.StartDate, baseline))}"
                : $"قبل: {previous.StartDate} تا {previous.EndDate}؛ {Rules.Percent(previous.Markup)}";
            Add(month, period.BrandName, previous is null ? "افزودن بازه مارک‌آپ" : "ویرایش بازه مارک‌آپ",
                oldText + $"\nبعد: {period.StartDate} تا {period.EndDate}؛ {Rules.Percent(period.Markup)}");
        }
        foreach (var period in before.MarkupPeriods.Where(p => after.MarkupPeriods.All(next => next.Id != p.Id)))
            Add(Rules.MonthOf(period.StartDate), period.BrandName, "حذف بازه مارک‌آپ",
                $"بازه حذف‌شده: {period.StartDate} تا {period.EndDate}؛ {Rules.Percent(period.Markup)}\nروزهای آزادشده با پایه ماه محاسبه می‌شوند.");
        return changes.Count == 0 ? after : after with { BrandChanges = [.. before.BrandChanges, .. changes] };
    }

    public static IEnumerable<BrandChangeEntry> ForMonth(Ledger ledger, string month, string? brand = null) =>
        ledger.BrandChanges.Where(e => e.MonthKey == month && (brand is null || MarkupRules.SameBrand(e.BrandName, brand))).OrderByDescending(e => e.AtUtc);
}
