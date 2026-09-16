namespace Profit.Core;

// مغایرت‌ها در سطح کد کالا جمع می‌شوند تا ورود یک فایل بزرگ به پنجره‌های متوالی تبدیل نشود.
public sealed record ReconciliationCandidate(string Code, string Name, string Brand, string FirstDate, decimal Quantity, decimal SuggestedUnitCost, int SaleRows);

public static class ReconciliationPlanner
{
    public static List<ReconciliationCandidate> Find(Ledger baseline, IEnumerable<Sale> sales)
    {
        var staged = baseline;
        var groups = new Dictionary<string, (CatalogItem Item, string FirstDate, decimal Quantity, int Rows)> (StringComparer.OrdinalIgnoreCase);
        foreach (var sale in sales.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var preview = LedgerCalculator.PreviewSale(staged, sale);
            if (preview.Shortage > 0)
            {
                var item = staged.Items.Single(x => x.Code.Equals(sale.Code, StringComparison.OrdinalIgnoreCase));
                if (groups.TryGetValue(sale.Code, out var group)) groups[sale.Code] = (group.Item, group.FirstDate, group.Quantity + preview.Shortage, group.Rows + 1);
                else groups[sale.Code] = (item, sale.Date, preview.Shortage, 1);
            }
            staged = staged with { Sales = [.. staged.Sales, sale] };
        }
        return groups.Values.Select(x => new ReconciliationCandidate(x.Item.Code, x.Item.Name, x.Item.Brand, x.FirstDate, x.Quantity, SuggestedUnitCost(baseline, x.Item.Code, x.FirstDate), x.Rows))
            .OrderBy(x => x.FirstDate, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.Ordinal).ToList();
    }

    public static List<ReconciliationCandidate> Existing(Ledger ledger)
        => Find(ledger with { Sales = [] }, ledger.Sales);

    public static decimal SuggestedUnitCost(Ledger ledger, string code, string date)
    {
        var purchase = ledger.Purchases.Where(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && string.CompareOrdinal(x.Date, date) <= 0)
            .OrderByDescending(x => x.Date, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
        if (purchase != null) return Rules.NetPurchase(purchase) / purchase.Quantity;
        return ledger.OpeningLots.Where(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.SourceDate, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).Select(x => x.UnitCost).FirstOrDefault();
    }

    public static List<Purchase> CreateAdjustments(IEnumerable<ReconciliationCandidate> candidates, IReadOnlyDictionary<string, decimal> unitCosts)
        => candidates.Select(x =>
        {
            if (!unitCosts.TryGetValue(x.Code, out var price) || price <= 0) throw new InvalidDataException($"برای مغایرت کد {x.Code} قیمت تعدیل تعیین نشده است.");
            return new Purchase { Date = x.FirstDate, Code = x.Code, Supplier = "تعدیل دستی", Quantity = x.Quantity, UnitPrice = price, Total = x.Quantity * price, IsAdjustment = true, Note = "تعدیل موجودی ناشی از مغایرت" };
        }).ToList();
}
