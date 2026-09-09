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
    if (args.Length > 0) { var original = ExcelTransfer.Import(args[0]); Equal(original.Count, 6, "original Excel import"); Equal(Rules.Summarize(new Month { Key = "1405/06", Products = original }).Profit, 8564400m, "original input reconciliation"); }
    Console.WriteLine(JsonSerializer.Serialize(new { status = "passed", checks, sample = t }, Rules.Json));
}
finally { Directory.Delete(root, true); }
