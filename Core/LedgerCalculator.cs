namespace Profit.Core;

public sealed class LedgerCalculation
{
    public Dictionary<string, SaleSettlement> Sales { get; } = [];
    public List<StockRow> Stock { get; } = [];
}

public static class LedgerCalculator
{
    sealed class Lot(Purchase purchase)
    {
        public string Code { get; } = purchase.Code;
        public string Date { get; } = purchase.Date;
        public string Id { get; } = purchase.Id;
        public decimal Remaining { get; set; } = purchase.Quantity;
        public decimal UnitCost { get; } = Rules.NetPurchase(purchase) / purchase.Quantity;

        public Lot(OpeningLot opening) : this(new Purchase { Id = opening.Id, Date = opening.SourceDate, Code = opening.Code, Quantity = opening.Quantity, Total = opening.Quantity * opening.UnitCost }) { }
    }

    sealed record Entry(string Date, int Order, string Id, Purchase? Purchase, Sale? Sale);

    public static LedgerCalculation Calculate(Ledger ledger)
        => Calculate(ledger, null);

    // موجودیِ یک ماه باید تا پایان همان ماه محاسبه شود، نه با خرید و فروش ماه‌های بعد.
    public static LedgerCalculation CalculateAtEndOfMonth(Ledger ledger, string monthKey)
    {
        if (!Rules.ValidMonth(monthKey)) throw new InvalidDataException("ماه نامعتبر است.");
        return Calculate(ledger, monthKey.Replace("/", "") + "31");
    }

    static LedgerCalculation Calculate(Ledger ledger, string? throughDate)
    {
        foreach (var brand in ledger.Brands) Rules.Validate(brand);
        foreach (var item in ledger.Items) Rules.Validate(item);
        foreach (var purchase in ledger.Purchases) Rules.Validate(purchase);
        foreach (var sale in ledger.Sales) Rules.Validate(sale);
        foreach (var opening in ledger.OpeningLots) Rules.Validate(opening);
        var brandNames = ledger.Brands.Select(x => Rules.Normalize(x.Name)).ToHashSet();
        if (brandNames.Count != ledger.Brands.Count) throw new InvalidDataException("نام برند تکراری است.");
        if (ledger.Items.Any(x => !brandNames.Contains(Rules.Normalize(x.Brand)))) throw new InvalidDataException("برای یکی از کالاها تنظیمات برند ثبت نشده است.");
        var itemByCode = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        if (itemByCode.Count != ledger.Items.Count) throw new InvalidDataException("کد کالا تکراری است.");
        if (ledger.Purchases.Any(x => !itemByCode.ContainsKey(x.Code)) || ledger.Sales.Any(x => !itemByCode.ContainsKey(x.Code))) throw new InvalidDataException("برای یکی از تراکنش‌ها کالا تعریف نشده است.");

        var result = new LedgerCalculation();
        var lots = new List<Lot>();
        var entries = ledger.Purchases.Where(x => throughDate == null || string.CompareOrdinal(x.Date, throughDate) <= 0).Select(x => new Entry(x.Date, 0, x.Id, x, null))
            .Concat(ledger.Sales.Where(x => throughDate == null || string.CompareOrdinal(x.Date, throughDate) <= 0).Select(x => new Entry(x.Date, 1, x.Id, null, x)))
            .OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal);
        lots.AddRange(ledger.OpeningLots.Select(x => new Lot(x)));
        foreach (var entry in entries)
        {
            if (entry.Purchase is not null) { lots.Add(new Lot(entry.Purchase)); continue; }
            var sale = entry.Sale!; var remaining = sale.Quantity; var cost = 0m;
            foreach (var lot in lots.Where(x => x.Code.Equals(sale.Code, StringComparison.OrdinalIgnoreCase) && x.Remaining > 0).OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var used = Math.Min(remaining, lot.Remaining); lot.Remaining -= used; remaining -= used; cost += used * lot.UnitCost;
                if (remaining == 0) break;
            }
            var split = Rules.SplitSale(sale);
            result.Sales[sale.Id] = new SaleSettlement(cost, split.Cash, split.Credit, remaining);
        }
        foreach (var group in lots.Where(x => x.Remaining > 0).GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            if (!itemByCode.TryGetValue(group.Key, out var item)) continue;
            result.Stock.Add(new StockRow(item.Code, item.Name, item.Brand, group.Sum(x => x.Remaining), group.Sum(x => x.Remaining * x.UnitCost)));
        }
        return result;
    }

    public static LedgerTotal SummarizeMonth(Ledger ledger, Month month)
    {
        var calculation = Calculate(ledger);
        var sales = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == month.Key).ToList();
        var settled = sales.Select(x => calculation.Sales[x.Id]).ToList();
        var codes = sales.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var items = ledger.Items.Where(x => codes.Contains(x.Code, StringComparer.OrdinalIgnoreCase)).ToList();
        return new LedgerTotal(settled.Sum(x => x.Cost), settled.Sum(x => x.Cash), settled.Sum(x => x.Credit), settled.Sum(x => x.Profit), month.FixedCost, sales.Sum(x => x.Quantity), codes.Count, items.Select(x => x.Brand).Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            InvoiceSales = sales.Sum(Rules.NetSaleBase),
            CashDiscountAmount = sales.Sum(Rules.CashDiscountAmount),
            Shortage = settled.Sum(x => x.Shortage)
        };
    }

    public static SaleSettlement PreviewSale(Ledger ledger, Sale sale)
    {
        var preview = ledger with { Sales = [.. ledger.Sales, sale] };
        return Calculate(preview).Sales[sale.Id];
    }
}
