namespace Profit.Core;

public static class BrandCatalogRules
{
    public static Ledger Upsert(Ledger ledger, Brand value, string? originalName = null)
    {
        Rules.Validate(value);
        var old = originalName is null ? null : ledger.Brands.FirstOrDefault(b => MarkupRules.SameBrand(b.Name, originalName)) ?? throw new InvalidDataException("برند یافت نشد.");
        if (ledger.Brands.Any(b => MarkupRules.SameBrand(b.Name, value.Name) && b != old)) throw new InvalidDataException("این نام برند قبلاً ثبت شده است؛ برند غیرفعال را از فهرست فعال کنید.");
        if (old is not null && !MarkupRules.SameBrand(old.Name, value.Name) && ledger.Items.Any(i => MarkupRules.SameBrand(i.Brand, old.Name))) throw new InvalidDataException("نام برند دارای کالا ثابت است؛ پیشوند و کدهای دقیق قابل ویرایش‌اند.");
        var brands = old is null ? ledger.Brands.Append(value).ToList() : ledger.Brands.Select(b => b == old ? value : b).ToList();
        BrandPrefixRules.ValidateUnique(brands);
        var next = ledger with
        {
            Brands = brands,
            BrandMonths = ledger.BrandMonths.Select(m => m with { Rates = m.Rates.Select(r => old is not null && MarkupRules.SameBrand(r.BrandName, old.Name) ? r with { BrandName = value.Name } : r).ToList() }).ToList(),
            MarkupPeriods = ledger.MarkupPeriods.Select(p => old is not null && MarkupRules.SameBrand(p.BrandName, old.Name) ? p with { BrandName = value.Name } : p).ToList()
        };
        var affected = next.Items.Where(i => string.IsNullOrWhiteSpace(i.Brand) && MarkupRules.SameBrand(BrandPrefixRules.DetectActive(brands, i.Code)?.Name ?? "", value.Name)).Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        next = next with { Items = next.Items.Select(i => affected.Contains(i.Code) ? i with { Brand = value.Name } : i).ToList() };
        next = BrandMonthRules.ApplyExactProductCodeAssignments(next);
        foreach (var code in affected) next = BrandMonthRules.ApplyPendingSalesForItem(next, code);
        return next;
    }
}
