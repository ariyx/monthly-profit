using System.Text;

namespace Profit.Core;

public static class BrandChangePreview
{
    public static string Describe(Ledger before, Ledger after, string scope)
    {
        var text = new StringBuilder(scope + "\n\n");
        foreach (var brand in after.Brands)
        {
            var previous = before.Brands.FirstOrDefault(b => MarkupRules.SameBrand(b.Name, brand.Name));
            if (previous is not null && previous.IsActive == brand.IsActive && previous.CodePrefixes.SequenceEqual(brand.CodePrefixes) && previous.ExactProductCodes.SequenceEqual(brand.ExactProductCodes)) continue;
            text.AppendLine($"برند {(previous is null ? "جدید" : "ویرایش‌شده")}: {brand.Name} · {(brand.IsActive ? "فعال" : "غیرفعال")}");
            text.AppendLine($"پیشوند: {(previous is null ? "ثبت نشده" : BrandPrefixRules.Display(previous.CodePrefixes))} ← {BrandPrefixRules.Display(brand.CodePrefixes)}");
            text.AppendLine($"کد دقیق: {(previous is null ? "ثبت نشده" : BrandPrefixRules.Display(previous.ExactProductCodes))} ← {BrandPrefixRules.Display(brand.ExactProductCodes)}");
        }
        foreach (var removed in before.Brands.Where(b => after.Brands.All(a => !MarkupRules.SameBrand(a.Name, b.Name))))
            text.AppendLine($"نام قبلی حذف یا جایگزین می‌شود: {removed.Name}");
        var assigned = after.Items.Count(i => before.Items.FirstOrDefault(old => old.Code.Equals(i.Code, StringComparison.OrdinalIgnoreCase)) is { } previous && !MarkupRules.SameBrand(previous.Brand, i.Brand));
        if (assigned > 0) text.AppendLine($"{assigned} کالا به برند مربوط تخصیص می‌گیرد.");
        foreach (var month in after.BrandMonths)
        foreach (var rate in month.Rates)
        {
            var old = before.BrandMonths.FirstOrDefault(m => m.MonthKey == month.MonthKey)?.Rates.FirstOrDefault(r => MarkupRules.SameBrand(r.BrandName, rate.BrandName));
            // Legacy rows inherit confirmation from the month. Compare effective states on both sides.
            var wasConfirmed = BrandMonthRules.TryConfirmed(before, rate.BrandName, month.MonthKey) is not null;
            var isConfirmed = BrandMonthRules.TryConfirmed(after, rate.BrandName, month.MonthKey) is not null;
            if (!wasConfirmed && !isConfirmed) continue;
            if (old is null && !isConfirmed) continue;
            if (old is not null && BrandMonthRules.SameValues(old, rate) && wasConfirmed == isConfirmed) continue;
            text.AppendLine($"{rate.BrandName} · ماه {month.MonthKey}");
            text.AppendLine($"مارک‌آپ پایه: {(old is null ? "ثبت نشده" : Rules.Percent(old.Markup))} ← {Rules.Percent(rate.Markup)}");
            text.AppendLine($"تخفیف خرید {Rules.Percent(rate.PurchaseDiscount)} · آفر {Rules.Percent(rate.Offer)} · نقدی {Rules.Percent(rate.CashShare)} · چکی {Rules.Percent(rate.CreditShare)} · تخفیف نقدی {Rules.Percent(rate.CashDiscount)}");
        }
        foreach (var period in after.MarkupPeriods)
        {
            var old = before.MarkupPeriods.FirstOrDefault(p => p.Id == period.Id);
            if (old == period) continue;
            if (old is not null) text.AppendLine($"بازه قبلی: {old.StartDate} تا {MarkupRules.EndOf(before, old)} · {Rules.Percent(old.Markup)}");
            text.AppendLine($"بازه جدید {period.BrandName}: {period.StartDate} تا {period.EndDate} · {Rules.Percent(period.Markup)}");
        }
        foreach (var removed in before.MarkupPeriods.Where(p => after.MarkupPeriods.All(q => q.Id != p.Id)))
            text.AppendLine($"حذف بازه {removed.StartDate} تا {MarkupRules.EndOf(before, removed)}؛ روزهای خارج از بازه‌های دیگر با پایه ماه محاسبه می‌شوند.");
        var oldCalculation = LedgerCalculator.Calculate(before); var newCalculation = LedgerCalculator.Calculate(after);
        var changed = after.Sales.Where(s => oldCalculation.Sales.GetValueOrDefault(s.Id) != newCalculation.Sales.GetValueOrDefault(s.Id)).ToList();
        text.AppendLine($"\n{changed.Count} فروش دارای تغییر در نتیجه محاسبه است.");
        foreach (var group in changed.GroupBy(s => (Month: Rules.MonthOf(s.Date), Brand: after.Items.First(i => i.Code.Equals(s.Code, StringComparison.OrdinalIgnoreCase)).Brand)))
            text.AppendLine($"{group.Key.Month} · {group.Key.Brand}: {group.Count()} فروش · {group.Min(s => s.Date)} تا {group.Max(s => s.Date)}");
        text.AppendLine($"تغییر بهای تمام‌شده: {Rules.ReportMoney(newCalculation.Sales.Values.Sum(s => s.Cost) - oldCalculation.Sales.Values.Sum(s => s.Cost))} ریال");
        text.AppendLine($"تغییر سود فروش‌ها: {Rules.ReportMoney(newCalculation.Sales.Values.Sum(s => s.Profit) - oldCalculation.Sales.Values.Sum(s => s.Profit))} ریال");
        var pending = after.Sales.Where(s => !newCalculation.Sales.ContainsKey(s.Id)).ToList();
        text.AppendLine($"فروش‌های همچنان معلق: {pending.Count}؛ درصدهای ماه هر برند باید جداگانه تأیید شوند.");
        text.AppendLine("مارک‌آپ اختصاصی فروش‌ها حفظ می‌شود. تغییرات تا تأیید نهایی ذخیره نشده‌اند.");
        return text.ToString();
    }
}
