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

    var prefixBrand = new Brand { Name = "Salome", CodePrefixes = ["118"] };
    var shampooBrand = new Brand { Name = "Shampoo Salome", ExactProductCodes = ["11890329"], Markup = 1.50m };
    var creamBrand = new Brand { Name = "Cream Salome", ExactProductCodes = ["11832944"], Markup = 2.50m };
    var exactBrands = new[] { prefixBrand, shampooBrand, creamBrand };
    Test.Equal(shampooBrand.Name, BrandPrefixRules.Detect(exactBrands, "11890329")!.Name, "exact code overrides matching prefix");
    Test.Equal(prefixBrand.Name, BrandPrefixRules.Detect(exactBrands, "11800000")!.Name, "prefix detects when no exact code exists");
    Test.Equal(creamBrand.Name, BrandPrefixRules.Detect(exactBrands, "11832944")!.Name, "exact codes under one prefix map to separate brands");
    Test.Throws(() => BrandPrefixRules.ValidateUnique([shampooBrand, creamBrand with { ExactProductCodes = ["11890329"] }]), "duplicate exact codes are rejected");
    var exactItems = new[]
    {
        new CatalogItem { Code = "11800000", Name = "Prefix product", Brand = BrandPrefixRules.Detect(exactBrands, "11800000")!.Name },
        new CatalogItem { Code = "11890329", Name = "Exact product", Brand = BrandPrefixRules.Detect(exactBrands, "11890329")!.Name }
    };
    var exactLedger = new Ledger { Brands = exactBrands.ToList(), Items = exactItems.ToList() };
    var exactDraft = BrandMonthRules.DraftFor(exactLedger, "1405/06");
    Test.Equal(3, exactDraft.Rates.Count, "exact-code brand receives an independent monthly rate");
    exactLedger = BrandMonthRules.Confirm(exactLedger, "1405/06", exactDraft.Rates);
    var prefixRate = BrandMonthRules.RequireConfirmed(exactLedger, prefixBrand.Name, "1405/06");
    var shampooRate = BrandMonthRules.RequireConfirmed(exactLedger, shampooBrand.Name, "1405/06");
    Test.Equal(1.50m, shampooRate.Markup, "exact-code brand keeps its own monthly settings");
    var previouslyAssigned = exactLedger with
    {
        Items = [new CatalogItem { Code = "11890329", Name = "Existing exact product", Brand = prefixBrand.Name }],
        Sales = [BrandMonthRules.Apply(new Sale { Id = "existing-exact-sale", Date = "14050627", Code = "11890329", Customer = "test", Quantity = 1, UnitPrice = 250m, Total = 250m }, prefixRate, "1405/06")]
    };
    var migratedExact = BrandMonthRules.ApplyExactProductCodeAssignments(previouslyAssigned);
    Test.Equal(shampooBrand.Name, migratedExact.Items.Single().Brand, "existing exact-code item moves from prefix brand");
    Test.Equal(1.50m, migratedExact.Sales.Single().Markup, "existing exact-code sale refreshes its independent snapshot");
    var exactSale = BrandMonthRules.Apply(new Sale { Id = "exact-sale", Date = "14050627", Code = "11890329", Customer = "test", Quantity = 1, UnitPrice = 250m, Total = 250m }, shampooRate, "1405/06");
    var prefixSale = BrandMonthRules.Apply(new Sale { Id = "prefix-sale", Date = "14050627", Code = "11800000", Customer = "test", Quantity = 1, UnitPrice = 104m, Total = 104m }, prefixRate, "1405/06");
    exactLedger = exactLedger with { Sales = [exactSale, prefixSale] };
    Test.Equal(1.50m, exactLedger.Sales.Single(x => x.Code == "11890329").Markup, "exact-code sale uses the independent brand snapshot");
    Test.Equal(2, LedgerCalculator.SummarizeMonth(exactLedger, new Month { Key = "1405/06" }).Brands, "exact-code sale remains separate in brand reporting");
    var legacyLedger = System.Text.Json.JsonSerializer.Deserialize<Ledger>("{\"Brands\":[{\"Name\":\"Legacy\",\"CodePrefixes\":[\"118\"]}]}", Rules.Json)!;
    Test.Equal("Legacy", BrandPrefixRules.Detect(legacyLedger.Brands, "118777")!.Name, "prefix-only saved data remains compatible");
    BrandMonthRules.Validate(new BrandRate { BrandName = "Markup 150", Markup = 1.50m, CashShare = .30m, CreditShare = .70m });
    BrandMonthRules.Validate(new BrandRate { BrandName = "Markup 250", Markup = 2.50m, CashShare = .30m, CreditShare = .70m });
    var markup150 = new Sale { Date = "14050627", Code = "11890329", Customer = "test", Quantity = 1, UnitPrice = 250m, Total = 250m, CashShare = .30m, CreditShare = .70m, Markup = 1.50m, RateMonthKey = "1405/06" };
    var markup250 = markup150 with { UnitPrice = 350m, Total = 350m, Markup = 2.50m };
    Test.Equal(100m, LedgerCalculator.PreviewSale(markup150).GrossPurchaseUnit, "150 percent markup calculation");
    Test.Equal(100m, LedgerCalculator.PreviewSale(markup250).GrossPurchaseUnit, "250 percent markup calculation");
    Test.Throws(() => BrandMonthRules.Validate(new BrandRate { BrandName = "Negative", Markup = -.01m, CashShare = .30m, CreditShare = .70m }), "negative markup is rejected");
    Test.Throws(() => BrandMonthRules.Validate(new BrandRate { BrandName = "Discount", PurchaseDiscount = 1.01m, CashShare = .30m, CreditShare = .70m }), "non-markup percentage upper limit remains");
    Test.Throws(() => BrandMonthRules.Validate(new BrandRate { BrandName = "Shares", CashShare = .31m, CreditShare = .70m }), "cash and credit shares must total 100 percent");
    var draft = BrandMonthRules.DraftFor(ledger, "1405/06");
    Test.True(!draft.IsConfirmed && draft.SourceMonthKey == "پیش‌فرض برند", "first month must require confirmation");
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

    var timelineLedger = ledger with { Sales = [sale with { Id = "before-change", Date = "14050614", Markup = .10m }, sale with { Id = "on-change", Date = "14050615", Markup = .10m }, sale with { Id = "after-change", Date = "14050625", Markup = .10m }] };
    var firstPeriod = new MarkupPeriod { Id = "period-15", BrandName = fikores.Name, StartDate = "14050615", EndDate = "14050619", Markup = .50m };
    timelineLedger = MarkupRules.SetPeriod(timelineLedger, firstPeriod, false);
    Test.Equal(.10m, timelineLedger.Sales.Single(s => s.Id == "before-change").Markup, "markup change preserves sales before start date");
    Test.Equal(.50m, timelineLedger.Sales.Single(s => s.Id == "on-change").Markup, "markup change includes start date");
    Test.Equal(sale.PurchaseDiscount, timelineLedger.Sales.Single(s => s.Id == "on-change").PurchaseDiscount, "dated markup leaves monthly discounts unchanged");
    var secondPeriod = new MarkupPeriod { Id = "period-20", BrandName = fikores.Name, StartDate = "14050620", EndDate = "14050631", Markup = 1.50m };
    timelineLedger = MarkupRules.SetPeriod(timelineLedger, secondPeriod, false);
    timelineLedger = MarkupRules.SetPeriod(timelineLedger, firstPeriod with { Markup = .60m }, true);
    Test.Equal(.60m, timelineLedger.Sales.Single(s => s.Id == "on-change").Markup, "correcting one period refreshes its sales");
    Test.Equal(1.50m, timelineLedger.Sales.Single(s => s.Id == "after-change").Markup, "correcting earlier period preserves later period");
    var timelineNextDraft = BrandMonthRules.DraftFor(timelineLedger, "1405/07");
    Test.Equal(1.50m, timelineNextDraft.Rates.Single().Markup, "next month draft carries final dated markup");
    Test.True(!timelineNextDraft.IsConfirmed, "carried markup still needs monthly confirmation");
    var baseCorrection = timelineLedger.BrandMonths.Single().Rates.Select(r => r with { Markup = .20m });
    timelineLedger = BrandMonthRules.Confirm(timelineLedger, "1405/06", baseCorrection);
    Test.Equal(.20m, timelineLedger.Sales.Single(s => s.Id == "before-change").Markup, "monthly base correction refreshes only baseline interval");
    Test.Equal(.60m, timelineLedger.Sales.Single(s => s.Id == "on-change").Markup, "monthly base correction preserves dated interval");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, firstPeriod with { Id = "duplicate-date" }, false), "duplicate markup start date rejected");
    var movedPeriod = MarkupRules.SetPeriod(timelineLedger, firstPeriod with { StartDate = "14050616", Markup = .60m }, true);
    Test.Equal(.20m, movedPeriod.Sales.Single(s => s.Id == "on-change").Markup, "moving start restores monthly baseline on freed day");
    Test.Equal(1.50m, movedPeriod.Sales.Single(s => s.Id == "after-change").Markup, "moving one interval preserves other interval");
    var dayOne = MarkupRules.SetPeriod(timelineLedger, new MarkupPeriod { BrandName = fikores.Name, StartDate = "14050601", EndDate = "14050602", Markup = .30m }, false);
    Test.Equal(.30m, MarkupRules.Resolve(dayOne, fikores.Name, "14050601", .20m), "explicit interval may begin on day one");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, new MarkupPeriod { BrandName = fikores.Name, StartDate = "14050715", EndDate = "14050730", Markup = .30m }, false), "period in unconfirmed month rejected");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, firstPeriod with { Markup = -.01m }, true), "negative dated markup rejected");

    var specialSale = BrandMonthRules.Apply(sale with { Id = "same-day-exception", Date = "14050615", MarkupOverride = .80m, MarkupOverrideReason = "قیمت‌گذاری متفاوت همین فروش" }, BrandMonthRules.RequireConfirmed(timelineLedger, fikores.Name, "1405/06"), "1405/06", timelineLedger);
    timelineLedger = timelineLedger with { Sales = [.. timelineLedger.Sales, specialSale] };
    timelineLedger = MarkupRules.SetPeriod(timelineLedger, firstPeriod with { Markup = .70m }, true);
    Test.Equal(.80m, timelineLedger.Sales.Single(s => s.Id == specialSale.Id).Markup, "row markup override survives interval correction");
    timelineLedger = BrandMonthRules.Confirm(timelineLedger, "1405/06", timelineLedger.BrandMonths.Single().Rates);
    Test.Equal(.80m, timelineLedger.Sales.Single(s => s.Id == specialSale.Id).Markup, "row markup override survives monthly confirmation");
    var restoredMarkup = BrandMonthRules.Apply(specialSale with { MarkupOverride = null, MarkupOverrideReason = "" }, BrandMonthRules.RequireConfirmed(timelineLedger, fikores.Name, "1405/06"), "1405/06", timelineLedger);
    Test.Equal(.70m, restoredMarkup.Markup, "clearing row exception restores dated markup");
    var lateImportedSale = BrandMonthRules.Apply(sale with { Id = "late-import", Date = "14050619" }, BrandMonthRules.RequireConfirmed(timelineLedger, fikores.Name, "1405/06"), "1405/06", timelineLedger);
    Test.Equal(.70m, lateImportedSale.Markup, "late import resolves markup by transaction date");
    Test.Equal(1.50m, MarkupRules.Resolve(timelineLedger, fikores.Name, "14050620", .20m), "next interval includes its start day");
    Test.Equal(0.30m, MarkupRules.Resolve(timelineLedger, "unrelated brand", "14050620", .30m), "markup dates are scoped to brand");
    Test.Throws(() => Rules.Validate(specialSale with { MarkupOverrideReason = "" }), "row exception requires a reason");
    Test.Throws(() => Rules.Validate(specialSale with { MarkupOverride = -.01m }), "negative row exception rejected");
    Test.Equal(sale.UnitPrice / 1.80m * (1 - sale.PurchaseDiscount - sale.Offer), LedgerCalculator.Calculate(timelineLedger).Sales[specialSale.Id].Cost, "cost uses specific row markup without changing monthly discounts");
    var timelineJuly = BrandMonthRules.Confirm(timelineLedger, "1405/07", timelineNextDraft.Rates);
    timelineJuly = timelineJuly with { Sales = [.. timelineJuly.Sales, BrandMonthRules.Apply(sale with { Id = "july", Date = "14050705" }, BrandMonthRules.RequireConfirmed(timelineJuly, fikores.Name, "1405/07"), "1405/07", timelineJuly)] };
    timelineJuly = MarkupRules.SetPeriod(timelineJuly, firstPeriod with { Markup = .90m }, true);
    Test.Equal(1.50m, timelineJuly.Sales.Single(s => s.Id == "july").Markup, "correcting prior month leaves confirmed later month unchanged");

    Test.Equal(.70m, MarkupRules.Resolve(timelineLedger, fikores.Name, "14050619", .20m), "end date is inclusive");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, new MarkupPeriod { BrandName = fikores.Name, StartDate = "14050619", EndDate = "14050621", Markup = .70m }, false), "shared end day counts as overlap");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, new MarkupPeriod { BrandName = fikores.Name, StartDate = "14050616", EndDate = "14050617", Markup = .70m }, false), "nested intervals rejected even with equal rate");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, firstPeriod with { EndDate = "14050701" }, true), "interval cannot cross month boundary");
    Test.Throws(() => MarkupRules.SetPeriod(timelineLedger, firstPeriod with { EndDate = "14050614" }, true), "reversed interval rejected");
    Test.True(!Rules.ValidDate("14050731") && !Rules.ValidDate("14050232"), "actual Persian month length validated");
    Test.Equal("14050631", Rules.LastDate("1405/06"), "summer month has 31 days");
    Test.Equal("14050730", Rules.LastDate("1405/07"), "autumn month has 30 days");
    var persian = new System.Globalization.PersianCalendar();
    Test.Equal("140512" + persian.GetDaysInMonth(1405, 12).ToString("00"), Rules.LastDate("1405/12"), "Esfand respects leap year");

    var shortened = MarkupRules.SetPeriod(timelineLedger, secondPeriod with { EndDate = "14050624" }, true);
    Test.Equal(.20m, shortened.Sales.Single(s => s.Id == "after-change").Markup, "shortening end refreshes sale newly outside range");
    Test.Equal(.20m, BrandMonthRules.DraftFor(shortened, "1405/07").Rates.Single().Markup, "next month inherits base when final day is outside range");
    var deletedRange = MarkupRules.RemovePeriod(timelineLedger, firstPeriod.Id);
    Test.Equal(.20m, deletedRange.Sales.Single(s => s.Id == "on-change").Markup, "range deletion restores baseline");
    Test.Equal(.80m, deletedRange.Sales.Single(s => s.Id == specialSale.Id).Markup, "range deletion preserves individual sale exception");
    var manualInRange = timelineLedger with { Sales = [.. timelineLedger.Sales, manual with { Id = "manual-in-range", Date = "14050616" }] };
    manualInRange = MarkupRules.SetPeriod(manualInRange, firstPeriod with { Markup = 2.50m }, true);
    Test.Equal(800_000m, LedgerCalculator.Calculate(manualInRange).Sales["manual-in-range"].Cost, "manual cost overrides dated markup");
    var legacyRanges = timelineLedger with { MarkupPeriods = timelineLedger.MarkupPeriods.Select(p => p with { EndDate = "" }).ToList() };
    var upgradedRanges = MarkupRules.UpgradeLegacy(legacyRanges);
    Test.Equal("14050619", upgradedRanges.MarkupPeriods.Single(p => p.Id == firstPeriod.Id).EndDate, "legacy range ends before next start");
    Test.Equal("14050631", upgradedRanges.MarkupPeriods.Single(p => p.Id == secondPeriod.Id).EndDate, "legacy final range ends on actual final day");
    Test.Equal(LedgerCalculator.Calculate(legacyRanges).Sales["on-change"], LedgerCalculator.Calculate(upgradedRanges).Sales["on-change"], "legacy upgrade preserves financial result");

    var extraBrand = new Brand { Name = "Independent brand", CodePrefixes = ["888"], Markup = .40m };
    var independent = BrandCatalogRules.Upsert(timelineLedger, extraBrand);
    Test.True(BrandMonthRules.TryConfirmed(independent, fikores.Name, "1405/06") is not null, "new brand preserves prior brand approval");
    Test.True(!BrandMonthRules.IsConfirmed(independent, "1405/06"), "new brand alone needs month approval");
    Test.True(BrandMonthRules.TryConfirmed(independent, extraBrand.Name, "1405/06") is null, "new brand starts unconfirmed");
    var independentItem = new CatalogItem { Code = "8880001", Name = "Independent item", Brand = extraBrand.Name };
    var independentSale = sale with { Id = "pending-new-brand", Code = independentItem.Code, CashShare = 0, CreditShare = 0, RateMonthKey = "" };
    independent = independent with { Items = [.. independent.Items, independentItem], Sales = [.. independent.Sales, independentSale] };
    Test.True(!LedgerCalculator.Calculate(independent).Sales.ContainsKey(independentSale.Id), "new brand sale stays pending without confirmation");
    var oldOtherSale = independent.Sales.Single(x => x.Id == "on-change");
    independent = BrandMonthRules.Confirm(independent, "1405/06", [BrandMonthRules.FromBrand(extraBrand)]);
    Test.Equal(oldOtherSale, independent.Sales.Single(x => x.Id == "on-change"), "subset confirmation leaves other brand snapshots untouched");
    Test.Equal(.40m, independent.Sales.Single(x => x.Id == independentSale.Id).Markup, "subset approval refreshes only own pending sale");
    Test.True(BrandMonthRules.IsConfirmed(independent, "1405/06"), "all active brands now individually approved");
    Test.True(ReferenceEquals(independent, BrandMonthRules.Confirm(independent, "1405/06", [BrandMonthRules.RequireConfirmed(independent, extraBrand.Name, "1405/06")])), "unchanged confirmed settings are no-op");
    var multiSource = BrandMonthRules.Confirm(independent, "1405/07", [BrandMonthRules.RequireConfirmed(independent, extraBrand.Name, "1405/06") with { Markup = .90m }]);
    var multiDraft = BrandMonthRules.DraftFor(multiSource, "1405/08");
    Test.Equal("1405/06", multiDraft.Rates.Single(r => r.BrandName == fikores.Name).SourceMonthKey, "per-brand inheritance skips unconfirmed intervening month");
    Test.Equal("1405/07", multiDraft.Rates.Single(r => r.BrandName == extraBrand.Name).SourceMonthKey, "per-brand inheritance uses own latest approval");
    Test.True(!multiDraft.IsConfirmed, "inherited per-brand values still need approval");
    var roundtripSettings = System.Text.Json.JsonSerializer.Deserialize<Ledger>(System.Text.Json.JsonSerializer.Serialize(multiSource, Rules.Json), Rules.Json)!;
    Test.True(BrandMonthRules.TryConfirmed(roundtripSettings, extraBrand.Name, "1405/07") is not null && BrandMonthRules.TryConfirmed(roundtripSettings, fikores.Name, "1405/07") is null, "partial approvals survive serialization");
    var legacyApproval = ledger with { BrandMonths = ledger.BrandMonths.Select(m => m with { Rates = m.Rates.Select(r => r with { IsConfirmed = null }).ToList() }).ToList() };
    var legacyWithNewBrand = BrandCatalogRules.Upsert(legacyApproval, extraBrand);
    Test.True(BrandMonthRules.TryConfirmed(legacyWithNewBrand, fikores.Name, "1405/06") is not null && BrandMonthRules.TryConfirmed(legacyWithNewBrand, extraBrand.Name, "1405/06") is null, "legacy monthly approval applies only to saved brand rows");
    var unknown1 = new CatalogItem { Code = "8880011", Name = "Unknown 1" };
    var carryLedger = new Ledger { Brands = [fikores, extraBrand] };
    carryLedger = BrandMonthRules.Confirm(carryLedger, "1405/01", [BrandMonthRules.FromBrand(fikores) with { Markup = .40m }, BrandMonthRules.FromBrand(extraBrand)]);
    // Approving another brand persists an unconfirmed February draft for Fikores.
    carryLedger = BrandMonthRules.Confirm(carryLedger, "1405/02", [BrandMonthRules.FromBrand(extraBrand)]);
    Test.Equal(.40m, BrandMonthRules.DraftFor(carryLedger, "1405/02").Rates.Single(r => r.BrandName == fikores.Name).Markup, "initial next-month default");
    var januaryPeriod = new MarkupPeriod { Id = "january-carry", BrandName = fikores.Name, StartDate = "14050115", EndDate = "14050131", Markup = .60m };
    var januaryChanged = MarkupRules.SetPeriod(carryLedger, januaryPeriod, false);
    var februaryDraft = BrandMonthRules.DraftFor(januaryChanged, "1405/02").Rates.Single(r => r.BrandName == fikores.Name);
    Test.Equal(.60m, februaryDraft.Markup, "last-day markup refreshes previously persisted unconfirmed next-month default");
    Test.Equal("1405/01", februaryDraft.SourceMonthKey, "carried markup keeps its source month");
    Test.True(februaryDraft.IsConfirmed != true, "automatic carry still requires month approval");
    var marchDraft = BrandMonthRules.DraftFor(januaryChanged, "1405/03").Rates.Single(r => r.BrandName == fikores.Name);
    Test.Equal(.60m, marchDraft.Markup, "unconfirmed intervening month does not lose latest approved markup");
    var februaryApproved = BrandMonthRules.Confirm(januaryChanged, "1405/02", [februaryDraft]);
    Test.Equal(.60m, BrandMonthRules.DraftFor(februaryApproved, "1405/03").Rates.Single(r => r.BrandName == fikores.Name).Markup, "carried base continues into subsequent month");
    var januaryRevised = MarkupRules.SetPeriod(februaryApproved, januaryPeriod with { Markup = .80m }, true);
    Test.Equal(.60m, BrandMonthRules.RequireConfirmed(januaryRevised, fikores.Name, "1405/02").Markup, "already-approved next month stays fixed");
    var februaryRevised = BrandMonthRules.Confirm(februaryApproved, "1405/02", [februaryDraft with { Markup = .90m }]);
    Test.Equal(.90m, BrandMonthRules.DraftFor(februaryRevised, "1405/03").Rates.Single(r => r.BrandName == fikores.Name).Markup, "new month-specific change becomes following default");
    var decemberLedger = BrandMonthRules.Confirm(new Ledger { Brands = [fikores] }, "1405/12", [BrandMonthRules.FromBrand(fikores)]);
    decemberLedger = MarkupRules.SetPeriod(decemberLedger, new MarkupPeriod { BrandName = fikores.Name, StartDate = "14051215", EndDate = Rules.LastDate("1405/12"), Markup = 1.20m }, false);
    Test.Equal(1.20m, BrandMonthRules.DraftFor(decemberLedger, "1406/01").Rates.Single().Markup, "actual Esfand final day carries across year boundary");

    var logTime = new DateTime(2026, 10, 8, 9, 50, 0, DateTimeKind.Utc);
    Test.Equal(0, januaryChanged.BrandChanges.Count, "staging a range does not write audit history");
    var januaryLogged = BrandChangeHistory.Append(carryLedger, januaryChanged, logTime);
    var addedLog = januaryLogged.BrandChanges.Single();
    Test.Equal(logTime, addedLog.AtUtc, "audit records commit time independently of effective start");
    Test.Equal("1405/01", addedLog.MonthKey, "range audit belongs to affected month");
    Test.True(addedLog.Details.Contains("14050115 تا 14050131") && addedLog.Details.Contains("40٪") && addedLog.Details.Contains("60٪"), "added range audit includes prior markup and new effective range");
    var januaryLoggedEdit = BrandChangeHistory.Append(januaryLogged, MarkupRules.SetPeriod(januaryLogged, januaryPeriod with { Markup = .75m, StartDate = "14050116" }, true), logTime.AddMinutes(1));
    Test.Equal(2, januaryLoggedEdit.BrandChanges.Count, "range correction appends without replacing older history");
    Test.True(januaryLoggedEdit.BrandChanges.Last().Details.Contains("قبل: 14050115") && januaryLoggedEdit.BrandChanges.Last().Details.Contains("بعد: 14050116") && januaryLoggedEdit.BrandChanges.Last().Details.Contains("75٪"), "correction records both dates and percentages");
    var januaryLoggedDelete = BrandChangeHistory.Append(januaryLoggedEdit, MarkupRules.RemovePeriod(januaryLoggedEdit, januaryPeriod.Id), logTime.AddMinutes(2));
    Test.Equal("حذف بازه مارک‌آپ", januaryLoggedDelete.BrandChanges.Last().Action, "deletion creates its own audit record");
    Test.Equal(3, januaryLoggedDelete.BrandChanges.Count, "deletion retains full previous history");
    Test.Equal(3, BrandChangeHistory.Append(januaryLoggedDelete, januaryLoggedDelete).BrandChanges.Count, "unchanged commit creates no duplicate audit");
    Test.Equal(0, BrandChangeHistory.ForMonth(januaryLoggedDelete, "1405/02").Count(), "audit view excludes other months");
    Test.Equal(0, BrandChangeHistory.ForMonth(januaryLoggedDelete, "1405/01", extraBrand.Name).Count(), "brand-specific audit excludes other brands");
    Test.Equal("حذف بازه مارک‌آپ", BrandChangeHistory.ForMonth(januaryLoggedDelete, "1405/01", fikores.Name).First().Action, "audit displays newest commit first");
    var inheritedApproval = BrandChangeHistory.Append(januaryLogged, BrandMonthRules.Confirm(januaryLogged, "1405/02", [BrandMonthRules.DraftFor(januaryLogged, "1405/02").Rates.Single(r => r.BrandName == fikores.Name)]), logTime.AddMinutes(3));
    var approvalLog = BrandChangeHistory.ForMonth(inheritedApproval, "1405/02").Single();
    Test.True(approvalLog.Details.Contains("60٪") && approvalLog.Details.Contains("1405/01"), "next-month approval records inherited source and markup");
    var editedBase = BrandMonthRules.Confirm(inheritedApproval, "1405/02", [BrandMonthRules.RequireConfirmed(inheritedApproval, fikores.Name, "1405/02") with { Markup = .85m }]);
    var editedBaseLog = BrandChangeHistory.Append(inheritedApproval, editedBase, logTime.AddMinutes(4));
    Test.True(editedBaseLog.BrandChanges.Last().Details.Contains("60٪") && editedBaseLog.BrandChanges.Last().Details.Contains("85٪"), "base markup audit retains before and after values");
    var legacyHistory = januaryLogged with { BrandMonths = januaryLogged.BrandMonths.Select(m => m.MonthKey == "1405/01" ? m with { Rates = m.Rates.Select(r => r with { IsConfirmed = null }).ToList() } : m).ToList() };
    var legacyHistoryNormalization = legacyHistory with { BrandMonths = legacyHistory.BrandMonths.Select(m => m with { Rates = m.Rates.Select(r => r with { IsConfirmed = r.IsConfirmed ?? m.IsConfirmed }).ToList() }).ToList() };
    Test.Equal(januaryLogged.BrandChanges.Count, BrandChangeHistory.Append(legacyHistory, legacyHistoryNormalization).BrandChanges.Count, "confirmation normalization is not logged as a user edit");
    var olderHistoryDefaults = System.Text.Json.JsonSerializer.Deserialize<Ledger>("{\"Brands\":[]}", Rules.Json)!;
    Test.Equal(0, olderHistoryDefaults.BrandChanges.Count, "old ledger defaults to empty history");
    var unknown2 = new CatalogItem { Code = "8880012", Name = "Unknown 2" };
    var unknowns = ledger with { Items = [.. ledger.Items, unknown1, unknown2], Sales = [sale with { Id = "unknown-pending", Code = unknown1.Code, RateMonthKey = "", CashShare = 0, CreditShare = 0 }] };
    var assignedUnknowns = BrandCatalogRules.Upsert(unknowns, extraBrand);
    Test.True(assignedUnknowns.Items.Where(i => i.Code.StartsWith("888", StringComparison.Ordinal)).All(i => i.Brand == extraBrand.Name), "brand creation assigns all unknown codes with matching prefix");
    Test.Equal("", assignedUnknowns.Sales.Single().RateMonthKey, "prefix assignment alone does not calculate unconfirmed sales");
    assignedUnknowns = BrandMonthRules.Confirm(assignedUnknowns, "1405/06", [BrandMonthRules.FromBrand(extraBrand)]);
    Test.True(LedgerCalculator.Calculate(assignedUnknowns).Sales.ContainsKey("unknown-pending"), "unknown sales calculate after specific brand approval");
    Test.Throws(() => BrandCatalogRules.Upsert(timelineLedger, fikores with { Name = "Renamed used" }, fikores.Name), "used brand cannot rename");
    var originalJson = System.Text.Json.JsonSerializer.Serialize(timelineLedger, Rules.Json);
    var discardedPreview = MarkupRules.SetPeriod(timelineLedger, firstPeriod with { Markup = 2m }, true);
    Test.Equal(originalJson, System.Text.Json.JsonSerializer.Serialize(timelineLedger, Rules.Json), "staged preview does not mutate original ledger");
    Test.True(BrandChangePreview.Describe(timelineLedger, discardedPreview, "preview").Contains("تغییر بهای تمام‌شده"), "preview includes cost and profit impact");

    // Older confirmed months have no per-brand confirmation flag. A range-only edit
    // must not report every saved brand as a monthly percentage change.
    var legacyPreview = independent with
    {
        MarkupPeriods = [],
        BrandMonths = independent.BrandMonths.Select(m => m with { Rates = m.Rates.Select(r => r with { IsConfirmed = null }).ToList() }).ToList()
    };
    legacyPreview = legacyPreview with { Sales = legacyPreview.Sales.Select(s => BrandMonthRules.Apply(s,
        BrandMonthRules.RequireConfirmed(legacyPreview, legacyPreview.Items.Single(i => i.Code == s.Code).Brand, Rules.MonthOf(s.Date)),
        Rules.MonthOf(s.Date), legacyPreview)).ToList() };
    var previewPeriod = new MarkupPeriod { Id = "legacy-preview-range", BrandName = fikores.Name, StartDate = "14050615", EndDate = "14050619", Markup = .90m };
    var previewAdded = MarkupRules.SetPeriod(legacyPreview, previewPeriod, false);
    var rangePreviewText = BrandChangePreview.Describe(legacyPreview, previewAdded, "range");
    Test.True(!rangePreviewText.Contains(extraBrand.Name) && !rangePreviewText.Contains("مارک‌آپ پایه:"), "legacy range preview excludes unchanged brand percentages");
    Test.True(rangePreviewText.Contains(fikores.Name) && rangePreviewText.Contains("14050615 تا 14050619") && rangePreviewText.Contains("1 فروش دارای تغییر"), "legacy range preview shows selected range and affected sales");
    Test.Equal(legacyPreview.Sales.Single(s => s.Id == independentSale.Id), previewAdded.Sales.Single(s => s.Id == independentSale.Id), "preview fix leaves unrelated sale snapshot untouched");
    var previewEdited = MarkupRules.SetPeriod(previewAdded, previewPeriod with { Markup = 1.20m }, true);
    var editedPreviewText = BrandChangePreview.Describe(previewAdded, previewEdited, "range");
    Test.True(!editedPreviewText.Contains(extraBrand.Name) && !editedPreviewText.Contains("مارک‌آپ پایه:") && editedPreviewText.Contains("بازه قبلی:"), "legacy range correction shows only range difference");
    var previewRemoved = MarkupRules.RemovePeriod(previewAdded, previewPeriod.Id);
    var removedPreviewText = BrandChangePreview.Describe(previewAdded, previewRemoved, "range");
    Test.True(!removedPreviewText.Contains(extraBrand.Name) && !removedPreviewText.Contains("مارک‌آپ پایه:") && removedPreviewText.Contains("حذف بازه"), "legacy range deletion excludes unrelated brands");
    var normalizedPreview = legacyPreview with { BrandMonths = legacyPreview.BrandMonths.Select(m => m with { Rates = m.Rates.Select(r => r with { IsConfirmed = true }).ToList() }).ToList() };
    Test.True(!BrandChangePreview.Describe(legacyPreview, normalizedPreview, "normalization").Contains("مارک‌آپ پایه:"), "legacy confirmation normalization is not a user-facing change");
    var genuineRateChange = BrandMonthRules.Confirm(legacyPreview, "1405/06", [BrandMonthRules.RequireConfirmed(legacyPreview, extraBrand.Name, "1405/06") with { Markup = 1.10m }]);
    var genuineRateText = BrandChangePreview.Describe(legacyPreview, genuineRateChange, "monthly");
    Test.True(genuineRateText.Contains(extraBrand.Name) && genuineRateText.Contains("مارک‌آپ پایه:") && !genuineRateText.Contains(fikores.Name), "genuine monthly rate change still appears for just the edited brand");
    var legacyPending = legacyPreview with { BrandMonths = legacyPreview.BrandMonths.Select(m => m with { IsConfirmed = false }).ToList(), Sales = [] };
    var newlyConfirmed = BrandMonthRules.Confirm(legacyPending, "1405/06", [BrandMonthRules.FromBrand(extraBrand)]);
    var approvalPreviewText = BrandChangePreview.Describe(legacyPending, newlyConfirmed, "approval");
    Test.True(approvalPreviewText.Contains(extraBrand.Name) && !approvalPreviewText.Contains(fikores.Name), "new approval is still visible while other legacy pending brands are omitted");

    var inactiveLedger = BrandLifecycle.RemoveOrDeactivate(timelineLedger, fikores.Name);
    Test.True(!inactiveLedger.Brands.Single().IsActive, "used brand is deactivated instead of deleted");
    Test.Equal(timelineLedger.Sales.Single(s => s.Id == specialSale.Id), inactiveLedger.Sales.Single(s => s.Id == specialSale.Id), "deactivation preserves snapshots and exceptions");
    Test.Equal(LedgerCalculator.Calculate(timelineLedger).Sales[specialSale.Id], LedgerCalculator.Calculate(inactiveLedger).Sales[specialSale.Id], "inactive brand retains historical financial results");
    Test.True(BrandPrefixRules.DetectActive(inactiveLedger.Brands, item.Code) is null, "inactive brand unavailable for new code detection");
    Test.Throws(() => BrandLifecycle.RequireActive(inactiveLedger, fikores.Name), "inactive brand cannot be chosen for new sale");
    var unusedBrand = new Brand { Name = "unused", CodePrefixes = ["999"] };
    var unusedLedger = timelineLedger with { Brands = [.. timelineLedger.Brands, unusedBrand], Items = [.. timelineLedger.Items, new CatalogItem { Code = "99900001", Name = "کالای بدون فروش", Brand = unusedBrand.Name }] };
    var removedLedger = BrandLifecycle.RemoveOrDeactivate(unusedLedger, unusedBrand.Name);
    Test.True(removedLedger.Brands.All(b => b.Name != unusedBrand.Name), "unused brand removed");
    Test.Equal("", removedLedger.Items.Single(i => i.Code == "99900001").Brand, "unused items survive brand deletion without dangling brand");
    Test.True(BrandMonthRules.IsConfirmed(removedLedger, "1405/06"), "removing unused brand preserves existing confirmation");
    Test.True(BrandMonthRules.DraftFor(inactiveLedger, "1405/06").IsConfirmed, "deactivation keeps historical confirmation valid");
    var olderData = System.Text.Json.JsonSerializer.Deserialize<Ledger>("{\"Brands\":[{\"Name\":\"old\"}]}", Rules.Json)!;
    Test.True(olderData.Brands.Single().IsActive && olderData.MarkupPeriods.Count == 0, "legacy data defaults active and has no date overrides");

    var discounted = BrandMonthRules.Apply(new Sale { Id = "sale-discount", Date = "14050627", Code = item.Code, Customer = "مشتری", Quantity = 1, UnitPrice = 1_040_000m, Total = 1_040_000m, Deductions = 40_000m }, BrandMonthRules.RequireConfirmed(ledger, fikores.Name, "1405/06"), "1405/06");
    var discountedResult = LedgerCalculator.PreviewSale(discounted);
    Test.Equal(985_000m, discountedResult.Sales, "invoice deductions and cash discount are not double counted");

    // برگشتِ دارای تطبیق قطعی، همان فروش را با فرمول مصوب دوباره محاسبه می‌کند.
    var returnSale = sale with { Id = "return-sale", Quantity = 10, Total = 10_400_000m, Deductions = 1_040_000m };
    var returnDocument = new SaleReturn { Id = "return-1", Date = "14050628", Code = item.Code, Customer = "مشتری", Quantity = 5, UnitPrice = 1_040_000m, Total = 5_200_000m, Deductions = 200_000m };
    var returnedLedger = ledger with { Sales = [returnSale], Returns = [returnDocument] };
    var returnedCalculation = LedgerCalculator.Calculate(returnedLedger);
    var returnedSettlement = returnedCalculation.Sales[returnSale.Id];
    Test.Equal(5m, returnedSettlement.ReturnedQuantity, "matched return quantity");
    // (۵۲۰٬۰۰۰ + (۵٬۲۰۰٬۰۰۰ - ۲۰۰٬۰۰۰)) - ۵٬۲۰۰٬۰۰۰ = ۳۲۰٬۰۰۰؛ به‌علاوه بخشِ برنگشتهٔ فروش (۴٬۶۸۰٬۰۰۰).
    Test.Equal(5_000_000m, returnedSettlement.InvoiceBase, "approved return formula recalculates sale");
    Test.Equal(Rules.EstimatedCost(returnSale) / 2, returnedSettlement.Cost, "return reduces estimated cost by returned quantity");

    // در تطبیق چندفروشی، تعداد دقیق و سپس نزدیک‌ترین تاریخ، اولویت دارند.
    Sale Candidate(string id, string date, decimal quantity) => new() { Id = id, Date = date, Code = "700001", Customer = "مشتری", Quantity = quantity, UnitPrice = 500m, Total = quantity * 500m };
    var candidateEarly = Candidate("candidate-early", "14050610", 5);
    var candidateExact = Candidate("candidate-exact", "14050620", 10);
    var candidateReturn = new SaleReturn { Id = "candidate-return", Date = "14050625", Code = "700001", Customer = "مشتری", Quantity = 10, UnitPrice = 500m, Total = 5_000m };
    var candidateMatch = SalesReturnMatcher.Match([candidateEarly, candidateExact], [candidateReturn]);
    Test.Equal("candidate-exact", candidateMatch.Allocations.Single().SaleId, "exact quantity has priority over older smaller sale");
    var tieOld = Candidate("tie-old", "14050615", 4);
    var tieNew = Candidate("tie-new", "14050622", 4);
    var tieReturn = candidateReturn with { Id = "tie-return", Quantity = 4 };
    var tieMatch = SalesReturnMatcher.Match([tieOld, tieNew], [tieReturn]);
    Test.Equal("tie-new", tieMatch.Allocations.Single().SaleId, "closest prior sale resolves exact-quantity tie");
    var tooLarge = candidateReturn with { Id = "too-large", Quantity = 16, Total = 8_000m };
    var pendingMatch = SalesReturnMatcher.Match([candidateEarly, candidateExact], [tooLarge]);
    Test.Equal(0, pendingMatch.Allocations.Count, "insufficient return receives no partial allocation");
    Test.Equal(1, pendingMatch.PendingReturns.Count, "insufficient return remains pending");

    // فقط قیمت واحد دقیقاً یک، آفر است؛ قیمت‌های پایین دیگر نباید حذف شوند.
    var importFile = System.IO.Path.Combine(root, "offer-rows.xls");
    File.WriteAllText(importFile, """
<?xml version="1.0"?><Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"><Worksheet ss:Name="Sheet1"><Table>
<Row><Cell><Data ss:Type="String">تاریخ</Data></Cell><Cell><Data ss:Type="String">نام حساب</Data></Cell><Cell><Data ss:Type="String">کد کالا</Data></Cell><Cell><Data ss:Type="String">نام کالا</Data></Cell><Cell><Data ss:Type="String">تعداد</Data></Cell><Cell><Data ss:Type="String">قیمت</Data></Cell><Cell><Data ss:Type="String">قیمت کل</Data></Cell></Row>
<Row><Cell><Data ss:Type="String">14050627</Data></Cell><Cell><Data ss:Type="String">مشتری</Data></Cell><Cell><Data ss:Type="String">900001</Data></Cell><Cell><Data ss:Type="String">آفر</Data></Cell><Cell><Data ss:Type="Number">1</Data></Cell><Cell><Data ss:Type="Number">1</Data></Cell><Cell><Data ss:Type="Number">1</Data></Cell></Row>
<Row><Cell><Data ss:Type="String">14050627</Data></Cell><Cell><Data ss:Type="String">مشتری</Data></Cell><Cell><Data ss:Type="String">900002</Data></Cell><Cell><Data ss:Type="String">فروش کم‌مبلغ</Data></Cell><Cell><Data ss:Type="Number">1</Data></Cell><Cell><Data ss:Type="Number">2</Data></Cell><Cell><Data ss:Type="Number">2</Data></Cell></Row>
</Table></Worksheet></Workbook>
""");
    var offerReview = TransactionImport.Review(importFile);
    Test.Equal(0, offerReview.Ignored, "ordinary offers are retained");
    Test.Equal(1, offerReview.Offers.Count, "unit price one is imported separately as offer");
    Test.Equal(1, offerReview.Rows.Count, "unit price two remains importable");

    Test.True(OfferRules.IsPromotional("ادکلن تستر"), "testers are excluded");
    Test.True(OfferRules.IsPromotional("استند ایستاده"), "promotional stands are excluded");
    Test.True(!OfferRules.IsPromotional("کرم بلوبری"), "ordinary product is not promotional");
    var offerSale = sale with { Id = "offer-sale", InvoiceNumber = "11", SourceRow = 53, Date = "14050620", Customer = "offer customer", Quantity = 4, UnitPrice = 6_150_000m, Total = 24_600_000m, Deductions = 0, CashShare = 1, CreditShare = 0, CashDiscount = 0 };
    var offerEntry = new OfferEntry { Id = "offer-1", InvoiceNumber = "11", SourceRow = 54, Date = offerSale.Date, Customer = offerSale.Customer, Code = offerSale.Code, Name = "کالای آزمایشی", Quantity = 1 };
    var offerLedger = ledger with { Sales = [offerSale], Returns = [], Offers = [offerEntry] };
    var offerCalculation = LedgerCalculator.Calculate(offerLedger);
    Test.Equal(18_450_000m, offerCalculation.Sales[offerSale.Id].InvoiceBase, "offer is deducted once from invoice before split");
    Test.Throws(() => OfferRules.Calculate(offerLedger with { Offers = [offerEntry with { ManualSaleId = offerSale.Id, Customer = "other customer", ManualReason = "test" }] }), "manual offer cannot link another customer sale");
    var partialReturn = new SaleReturn { Id = "partial-offer-sale-return", InvoiceNumber = "12", Date = "14050622", Customer = offerSale.Customer, Code = offerSale.Code, Quantity = 2, UnitPrice = offerSale.UnitPrice, Total = 12_300_000m };
    var partialOfferLedger = offerLedger with { Returns = [partialReturn] };
    var partialOfferCalculation = LedgerCalculator.Calculate(partialOfferLedger);
    Test.Equal(6_150_000m, partialOfferCalculation.Sales[offerSale.Id].InvoiceBase, "partial return keeps full offer deduction without double counting");
    Test.Equal(offerCalculation.Sales[offerSale.Id].Cost / 2, partialOfferCalculation.Sales[offerSale.Id].Cost, "partial return cost follows remaining paid quantity");
    var returnOffer = offerEntry with { Id = "offer-return", IsReturn = true, Date = "14050622", InvoiceNumber = "12", SourceRow = 2 };
    var withOfferReturn = partialOfferLedger with { Offers = [offerEntry, returnOffer] };
    Test.Equal(12_300_000m, LedgerCalculator.Calculate(withOfferReturn).Sales[offerSale.Id].InvoiceBase, "returned offer releases the original sale price");
    var completeReturn = partialReturn with { Quantity = 4, Total = offerSale.Total };
    Test.Equal(0m, LedgerCalculator.Calculate(withOfferReturn with { Returns = [completeReturn] }).Sales[offerSale.Id].InvoiceBase, "full sale and offer return settle to zero");
    var tooManyOffers = returnOffer with { Id = "too-many-offers", Quantity = 2, ManualUnitPrice = offerSale.UnitPrice, ManualReason = "test" };
    Test.True(!OfferRules.Calculate(offerLedger with { Offers = [offerEntry, tooManyOffers] }).Rows.Single(x => x.Entry.IsReturn).UnitPrice.HasValue, "manual price cannot bypass insufficient original offer quantity");
    var orphan = returnOffer with { ManualUnitPrice = offerSale.UnitPrice, ManualReason = "فروش سال قبل موجود نیست" };
    var orphanLedger = offerLedger with { Sales = [], Offers = [orphan] };
    Test.Equal(offerSale.UnitPrice, LedgerCalculator.SummarizeMonth(orphanLedger, new Month { Key = "1405/06" }).Profit, "orphan manual valuation is credited once in return month");
    var laterMatched = OfferRules.Calculate(offerLedger with { Offers = [offerEntry, orphan] });
    Test.Equal(0, laterMatched.UnlinkedReturnCredits.Count, "later automatic match supersedes manual valuation");
    Test.Equal(0m, laterMatched.SaleAdjustments[offerSale.Id], "original and returned offers net to zero after late match");
    var reordered = offerSale with { Id = "reordered-sale", SourceRow = 99 };
    Test.Equal("", OfferRules.Calculate(offerLedger with { Sales = [offerSale, reordered with { UnitPrice = offerSale.UnitPrice + 1 }] }).Rows.Single().SaleId, "ambiguous different-price offers should not pick a sale");

    var importRow = offerReview.Rows.Single() with { InvoiceNumber = "99", Identity = "invoice:1", Fingerprint = "content-A" };
    var existingRow = new ImportRecord("saved", importRow.ImportKey, importRow.Identity, importRow.Fingerprint);
    Test.Equal(0, ImportMerge.Plan([importRow], [existingRow]).Added.Count, "same file is not imported again");
    var overlappingRow = importRow with { ImportKey = "another-file:10", Row = 10 };
    Test.Equal(1, ImportMerge.Plan([overlappingRow], [existingRow]).Matched.Count, "overlapping different file is recognized");
    var correctedRow = overlappingRow with { Fingerprint = "content-B", Quantity = 2 };
    Test.Equal(1, ImportMerge.Plan([correctedRow], [existingRow]).Replacements.Count, "corrected quantity is flagged for replacement");
    var duplicateActualSale = importRow with { Identity = "invoice:2", ImportKey = "file:11" };
    Test.Equal(1, ImportMerge.Plan([importRow, duplicateActualSale], [existingRow]).Added.Count, "two real identical sales retain their multiplicity");
    Test.Equal(2, ImportMerge.Plan([importRow, duplicateActualSale], [existingRow, existingRow with { Id = "saved-2", Identity = duplicateActualSale.Identity }]).Matched.Count, "reimport preserves two identical real sales without duplication");
    var reorderedRow = overlappingRow with { Identity = "invoice:2" };
    Test.Equal(1, ImportMerge.Plan([reorderedRow], [existingRow]).Matched.Count, "row order changes do not duplicate identical content in a bill");

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
    store.SaveLedger(editedBaseLog);
    Test.Equal(editedBaseLog.BrandChanges.Count, store.LoadLedger().BrandChanges.Count, "monthly change logs survive SQLite roundtrip");
    Test.Equal(editedBaseLog.BrandChanges.Last(), store.LoadLedger().BrandChanges.Last(), "audit commit timestamp and before/after details survive persistence");
    store.Save(new Month { Key = "1405/06" });
    store.SaveLedger(ledger);
    store.SaveLedger(offerLedger);
    Test.Equal(1, store.LoadLedger().Offers.Count, "offers survive database roundtrip");
    Test.Equal("11", store.LoadLedger().Sales.Single().InvoiceNumber, "invoice metadata survives database roundtrip");
    store.SaveLedger(inactiveLedger);
    Test.Equal(2, store.LoadLedger().MarkupPeriods.Count, "dated markup survives database roundtrip");
    Test.Equal(.80m, store.LoadLedger().Sales.Single(s => s.Id == specialSale.Id).MarkupOverride!.Value, "row markup exception survives database roundtrip");
    Test.True(!store.LoadLedger().Brands.Single().IsActive, "brand active status survives database roundtrip");
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
