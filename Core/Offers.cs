namespace Profit.Core;

public sealed record OfferEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public bool IsReturn { get; init; }
    public string Date { get; init; } = "";
    public string InvoiceNumber { get; init; } = "";
    public int SourceRow { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string Customer { get; init; } = "";
    public decimal Quantity { get; init; }
    public string ImportKey { get; init; } = "";
    public string SourceIdentity { get; init; } = "";
    public string SourceFingerprint { get; init; } = "";
    public string ManualSaleId { get; init; } = "";
    public decimal? ManualUnitPrice { get; init; }
    public string ManualReason { get; init; } = "";
    public DateTime? ManualUpdatedUtc { get; init; }
}

public sealed record OfferResolution(OfferEntry Entry, string SaleId, decimal? UnitPrice, decimal Amount, string Status);

public sealed class OfferCalculation
{
    public List<OfferResolution> Rows { get; } = [];
    public Dictionary<string, decimal> SaleAdjustments { get; } = [];
    public Dictionary<string, decimal> UnlinkedReturnCredits { get; } = [];
}

public static class OfferRules
{
    public static bool IsPromotional(string text)
    {
        var name = Rules.Normalize(text).ToLowerInvariant();
        return new[] { "تستر", "سمپل", "تبلیغ", "نمونه", "استند", "بگ", "tester", "sample" }.Any(name.Contains);
    }

    static bool SameCustomer(string a, string b) => Rules.MatchText(a).Equals(Rules.MatchText(b), StringComparison.OrdinalIgnoreCase);
    static bool SameCode(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    public static OfferCalculation Calculate(Ledger ledger)
    {
        var result = new OfferCalculation();
        foreach (var offer in ledger.Offers)
        {
            if (!Rules.ValidDate(offer.Date) || offer.Quantity <= 0 || string.IsNullOrWhiteSpace(offer.Code)) throw new InvalidDataException("اطلاعات آفر نامعتبر است.");
            if (offer.ManualUnitPrice.HasValue && (offer.ManualUnitPrice <= 1 || string.IsNullOrWhiteSpace(offer.ManualReason))) throw new InvalidDataException("قیمت دستی آفر و دلیل آن نامعتبر است.");
            if (offer.ManualSaleId.Length > 0 && (!ledger.Sales.Any(x => x.Id == offer.ManualSaleId && x.Date == offer.Date && SameCustomer(x.Customer, offer.Customer)) || string.IsNullOrWhiteSpace(offer.ManualReason))) throw new InvalidDataException("فروش مرتبط دستی باید برای همین مشتری و تاریخ باشد و دلیل آن ثبت شود.");
        }
        if (ledger.Offers.Select(x => x.Id).Distinct().Count() != ledger.Offers.Count) throw new InvalidDataException("شناسه آفر تکراری است.");

        void Adjust(string saleId, decimal value) => result.SaleAdjustments[saleId] = result.SaleAdjustments.GetValueOrDefault(saleId) + value;
        foreach (var offer in ledger.Offers.Where(x => !x.IsReturn).OrderBy(x => x.Date).ThenBy(x => x.SourceRow))
        {
            var manual = ledger.Sales.FirstOrDefault(s => s.Id == offer.ManualSaleId);
            var candidates = ledger.Sales.Where(s => offer.InvoiceNumber.Length > 0 && s.InvoiceNumber == offer.InvoiceNumber && s.Date == offer.Date && SameCustomer(s.Customer, offer.Customer) && SameCode(s.Code, offer.Code)).ToList();
            var distinctPrices = candidates.Select(s => s.UnitPrice).Distinct().Count();
            var sale = manual ?? (distinctPrices == 1 ? candidates.OrderBy(s => s.SourceRow < offer.SourceRow ? 0 : 1).ThenBy(s => Math.Abs(s.SourceRow - offer.SourceRow)).FirstOrDefault() : null);
            if (sale is null)
            {
                result.Rows.Add(new(offer, "", null, 0, distinctPrices > 1 ? "چند فروش با قیمت متفاوت؛ تعیین فروش مرتبط" : "فروش مرتبط یافت نشد"));
                continue;
            }
            var amount = offer.Quantity * sale.UnitPrice;
            Adjust(sale.Id, -amount);
            result.Rows.Add(new(offer, sale.Id, sale.UnitPrice, amount, manual is null ? "تطبیق خودکار" : "تطبیق دستی"));
        }

        var original = result.Rows.Where(x => !x.Entry.IsReturn && x.UnitPrice.HasValue).ToList();
        var remaining = original.ToDictionary(x => x.Entry.Id, x => x.Entry.Quantity);
        foreach (var offer in ledger.Offers.Where(x => x.IsReturn).OrderBy(x => x.Date).ThenBy(x => x.SourceRow).ThenBy(x => x.Id))
        {
            var siblings = ledger.Returns.Where(r => offer.InvoiceNumber.Length > 0 && r.InvoiceNumber == offer.InvoiceNumber && r.Date == offer.Date && SameCustomer(r.Customer, offer.Customer) && SameCode(r.Code, offer.Code)).ToList();
            var prices = siblings.Select(x => x.UnitPrice).Distinct().ToList();
            var candidates = original.Where(x => SameCustomer(x.Entry.Customer, offer.Customer) && SameCode(x.Entry.Code, offer.Code) && string.CompareOrdinal(x.Entry.Date, offer.Date) <= 0 && remaining[x.Entry.Id] > 0 && (prices.Count == 0 || prices.Contains(x.UnitPrice!.Value))).ToList();
            if (prices.Count > 1 || candidates.Select(x => x.UnitPrice).Distinct().Count() > 1)
            {
                candidates.Clear(); // Never select a price silently when the original sale is ambiguous.
            }
            if (candidates.Sum(x => remaining[x.Entry.Id]) >= offer.Quantity)
            {
                var exact = candidates.Where(x => remaining[x.Entry.Id] == offer.Quantity).OrderByDescending(x => x.Entry.Date).ThenByDescending(x => x.Entry.SourceRow).FirstOrDefault();
                var ordered = exact is null ? candidates.OrderByDescending(x => x.Entry.Date).ThenByDescending(x => x.Entry.SourceRow).ToList() : new List<OfferResolution> { exact };
                var left = offer.Quantity; var amount = 0m; var saleIds = new List<string>();
                foreach (var candidate in ordered)
                {
                    var quantity = Math.Min(left, remaining[candidate.Entry.Id]);
                    remaining[candidate.Entry.Id] -= quantity; left -= quantity;
                    var value = quantity * candidate.UnitPrice!.Value; amount += value;
                    Adjust(candidate.SaleId, value); saleIds.Add(candidate.SaleId);
                    if (left == 0) break;
                }
                result.Rows.Add(new(offer, string.Join(",", saleIds.Distinct()), amount / offer.Quantity, amount, "برگشت آفر تطبیق داده شد"));
            }
            else if (offer.ManualUnitPrice.HasValue && !original.Any(x => SameCustomer(x.Entry.Customer, offer.Customer) && SameCode(x.Entry.Code, offer.Code) && string.CompareOrdinal(x.Entry.Date, offer.Date) <= 0))
            {
                // An automatic match supersedes this manual valuation when older data is imported.
                var amount = offer.Quantity * offer.ManualUnitPrice.Value;
                var month = Rules.MonthOf(offer.Date);
                result.UnlinkedReturnCredits[month] = result.UnlinkedReturnCredits.GetValueOrDefault(month) + amount;
                result.Rows.Add(new(offer, "", offer.ManualUnitPrice, amount, "ارزش‌گذاری دستی؛ فروش اصلی موجود نیست"));
            }
            else result.Rows.Add(new(offer, "", null, 0, "آفر فروش اصلی با قیمت و تعداد کافی یافت نشد"));
        }
        return result;
    }
}
