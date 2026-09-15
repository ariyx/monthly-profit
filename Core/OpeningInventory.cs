namespace Profit.Core;

public sealed record OpeningInventoryReview(List<OpeningLot> Lots, List<ImportIssue> Issues, int Purchases, int Sales);

public static class OpeningInventory
{
    sealed class Lot(ImportedTransaction row)
    {
        public string Code { get; } = row.Code;
        public string Date { get; } = row.Date;
        public string Id { get; } = row.ImportKey;
        public decimal Remaining { get; set; } = row.Quantity;
        public decimal UnitCost { get; } = (row.Total - row.Deductions) / row.Quantity;
    }
    sealed record Entry(string Date, int Order, string Id, ImportedTransaction? Purchase, ImportedTransaction? Sale);

    // فقط ماندهٔ واقعیِ پایان ۱۴۰۴ را می‌سازد؛ فروش و خرید ۱۴۰۴ وارد گزارش‌های ۱۴۰۵ نمی‌شوند.
    public static OpeningInventoryReview Build(TransactionImportReview purchases, TransactionImportReview sales)
    {
        var buys = purchases.Rows.Where(x => x.Date.StartsWith("1404", StringComparison.Ordinal)).ToList();
        var sells = sales.Rows.Where(x => x.Date.StartsWith("1404", StringComparison.Ordinal)).ToList();
        var lots = new List<Lot>(); var issues = new List<ImportIssue>();
        var entries = buys.Select(x => new Entry(x.Date, 0, x.ImportKey, x, null))
            .Concat(sells.Select(x => new Entry(x.Date, 1, x.ImportKey, null, x)))
            .OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Purchase is not null) { lots.Add(new Lot(entry.Purchase)); continue; }
            var sale = entry.Sale!; var remaining = sale.Quantity;
            foreach (var lot in lots.Where(x => x.Code.Equals(sale.Code, StringComparison.OrdinalIgnoreCase) && x.Remaining > 0).OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var used = Math.Min(remaining, lot.Remaining); lot.Remaining -= used; remaining -= used;
                if (remaining == 0) break;
            }
            if (remaining > 0) issues.Add(new ImportIssue(sale.Row, $"کد {sale.Code}: کسری تاریخی {Rules.Money(remaining)} واحد؛ این مقدار به موجودی افتتاحیه افزوده نشد."));
        }
        var result = lots.Where(x => x.Remaining > 0).Select(x => new OpeningLot { Code = x.Code, Quantity = x.Remaining, UnitCost = x.UnitCost, SourceDate = x.Date }).ToList();
        return new OpeningInventoryReview(result, issues, buys.Count, sells.Count);
    }
}
