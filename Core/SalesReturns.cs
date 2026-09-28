namespace Profit.Core;

public sealed record ReturnAllocation(string ReturnId, string SaleId, decimal Quantity, decimal ReturnNetAmount);

public sealed class ReturnMatchingResult
{
    public List<ReturnAllocation> Allocations { get; } = [];
    public List<SaleReturn> PendingReturns { get; } = [];
}

// تطبیق‌ها به‌صورت قطعی و قابل بازتولید ساخته می‌شوند. ردیف برگشتِ بدون
// فروش کافی هیچ تخصیصی نمی‌گیرد تا هرگز بخشی از آن اشتباهاً در سود اعمال نشود.
public static class SalesReturnMatcher
{
    public static ReturnMatchingResult Match(IEnumerable<Sale> sales, IEnumerable<SaleReturn> returns)
    {
        var orderedSales = sales.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.ImportKey, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        var remaining = orderedSales.ToDictionary(x => x.Id, x => x.Quantity, StringComparer.Ordinal);
        var result = new ReturnMatchingResult();

        foreach (var saleReturn in returns.OrderBy(x => x.Date, StringComparer.Ordinal).ThenBy(x => x.ImportKey, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var candidates = orderedSales
                .Where(sale => Rules.MatchText(sale.Customer).Equals(Rules.MatchText(saleReturn.Customer), StringComparison.OrdinalIgnoreCase)
                    && sale.Code.Equals(saleReturn.Code, StringComparison.OrdinalIgnoreCase)
                    && sale.UnitPrice == saleReturn.UnitPrice
                    && string.CompareOrdinal(sale.Date, saleReturn.Date) <= 0
                    && remaining[sale.Id] > 0)
                .ToList();

            // تعداد دقیق، اولویت اول است؛ در تساوی، نزدیک‌ترین فروش قبل از برگشت.
            var exact = candidates.Where(x => remaining[x.Id] == saleReturn.Quantity)
                .OrderByDescending(x => x.Date, StringComparer.Ordinal).ThenByDescending(x => x.ImportKey, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (exact is not null) candidates = [exact];
            else candidates = candidates.OrderByDescending(x => x.Date, StringComparer.Ordinal).ThenByDescending(x => x.ImportKey, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).ToList();

            if (candidates.Sum(x => remaining[x.Id]) < saleReturn.Quantity)
            {
                result.PendingReturns.Add(saleReturn);
                continue;
            }

            var quantityLeft = saleReturn.Quantity;
            foreach (var candidate in candidates)
            {
                if (quantityLeft == 0) break;
                var allocated = Math.Min(quantityLeft, remaining[candidate.Id]);
                remaining[candidate.Id] -= allocated;
                quantityLeft -= allocated;
                result.Allocations.Add(new ReturnAllocation(saleReturn.Id, candidate.Id, allocated, Rules.ReturnNetBase(saleReturn) * allocated / saleReturn.Quantity));
            }
        }
        return result;
    }
}
