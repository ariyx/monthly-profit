using Profit.Core;
using System.IO.Compression;

static class Test
{
    public static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{name}: expected {expected}, got {actual}");
    }

    public static void True(bool condition, string name)
    {
        if (!condition) throw new Exception($"{name}: false");
    }

    public static void Throws(Action action, string name)
    {
        try { action(); }
        catch { return; }
        throw new Exception($"{name}: expected exception");
    }
}

public static class Program
{
public static void Main()
{
    var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "monthly-profit-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var fikores = BrandPrefixRules.Defaults.Single(x => x.Name == "Fikores");
    var item = new CatalogItem { Code = "106001", Name = "کالای آزمایشی", Brand = fikores.Name };
    var ledger = new Ledger { Brands = [fikores], Items = [item] };

    var draft = BrandMonthRules.DraftFor(ledger, "1405/06");
    Test.True(!draft.IsConfirmed && draft.SourceMonthKey == "تنظیمات پیش‌فرض", "first month must require confirmation");
    Test.Equal(.25m, fikores.PurchaseDiscount, "default purchase discount");
    Test.Equal(.05m, fikores.Offer, "default offer");
    Test.Equal(.04m, fikores.Markup, "default markup");
    ledger = BrandMonthRules.Confirm(ledger, "1405/06", draft.Rates);
    Test.True(BrandMonthRules.IsConfirmed(ledger, "1405/06"), "month confirmation");

    Sale AddSale(string id, decimal unitPrice)
    {
        var rate = BrandMonthRules.RequireConfirmed(ledger, fikores.Name, "1405/06");
        return BrandMonthRules.Apply(new Sale
        {
            Id = id, Date = "14050627", Code = item.Code, Customer = "مشتری", Quantity = 1,
            UnitPrice = unitPrice, Total = unitPrice, Deductions = 0
        }, rate, "1405/06");
    }

    var sale = AddSale("sale-1", 1_040_000m);
    var result = LedgerCalculator.PreviewSale(sale);
    Test.Equal(1_000_000m, result.GrossPurchaseUnit, "reverse gross purchase unit");
    Test.Equal(700_000m, result.NetCostUnit, "net unit after brand discounts");
    Test.Equal(700_000m, result.Cost, "automatic cost");
    Test.Equal(296_400m, result.Cash, "cash after cash discount");
    Test.Equal(728_000m, result.Credit, "credit receipt");
    Test.Equal(324_400m, result.Profit, "sale profit");

    var moreExpensiveSale = AddSale("sale-2", 1_560_000m);
    var expensive = LedgerCalculator.PreviewSale(moreExpensiveSale);
    Test.Equal(1_500_000m, expensive.GrossPurchaseUnit, "per-sale reverse calculation");
    Test.Equal(1_050_000m, expensive.Cost, "per-sale cost does not reuse old price");

    var manual = sale with { Id = "sale-manual", EstimatedCostOverride = 800_000m, EstimatedCostOverrideNote = "فاکتور واقعی" };
    var manualResult = LedgerCalculator.PreviewSale(manual);
    Test.Equal(800_000m, manualResult.Cost, "manual cost override");
    Test.True(manualResult.HasManualCost, "manual cost marker");

    ledger = ledger with { Sales = [sale] };
    var changedRates = draft.Rates.Select(r => r.BrandName == fikores.Name ? r with { Markup = .10m } : r).ToList();
    ledger = BrandMonthRules.Confirm(ledger, "1405/06", changedRates);
    Test.Equal(.10m, ledger.Sales.Single().Markup, "confirmation refreshes same-month sale snapshot");
    var nextDraft = BrandMonthRules.DraftFor(ledger, "1405/07");
    Test.Equal("1405/06", nextDraft.SourceMonthKey, "new month inherits latest confirmed rates");
    Test.True(!nextDraft.IsConfirmed, "inherited month still requires confirmation");
    Test.Throws(() => BrandMonthRules.RequireConfirmed(ledger, fikores.Name, "1405/07"), "unconfirmed month blocks sale");

    var discounted = BrandMonthRules.Apply(new Sale { Id = "sale-discount", Date = "14050627", Code = item.Code, Customer = "مشتری", Quantity = 1, UnitPrice = 1_040_000m, Total = 1_040_000m, Deductions = 40_000m }, BrandMonthRules.RequireConfirmed(ledger, fikores.Name, "1405/06"), "1405/06");
    var discountedResult = LedgerCalculator.PreviewSale(discounted);
    Test.Equal(985_000m, discountedResult.Sales, "invoice deductions and cash discount are not double counted");

    var unknownItem = new CatalogItem { Code = "999001", Name = "کالای بی‌برند" };
    var pendingSale = new Sale { Id = "sale-pending", Date = "14050702", Code = unknownItem.Code, Customer = "مشتری", Quantity = 2, UnitPrice = 500_000m, Total = 1_000_000m, Deductions = 0 };
    var pendingLedger = ledger with { Items = [.. ledger.Items, unknownItem], Sales = [pendingSale] };
    var pendingCalculation = LedgerCalculator.Calculate(pendingLedger);
    Test.True(!pendingCalculation.Sales.ContainsKey(pendingSale.Id), "unknown-brand sale is retained but not calculated");
    var pendingTotal = LedgerCalculator.SummarizeMonth(pendingLedger, new Month { Key = "1405/07" }, pendingCalculation);
    Test.Equal(1, pendingTotal.PendingSalesCount, "pending sale count");
    Test.Equal(1_000_000m, pendingTotal.PendingInvoiceSales, "pending invoice amount");
    pendingLedger = pendingLedger with { Items = [item, unknownItem with { Brand = fikores.Name }] };
    pendingLedger = BrandMonthRules.ApplyPendingSalesForItem(pendingLedger, unknownItem.Code);
    Test.True(!Rules.HasRateSnapshot(pendingLedger.Sales.Single()), "brand assignment waits for month confirmation");
    pendingLedger = BrandMonthRules.Confirm(pendingLedger, "1405/07", BrandMonthRules.DraftFor(pendingLedger, "1405/07").Rates);
    Test.True(Rules.HasRateSnapshot(pendingLedger.Sales.Single()), "confirmed brand month calculates assigned pending sale");

    var dbPath = System.IO.Path.Combine(root, "monthly-profit.sqlite");
    var store = new Store(dbPath);
    store.Save(new Month { Key = "1405/06" });
    store.SaveLedger(ledger);
    Test.Equal("1405/06", new Store(dbPath).Load("1405/06").Key, "stored month opens after schema initialization");
    Test.Equal(1, new Store(dbPath).LoadLedger().Sales.Count, "stored sales ledger");

    var xlsx = System.IO.Path.Combine(root, "sales.xlsx");
    ExcelTransfer.ExportLedgerMonth(ledger, new Month { Key = "1405/06" }, xlsx);
    using var zip = ZipFile.OpenRead(xlsx);
    Test.True(zip.Entries.Any(e => e.FullName == "xl/worksheets/sheet1.xml"), "excel export sheet");
        Console.WriteLine("Business, database and spreadsheet tests passed.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
}
