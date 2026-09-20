namespace Profit.Core;

// هر مغایرت به یک فروش مشخص متصل است. تجمیع بر اساس کد کالا باعث می‌شد
// فروش‌هایی با قیمت‌های متفاوت یک بهای تعدیل مشترک بگیرند.
public sealed record ReconciliationCandidate(
    string SaleId,
    string Code,
    string Name,
    string Brand,
    string FirstDate,
    decimal Quantity,
    decimal SaleUnitPrice,
    decimal SuggestedGrossPurchaseUnitPrice,
    decimal SuggestedNetUnitCost,
    decimal PurchaseDiscount,
    decimal Offer,
    decimal Markup,
    string RateMonthKey);

public static class ReconciliationPlanner
{
    public static List<ReconciliationCandidate> Find(Ledger baseline, IEnumerable<Sale> sales)
    {
        var ordered = sales.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0) return [];
        var calculation = LedgerCalculator.Calculate(baseline with { Sales = [.. baseline.Sales, .. ordered] });
        return FromCalculation(baseline, ordered, calculation);
    }

    static List<ReconciliationCandidate> FromCalculation(Ledger ledger, IEnumerable<Sale> sales, LedgerCalculation calculation)
    {
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var result = new List<ReconciliationCandidate>();
        foreach (var sale in sales.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var settlement = calculation.Sales[sale.Id];
            if (settlement.Shortage <= 0 || !items.TryGetValue(sale.Code, out var item)) continue;
            var monthKey = Rules.MonthOf(sale.Date);
            var rate = BrandMonthRules.TryConfirmed(ledger, item.Brand, monthKey);
            var purchaseDiscount = string.IsNullOrWhiteSpace(sale.RateMonthKey) ? rate?.PurchaseDiscount ?? 0 : sale.PurchaseDiscount;
            var offer = string.IsNullOrWhiteSpace(sale.RateMonthKey) ? rate?.Offer ?? 0 : sale.Offer;
            var markup = string.IsNullOrWhiteSpace(sale.RateMonthKey) ? rate?.Markup ?? 0 : sale.Markup;
            var hasRates = !string.IsNullOrWhiteSpace(sale.RateMonthKey) || rate != null;
            var gross = !hasRates ? 0 : sale.UnitPrice / (1 + markup);
            var net = !hasRates ? 0 : gross * (1 - purchaseDiscount - offer);
            result.Add(new ReconciliationCandidate(sale.Id, item.Code, item.Name, item.Brand, sale.Date, settlement.Shortage,
                sale.UnitPrice, gross, net, purchaseDiscount, offer, markup, sale.RateMonthKey.Length > 0 ? sale.RateMonthKey : monthKey));
        }
        return result.OrderBy(x => x.FirstDate, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.SaleId, StringComparer.Ordinal).ToList();
    }

    public static List<ReconciliationCandidate> Existing(Ledger ledger)
        => Existing(ledger, LedgerCalculator.Calculate(ledger));

    public static List<ReconciliationCandidate> Existing(Ledger ledger, LedgerCalculation calculation)
        => FromCalculation(ledger, ledger.Sales, calculation);

    public static Purchase CreateAdjustment(ReconciliationCandidate candidate, decimal quantity, decimal grossPurchaseUnitPrice, string note = "")
    {
        if (quantity <= 0 || quantity > candidate.Quantity) throw new InvalidDataException("تعداد تعدیل باید بیشتر از صفر و حداکثر برابر کسری همین فروش باشد.");
        if (grossPurchaseUnitPrice <= 0) throw new InvalidDataException("قیمت خرید اولیهٔ تخمینی باید بیشتر از صفر باشد.");
        var purchase = new Purchase
        {
            Date = candidate.FirstDate,
            Code = candidate.Code,
            Supplier = "تعدیل مغایرت فروش",
            Quantity = quantity,
            UnitPrice = grossPurchaseUnitPrice,
            Total = quantity * grossPurchaseUnitPrice,
            BrandDiscount = candidate.PurchaseDiscount,
            Offer = candidate.Offer,
            IsAdjustment = true,
            AdjustmentSaleId = candidate.SaleId,
            RateMonthKey = candidate.RateMonthKey,
            Note = string.IsNullOrWhiteSpace(note) ? "تعدیل خودکار بر اساس قیمت همان فروش و درصدهای ماه فروش" : Rules.Normalize(note)
        };
        Rules.Validate(purchase);
        return purchase;
    }
}
