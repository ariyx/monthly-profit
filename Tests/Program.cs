using Profit.Core;
using System.Text.Json;
using System.IO.Compression;

var root = Path.Combine(Path.GetTempPath(), "profit-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
int checks = 0;
void Equal<T>(T actual, T expected, string name) { if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"{name}: got {actual}, expected {expected}"); checks++; }
void Throws(Action f, string name) { bool threw = false; try { f(); } catch { threw = true; } if (!threw) throw new Exception("Expected rejection: " + name); checks++; }
try
{
    var sample = Rules.Sample("1405/06"); var t = Rules.Summarize(sample);
    Equal(t.Cost, 18070000m, "sample cost"); Equal(t.Sales, 26634400m, "sample sales"); Equal(t.Profit, 8564400m, "sample profit"); Equal(t.Net, -43435600m, "monthly loss");
    Equal(t.Cash, 7706400m, "cash"); Equal(t.Credit, 18928000m, "credit");
    var p = sample.Products[4]; var r = Rules.Calculate(p); var doubled = Rules.Calculate(p with { Quantity = 4 });
    Equal(doubled.Profit, r.Profit * 2, "linear quantity"); Equal(doubled.UnitSale, r.UnitSale, "unit price independent of quantity"); Equal(doubled.Margin, r.Margin, "margin independent of quantity");
    var zero = Rules.Calculate(p with { CashShare = 1, CreditShare = 0, CashDiscount = 1 }); Equal(zero.Sales, 0m, "zero sales"); Equal(zero.Margin, null, "undefined margin"); Equal(zero.Profit, -r.Cost, "loss retained");
    Equal(Rules.Calculate(p with { Quantity = .5m }).Sales, r.Sales / 4, "fractional quantity");
    Throws(() => Rules.Calculate(p with { CreditShare = .6m }), "shares"); Throws(() => Rules.Calculate(p with { Discount = .99m }), "offer and discount"); Throws(() => Rules.Calculate(p with { Price = -1 }), "negative price");
    Equal(Rules.Number("۵۲٬۰۰۰٬۰۰۰"), 52000000m, "Persian input"); Equal(Rules.Number("٥٫٥"), 5.5m, "Arabic digits");
    var categorized = sample with { FixedExpenses = [new FixedExpense { Title = "اجاره", Amount = 20_000_000m }, new FixedExpense { Title = "حقوق", Amount = 32_000_000m }] }; Rules.Validate(categorized); Equal(Rules.Summarize(categorized).FixedCost, 52_000_000m, "fixed expense total");
    var changed = p with { Id = "different", Name = "second", Price = 1000000m, Markup = .2m }; var mixed = new Month { Key = "1405/07", Products = [p, changed] }; var mt = Rules.Summarize(mixed);
    Equal(mt.Brands, 1, "multiple products per brand"); Equal(mt.Margin, mt.Profit / mt.Sales, "weighted margin");
    var db = new Store(Path.Combine(root, "data.sqlite")); db.Save(sample); var loaded = new Store(db.Path).Load(sample.Key); Equal(loaded.Products.Count, 6, "persistent restart");
    var copy = loaded.CopyTo("1405/07"); db.Save(copy); copy.Products[0] = copy.Products[0] with { Price = 77 }; db.Save(copy); Equal(db.Load("1405/06").Products[0].Price, 1000000m, "month isolation");
    var stale = db.Load(copy.Key); var fresh = db.Load(copy.Key); fresh.FixedCost = 9; db.Save(fresh); Throws(() => db.Save(stale), "optimistic revision");
    var closed = db.SetClosed(db.Load(copy.Key), true); Throws(() => db.Save(closed with { FixedCost = 10 }), "closed month protection"); db.SetClosed(closed, false);
    var renameSafety = db.CreateSafetyBackup("before-rename"); Equal(File.Exists(renameSafety), true, "rename safety backup");
    var renamed = db.Rename(db.Load("1405/07"), "1405/08"); Equal(renamed.Key, "1405/08", "month rename"); Throws(() => db.Load("1405/07"), "old month absent after rename");
    Throws(() => db.Rename(db.Load("1405/08"), "1405/06"), "rename conflict");
    var deleteSafety = db.CreateSafetyBackup("before-delete"); db.Delete(db.Load("1405/08")); Equal(File.Exists(deleteSafety), true, "delete safety backup"); Throws(() => db.Load("1405/08"), "month delete");
    var backup = Path.Combine(root, "backup.sqlite"); db.Backup(backup); var m = db.Load("1405/06"); m.FixedCost = 0; db.Save(m); db.Restore(backup); Equal(db.Load("1405/06").FixedCost, 52000000m, "backup restore");
    var invalid = Path.Combine(root, "invalid.sqlite"); File.WriteAllText(invalid, "not a database"); Throws(() => db.Restore(invalid), "invalid backup"); Equal(db.Load("1405/06").Products.Count, 6, "failed restore leaves data intact");
    var export = Path.Combine(root, "roundtrip.xlsx"); ExcelTransfer.Export(sample, export); var rows = ExcelTransfer.Import(export); Equal(rows.Count, 6, "Excel roundtrip count"); Equal(Rules.Summarize(new Month { Key = sample.Key, Products = rows }).Profit, t.Profit, "Excel precision roundtrip");
    var range = Path.Combine(root, "range.xlsx"); ExcelTransfer.ExportRange([sample, mixed], range); using (var archive = ZipFile.OpenRead(range)) Equal(archive.GetEntry("xl/worksheets/sheet1.xml") is not null, true, "range Excel output");
    var duplicate = sample with { Products = [p, p with { Id = Guid.NewGuid().ToString() }] }; Throws(() => db.Save(duplicate), "duplicate brand-product pair");
    var brand = new Brand { Name = "Fikores", Markup = .20m, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m };
    var migratedBrands = BrandPrefixRules.EnsureDefaults(new Ledger { Brands = [brand] }).Brands;
    Equal(BrandPrefixRules.Detect(migratedBrands, "1061342")?.Name, "Fikores", "brand prefix maps all 106 codes");
    Equal(BrandPrefixRules.Detect(migratedBrands, "1120005")?.Name, "آذر بیوتی", "global default brand prefix");
    Equal(BrandPrefixRules.Display(BrandPrefixRules.Parse("115، 145")), "115، 145", "brand prefix editor parsing");
    Throws(() => BrandPrefixRules.ValidateUnique([brand with { CodePrefixes = ["106"] }, new Brand { Name = "Other", CodePrefixes = ["106"] }]), "duplicate prefix across brands");
    var item = new CatalogItem { Code = "1060395", Name = "کالای آزمایشی", Brand = brand.Name };
    var ledger = new Ledger
    {
        Brands = [brand], Items = [item],
        Purchases = [
            new Purchase { Id = "p1", Date = "14050301", Code = item.Code, Quantity = 5, UnitPrice = 100, Total = 500 },
            new Purchase { Id = "p2", Date = "14050401", Code = item.Code, Quantity = 5, UnitPrice = 200, Total = 1000 }
        ],
        Sales = [new Sale { Id = "s1", Date = "14050402", Code = item.Code, Quantity = 6, UnitPrice = 200, Total = 1200, Deductions = 100, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m }]
    };
    var calculation = LedgerCalculator.Calculate(ledger); var settlement = calculation.Sales["s1"];
    Equal(settlement.Cost, 700m, "FIFO cost uses oldest purchase"); Equal(settlement.Cash, 313.5m, "cash discount only reduces cash share"); Equal(settlement.Credit, 770m, "credit share retained"); Equal(settlement.Profit, 383.5m, "ledger profit"); Equal(calculation.Stock.Single().Quantity, 4m, "rolling inventory");
    var monthTotal = LedgerCalculator.SummarizeMonth(ledger, new Month { Key = "1405/04" }); Equal(monthTotal.InvoiceSales, 1100m, "invoice sales after deductions"); Equal(monthTotal.CashDiscountAmount, 16.5m, "cash discount is explicit");
    Equal(LedgerCalculator.CalculateAtEndOfMonth(ledger, "1405/03").Stock.Single().Quantity, 5m, "inventory snapshot excludes future purchases and sales");
    var shortage = LedgerCalculator.PreviewSale(ledger, new Sale { Id = "s2", Date = "14050403", Code = item.Code, Quantity = 6, UnitPrice = 200, Total = 1200, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m });
    Equal(shortage.Shortage, 2m, "shortage is surfaced for manual reconciliation");
    var reconciliation = ReconciliationPlanner.Find(ledger, [new Sale { Id = "s3", Date = "14050403", Code = item.Code, Quantity = 6, UnitPrice = 200, Total = 1200, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m }]);
    Equal(reconciliation.Count, 1, "reconciliation groups shortages by code"); Equal(reconciliation[0].Quantity, 2m, "reconciliation quantity");
    var adjustments = ReconciliationPlanner.CreateAdjustments(reconciliation, new Dictionary<string, decimal> { [item.Code] = 200m });
    var partialAdjustment = ReconciliationPlanner.CreateAdjustments([reconciliation[0] with { Quantity = 1m }], new Dictionary<string, decimal> { [item.Code] = 200m });
    Equal(partialAdjustment.Single().Quantity, 1m, "partial reconciliation adjustment is retained");
    var reconciledLedger = ledger with { Purchases = [.. ledger.Purchases, .. adjustments], Sales = [.. ledger.Sales, new Sale { Id = "s3", Date = "14050403", Code = item.Code, Quantity = 6, UnitPrice = 200, Total = 1200, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m }] };
    Equal(LedgerCalculator.Calculate(reconciledLedger).Sales["s3"].Shortage, 0m, "approved adjustment resolves shortage");
    var withOpening = new Ledger { Brands = [brand], Items = [item], OpeningLots = [new OpeningLot { Id = "o1", Code = item.Code, Quantity = 3, UnitCost = 80, SourceDate = "14041229" }], Sales = [new Sale { Id = "os1", Date = "14050102", Code = item.Code, Quantity = 2, UnitPrice = 200, Total = 400, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m }] };
    Equal(LedgerCalculator.Calculate(withOpening).Sales["os1"].Cost, 160m, "opening inventory is consumed before 1405 purchases");
    db.SaveLedger(ledger); Equal(db.LoadLedger().Purchases.Count, 2, "ledger persists"); Equal(db.Keys().Contains("1405/03"), true, "purchase month auto-created"); Equal(db.Keys().Contains("1405/04"), true, "sale month auto-created");
    var pendingLedger = ledger with { PendingBrandTransactions = [new PendingBrandTransaction { Kind = TransactionKind.Sale, Transaction = new ImportedTransaction(2, "pending:2", "14050601", "9990001", "کالای ناشناخته", "مشتری", 2, 100, 200, 0) }] };
    db.SaveLedger(pendingLedger); Equal(db.LoadLedger().PendingBrandTransactions.Single().Transaction.Code, "9990001", "unknown brand queue persists");
    var ledgerExport = Path.Combine(root, "ledger.xlsx"); ExcelTransfer.ExportLedgerMonth(ledger, new Month { Key = "1405/04" }, ledgerExport); using (var archive = ZipFile.OpenRead(ledgerExport)) Equal(archive.GetEntry("xl/worksheets/sheet1.xml") is not null, true, "ledger Excel output");
    var compact = Path.Combine(root, "compact.xls"); File.WriteAllText(compact, """
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet" xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet"><Worksheet ss:Name="Sheet1"><Table><Row><Cell><Data ss:Type="String">تاریخ</Data></Cell><Cell><Data ss:Type="String">نام حساب</Data></Cell><Cell><Data ss:Type="String">کد کالا</Data></Cell><Cell><Data ss:Type="String">نام کالا</Data></Cell><Cell><Data ss:Type="String">تعداد</Data></Cell><Cell><Data ss:Type="String">قیمت</Data></Cell><Cell><Data ss:Type="String">کسورات</Data></Cell></Row><Row><Cell><Data ss:Type="String">14050601</Data></Cell><Cell><Data ss:Type="String">مشتری</Data></Cell><Cell><Data ss:Type="String">1060395</Data></Cell><Cell><Data ss:Type="String">کالا</Data></Cell><Cell><Data ss:Type="Number">2</Data></Cell><Cell><Data ss:Type="Number">1000</Data></Cell><Cell><Data ss:Type="Number">20</Data></Cell></Row><Row><Cell><Data ss:Type="String">14050601</Data></Cell><Cell><Data ss:Type="String">مشتری</Data></Cell><Cell><Data ss:Type="String">1060396</Data></Cell><Cell><Data ss:Type="String">آفر</Data></Cell><Cell><Data ss:Type="Number">2</Data></Cell><Cell><Data ss:Type="Number">1</Data></Cell><Cell><Data ss:Type="Number">0</Data></Cell></Row></Table></Worksheet></Workbook>
""");
    var compactReview = TransactionImport.Review(compact); Equal(compactReview.Rows.Count, 1, "compact Excel accepts quantity and unit price"); Equal(compactReview.Rows[0].Total, 2000m, "compact Excel derives total"); Equal(compactReview.Ignored, 1, "compact Excel ignores offers");
    if (args.Length > 0) { var original = ExcelTransfer.Import(args[0]); Equal(original.Count, 6, "original Excel import"); Equal(Rules.Summarize(new Month { Key = "1405/06", Products = original }).Profit, 8564400m, "original input reconciliation"); }
    Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", checks, sample = t }, Rules.Json));
}
finally { Directory.Delete(root, true); }
