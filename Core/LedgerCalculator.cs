namespace Profit.Core;

public sealed class LedgerCalculation
{
    public Dictionary<string, SaleSettlement> Sales { get; } = [];
}

// محاسبه کاملاً فروش‌محور است: خرید، موجودی و FIFO در این مدل وجود ندارند.
public static class LedgerCalculator
{
    public static LedgerCalculation Calculate(Ledger ledger)
    {
        foreach (var brand in ledger.Brands) Rules.Validate(brand);
        BrandPrefixRules.ValidateUnique(ledger.Brands);
        if (ledger.Brands.Select(x => Rules.Normalize(x.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ledger.Brands.Count) throw new InvalidDataException("نام برند تکراری است.");
        foreach (var setting in ledger.BrandMonths)
        {
            if (!Rules.ValidMonth(setting.MonthKey)) throw new InvalidDataException("ماه تنظیمات برند نامعتبر است.");
            foreach (var rate in setting.Rates) BrandMonthRules.Validate(rate);
            if (setting.Rates.Select(x => Rules.Normalize(x.BrandName)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != setting.Rates.Count) throw new InvalidDataException("برند تکراری در تنظیمات ماهانه وجود دارد.");
        }
        if (ledger.BrandMonths.Select(x => x.MonthKey).Distinct(StringComparer.Ordinal).Count() != ledger.BrandMonths.Count) throw new InvalidDataException("تنظیمات یک ماه بیش از یک بار ثبت شده است.");
        foreach (var item in ledger.Items) Rules.Validate(item);
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        if (items.Count != ledger.Items.Count) throw new InvalidDataException("کد کالا تکراری است.");
        foreach (var sale in ledger.Sales) Rules.Validate(sale);
        if (ledger.Sales.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != ledger.Sales.Count) throw new InvalidDataException("شناسه فروش تکراری است.");
        if (ledger.Sales.Any(x => !items.ContainsKey(x.Code))) throw new InvalidDataException("برای یکی از فروش‌ها کالا تعریف نشده است.");

        var result = new LedgerCalculation();
        foreach (var sale in ledger.Sales)
        {
            // تا وقتی برند و Snapshot ماهانه مشخص نشده‌اند، فروش ذخیره می‌شود اما در سود وارد نمی‌شود.
            if (!Rules.HasRateSnapshot(sale)) continue;
            var grossPurchaseUnit = Rules.GrossPurchaseUnitFromSale(sale);
            var netCostUnit = Rules.EstimatedNetCostUnit(sale);
            var split = Rules.SplitSale(sale);
            result.Sales[sale.Id] = new SaleSettlement(
                Rules.EstimatedCost(sale), split.Cash, split.Credit, grossPurchaseUnit, netCostUnit, sale.EstimatedCostOverride.HasValue);
        }
        return result;
    }

    public static SaleSettlement PreviewSale(Sale sale)
    {
        Rules.Validate(sale);
        var split = Rules.SplitSale(sale);
        return new SaleSettlement(Rules.EstimatedCost(sale), split.Cash, split.Credit, Rules.GrossPurchaseUnitFromSale(sale), Rules.EstimatedNetCostUnit(sale), sale.EstimatedCostOverride.HasValue);
    }

    public static LedgerTotal SummarizeMonth(Ledger ledger, Month month, LedgerCalculation? calculation = null)
    {
        Rules.Validate(month);
        calculation ??= Calculate(ledger);
        var sales = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == month.Key).ToList();
        var calculatedSales = sales.Where(x => calculation.Sales.ContainsKey(x.Id)).ToList();
        var settled = calculatedSales.Select(x => calculation.Sales[x.Id]).ToList();
        var codes = calculatedSales.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var items = ledger.Items.Where(x => codes.Contains(x.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        return new LedgerTotal(settled.Sum(x => x.Cost), settled.Sum(x => x.Cash), settled.Sum(x => x.Credit), settled.Sum(x => x.Profit), month.FixedCost, calculatedSales.Sum(x => x.Quantity), codes.Count, items.Select(x => x.Brand).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            InvoiceSales = calculatedSales.Sum(Rules.NetSaleBase),
            CashDiscountAmount = calculatedSales.Sum(Rules.CashDiscountAmount),
            PendingSalesCount = sales.Count - calculatedSales.Count,
            PendingInvoiceSales = sales.Where(x => !calculation.Sales.ContainsKey(x.Id)).Sum(Rules.NetSaleBase)
        };
    }
}
