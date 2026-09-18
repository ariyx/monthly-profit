using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Profit.Core;

namespace Profit.Desktop;

public partial class MainWindow : Window
{
    readonly Store store;
    Ledger ledger = new();
    Month current = new();
    bool loading;
    Preferences preferences = new();
    List<Month> reportMonths = [];
    List<ReconciliationCandidate> reconciliationCandidates = [];
    List<PendingBrandGridRow> pendingBrandRows = [];
    List<PurchaseGridRow> purchaseRows = [];
    List<SaleGridRow> saleRows = [];
    string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonthlyProfit", "Data");

    public MainWindow()
    {
        InitializeComponent();
        store = new Store(Path.Combine(DataDirectory, "monthly-profit.sqlite"));
        MoneyInput.Attach(Fixed);
        preferences = store.LoadPreferences();
        loading = true; AutoBackup.IsChecked = preferences.AutoBackupOnExit; loading = false;
        AboutVersion.Text = "نسخه برنامه ۰٫۴٫۱۵ · گردش تاریخ‌دار کالا · داده‌ها فقط محلی هستند.";
        Reload(); Closing += OnClosing;
    }
    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ResolveFixedEdit()) { e.Cancel = true; return; }
        if (preferences.AutoBackupOnExit) Guard(() => { var file = store.CreateAutomaticBackup(preferences.AutoBackupKeep); preferences = preferences with { LastAutoBackupUtc = DateTime.UtcNow }; store.SavePreferences(preferences); Status.Text = "پشتیبان خودکار ذخیره شد: " + file; });
    }
    void Guard(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "عملیات انجام نشد", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    static string CurrentMonth()
    {
        var pc = new PersianCalendar(); var d = DateTime.Today; return $"{pc.GetYear(d):0000}/{pc.GetMonth(d):00}";
    }
    void Reload(string? key = null)
    {
        ledger = BrandPrefixRules.EnsureDefaults(store.LoadLedger()); var keys = store.Keys();
        if (keys.Count == 0) { store.EnsureMonth(CurrentMonth()); keys = store.Keys(); }
        loading = true; var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string; var selected = Months.SelectedItem as string;
        Months.ItemsSource = keys; Months.SelectedItem = key != null && keys.Contains(key) ? key : selected != null && keys.Contains(selected) ? selected : keys.First();
        HistoryFrom.ItemsSource = keys; HistoryTo.ItemsSource = keys; HistoryFrom.SelectedItem = from != null && keys.Contains(from) ? from : null; HistoryTo.SelectedItem = to != null && keys.Contains(to) ? to : null;
        loading = false; current = store.Load((string)Months.SelectedItem); Draw();
    }
    bool CanEdit(string key, bool show = true)
    {
        if (!store.Keys().Contains(key) || !store.Load(key).IsClosed) return true;
        if (show) MessageBox.Show(this, $"ماه {key} بسته است. ابتدا آن را باز کنید.", "ماه بسته", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
    bool CanEdit() => CanEdit(current.Key);
    bool ResolveFixedEdit()
    {
        if (current.Revision == 0 || Fixed.Text == Rules.Money(current.FixedCost)) return true;
        if (!CanEdit()) { Fixed.Text = Rules.Money(current.FixedCost); return true; }
        var choice = MessageBox.Show(this, "هزینه ثابت ویرایش شده است. ذخیره شود؟", "تغییر ذخیره‌نشده", MessageBoxButton.YesNoCancel);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.No) { Fixed.Text = Rules.Money(current.FixedCost); return true; }
        SaveMonth(current with { FixedCost = Rules.Number(Fixed.Text), FixedExpenses = [] }, "هزینه ثابت ویرایش شد"); return true;
    }
    void MonthChanged(object s, SelectionChangedEventArgs e)
    {
        if (loading || Months.SelectedItem is not string key || key == current.Key) return;
        if (!ResolveFixedEdit()) { loading = true; Months.SelectedItem = current.Key; loading = false; return; }
        Guard(() => { current = store.Load(key); Draw(); });
    }
    void Draw()
    {
        var t = LedgerCalculator.SummarizeMonth(ledger, current); Sales.Text = Rules.ReportMoney(t.Sales); Profit.Text = Rules.ReportMoney(t.Profit); Net.Text = Rules.ReportMoney(t.Net);
        var color = t.Net < 0 ? Brushes.Firebrick : t.Net > 0 ? Brushes.SeaGreen : Brushes.SlateGray; Net.Foreground = color; NetLabel.Text = Outcome(t.Net); NetLabel.Foreground = color;
        ClosedBadge.Text = current.IsClosed ? "ماه بسته" : "ماه باز"; ClosedBadge.Foreground = current.IsClosed ? Brushes.Firebrick : Brushes.SeaGreen;
        MarginLabel.Text = "حاشیه سود کل: " + Rules.Percent(t.Margin); Fixed.Text = Rules.Money(current.FixedCost);
        Breakdown.Text = $"فروش پس از کسورات فاکتور: {Rules.ReportMoney(t.InvoiceSales)} ریال\nتخفیف نقدی: {Rules.ReportMoney(t.CashDiscountAmount)} ریال\nدریافتی نقدی: {Rules.ReportMoney(t.Cash)} ریال\nفروش چکی: {Rules.ReportMoney(t.Credit)} ریال\nبهای تمام‌شده فروش‌رفته: {Rules.ReportMoney(t.Cost)} ریال\nبرند: {t.Brands}   |   کالا: {t.Products}" + (t.Shortage > 0 ? $"\nمغایرت تأییدنشده: {Rules.Money(t.Shortage)} عدد؛ سود قطعی نیست." : "");
        DrawTransactions(); DrawInventory(); DrawReconciliations(); DrawPendingBrands(); DrawBrandSettings(); DrawBrands(); DrawHistory(); DrawBackupStatus(); Status.Text = $"ماه {current.Key} · {ledger.Purchases.Count} خرید و {ledger.Sales.Count} فروش ثبت شده";
    }
    static string Outcome(decimal net) => net < 0 ? "زیان" : net > 0 ? "سود" : "سر‌به‌سر";
    void SaveMonth(Month next, string action)
    {
        if (!CanEdit(next.Key)) return; next = next with { Audit = [.. next.Audit.TakeLast(499), new AuditEntry { Action = action }] }; store.Save(next); current = store.Load(next.Key); Draw();
    }
    void SaveLedger(Ledger next, string action, string? selectMonth = null)
    {
        // فقط ماهی که اکنون در آن تراکنش ثبت شده کنترل می‌شود؛ ماه‌های قدیمیِ بسته نباید مانع ذخیرهٔ اطلاعات جدید شوند.
        if (selectMonth != null && !CanEdit(selectMonth)) throw new InvalidOperationException($"ماه {selectMonth} بسته است؛ ورود یا ثبت تراکنش در آن ممکن نیست.");
        store.SaveLedger(next); Reload(selectMonth ?? current.Key); Status.Text = action;
    }
    void OpenMonthMenu(object s, RoutedEventArgs e) { if (s is Button b && b.ContextMenu != null) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; } }
    void ToggleMonthClosed(object s, RoutedEventArgs e)
    {
        var target = !current.IsClosed; var text = target ? "ماه پس از بسته‌شدن قابل ثبت یا ورود نیست. ادامه می‌دهید؟" : "ماه دوباره قابل ثبت می‌شود. ادامه می‌دهید؟";
        if (MessageBox.Show(this, text, target ? "بستن ماه" : "باز کردن ماه", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Guard(() => { current = store.SetClosed(current, target); Draw(); });
    }
    void SaveFixed(object s, RoutedEventArgs e) => Guard(() => SaveMonth(current with { FixedCost = Rules.Number(Fixed.Text), FixedExpenses = [] }, "هزینه ثابت ویرایش شد"));
    void FixedExpenses(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return; var d = new FixedExpensesDialog(current) { Owner = this }; if (d.ShowDialog() != true || d.Value == null) return;
        Guard(() => SaveMonth(current with { FixedExpenses = d.Value, FixedCost = d.Value.Sum(x => x.Amount) }, "ریز هزینه‌های ثابت ویرایش شد"));
    }
    void AddBrand(object s, RoutedEventArgs e)
    {
        var d = new BrandEditorDialog(null) { Owner = this }; if (d.ShowDialog() != true || d.Value == null) return;
        Guard(() =>
        {
            if (ledger.Brands.Any(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(d.Value.Name), StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("این برند قبلاً ثبت شده است.");
            var brands = ledger.Brands.Append(d.Value).ToList(); BrandPrefixRules.ValidateUnique(brands);
            SaveLedger(ledger with { Brands = brands }, $"برند «{d.Value.Name}» افزوده شد");
        });
    }
    void BrandSettingsGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        Guard(() =>
        {
            if (BrandSettingsGrid.SelectedItem is not BrandSettingsRow row) return;
            var d = new BrandEditorDialog(row.Value) { Owner = this }; if (d.ShowDialog() != true || d.Value == null) return;
            var previous = row.Value; var nextBrand = d.Value;
            var used = ledger.Items.Any(x => Rules.Normalize(x.Brand).Equals(Rules.Normalize(previous.Name), StringComparison.OrdinalIgnoreCase));
            if (used && !Rules.Normalize(previous.Name).Equals(Rules.Normalize(nextBrand.Name), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("نام برندی که قبلاً روی کالا ثبت شده قابل تغییر نیست؛ سایر تنظیمات آن قابل ویرایش‌اند.");
            var brands = ledger.Brands.Select(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(previous.Name), StringComparison.OrdinalIgnoreCase) ? nextBrand : x).ToList();
            if (brands.Select(x => Rules.Normalize(x.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != brands.Count) throw new InvalidDataException("نام برند تکراری است.");
            BrandPrefixRules.ValidateUnique(brands);
            SaveLedger(ledger with { Brands = brands }, $"تنظیمات برند «{nextBrand.Name}» ویرایش شد");
        });
    }
    void AddPurchase(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return; var d = new PurchaseDialog(ledger) { Owner = this }; if (d.ShowDialog() != true || d.Value == null || d.Item == null) return;
        Guard(() => AddPurchase(d.Item, d.Value, "خرید دستی ثبت شد"));
    }
    void AddPurchase(CatalogItem item, Purchase purchase, string action)
    {
        var items = ledger.Items.ToList(); var index = items.FindIndex(x => x.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase));
        if (index < 0) items.Add(item); else if (!Rules.Normalize(items[index].Brand).Equals(Rules.Normalize(item.Brand), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("کد کالا قبلاً با برند دیگری ثبت شده است.");
        SaveLedger(ledger with { Items = items, Purchases = [.. ledger.Purchases, purchase] }, action, Rules.MonthOf(purchase.Date));
    }
    void AddSale(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return; var d = new SaleDialog(ledger) { Owner = this }; if (d.ShowDialog() != true || d.Value == null) return;
        Guard(() => AddSaleWithReconciliation(d.Value, "فروش دستی ثبت شد"));
    }
    void AddSaleWithReconciliation(Sale sale, string action)
    {
        // فروش بدون وقفه ثبت می‌شود؛ هر کسری بعداً و به‌صورت قابل جست‌وجو در تب مغایرت‌ها تعیین تکلیف خواهد شد.
        SaveLedger(ledger with { Sales = [.. ledger.Sales, sale] }, action + "؛ مغایرت احتمالی در تب مغایرت‌ها قابل بررسی است.", Rules.MonthOf(sale.Date));
    }
    void SalesGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        Guard(() =>
        {
            if (SalesGrid.SelectedItem is not SaleGridRow row) return;
            if (!CanEdit(Rules.MonthOf(row.Value.Date))) return;
            var dialog = new SaleEditorDialog(ledger, row.Value) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Value == null) return;
            if (!CanEdit(Rules.MonthOf(dialog.Value.Date))) return;
            var sales = ledger.Sales.ToList(); var index = sales.FindIndex(x => x.Id == row.Value.Id);
            if (index < 0) throw new InvalidOperationException("فروش انتخاب‌شده دیگر یافت نشد.");
            sales[index] = dialog.Value;
            SaveLedger(ledger with { Sales = sales }, $"فروش کد {dialog.Value.Code} ویرایش شد", Rules.MonthOf(dialog.Value.Date));
        });
    }
    void PurchaseSearchChanged(object s, TextChangedEventArgs e) => ApplyPurchaseFilter();
    void PurchaseFilterChanged(object s, SelectionChangedEventArgs e) => ApplyPurchaseFilter();
    void ClearPurchaseFilters(object s, RoutedEventArgs e)
    {
        PurchaseSearch.Text = ""; PurchaseBrandFilter.SelectedItem = "همه برندها"; ApplyPurchaseFilter();
    }
    void SaleSearchChanged(object s, TextChangedEventArgs e) => ApplySaleFilter();
    void SaleFilterChanged(object s, SelectionChangedEventArgs e) => ApplySaleFilter();
    void ClearSaleFilters(object s, RoutedEventArgs e)
    {
        SaleSearch.Text = ""; SaleBrandFilter.SelectedItem = "همه برندها"; ApplySaleFilter();
    }
    static void SetBrandFilter(ComboBox filter, IEnumerable<string> brands)
    {
        var selected = filter.SelectedItem as string;
        var options = new[] { "همه برندها" }.Concat(brands.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).ToList();
        filter.ItemsSource = options; filter.SelectedItem = options.Contains(selected ?? "", StringComparer.OrdinalIgnoreCase) ? selected : "همه برندها";
    }
    void ApplyPurchaseFilter()
    {
        if (PurchasesGrid == null) return;
        var needle = Rules.Normalize(PurchaseSearch?.Text ?? ""); var brand = PurchaseBrandFilter?.SelectedItem as string ?? "همه برندها";
        var visible = purchaseRows.Where(x => (brand == "همه برندها" || x.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase)) &&
            (needle.Length == 0 || x.Supplier.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Brand.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
        PurchasesGrid.ItemsSource = visible;
        PurchaseSummary.Text = $"{visible.Count} از {purchaseRows.Count} ردیف · {Rules.Money(visible.Sum(x => x.Value.Quantity))} عدد · بهای خالص خرید: {Rules.ReportMoney(visible.Sum(x => Rules.NetPurchase(x.Value)))} ریال";
    }
    void ApplySaleFilter()
    {
        if (SalesGrid == null) return;
        var needle = Rules.Normalize(SaleSearch?.Text ?? ""); var brand = SaleBrandFilter?.SelectedItem as string ?? "همه برندها";
        var visible = saleRows.Where(x => (brand == "همه برندها" || x.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase)) &&
            (needle.Length == 0 || x.Customer.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Code.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Brand.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
        SalesGrid.ItemsSource = visible;
        var invoice = visible.Sum(x => Rules.NetSaleBase(x.Value)); var discount = visible.Sum(x => Rules.CashDiscountAmount(x.Value)); var sales = visible.Sum(x => x.Settlement.Sales); var cost = visible.Sum(x => x.Settlement.Cost); var shortage = visible.Sum(x => x.Settlement.Shortage);
        SaleSummary.Text = $"{visible.Count} از {saleRows.Count} ردیف · فروش پس از کسورات: {Rules.ReportMoney(invoice)} ریال · تخفیف نقدی: {Rules.ReportMoney(discount)} ریال · دریافتی نهایی: {Rules.ReportMoney(sales)} ریال · سود ناخالص: {Rules.ReportMoney(sales - cost)} ریال" + (shortage > 0 ? $" · کسری تأییدنشده: {Rules.Money(shortage)} عدد" : "");
    }

    void OpenReconciliations(object s, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 4;
        ReconciliationSearch.Focus();
    }
    void ReconciliationSearchChanged(object s, TextChangedEventArgs e) => ApplyReconciliationFilter();
    void ClearReconciliationSearch(object s, RoutedEventArgs e) => ReconciliationSearch.Text = "";
    void ReconciliationsGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        Guard(() =>
        {
            if (ReconciliationsGrid.SelectedItem is not ReconciliationGridRow row) return;
            var candidate = row.Candidate;
            if (!CanEdit(Rules.MonthOf(candidate.FirstDate))) throw new InvalidOperationException($"ماه {Rules.MonthOf(candidate.FirstDate)} بسته است؛ ابتدا آن را باز کنید.");
            var dialog = new ReconciliationDialog(ledger, candidate, key => CanEdit(key)) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Purchases == null) return;
            SaveLedger(ledger with { Purchases = dialog.Purchases }, $"مغایرت کد {candidate.Code} رسیدگی شد", Rules.MonthOf(candidate.FirstDate));
        });
    }
    void PendingBrandSearchChanged(object s, TextChangedEventArgs e) => ApplyPendingBrandFilter();
    void ClearPendingBrandSearch(object s, RoutedEventArgs e) => PendingBrandSearch.Text = "";
    void PendingBrandsGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        Guard(() =>
        {
            if (PendingBrandsGrid.SelectedItem is not PendingBrandGridRow selected) return;
            var dialog = new PendingBrandDialog(ledger.Brands, selected, ledger.PendingBrandTransactions) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Value == null) return;
            ProcessPendingBrand(selected, dialog.Value);
        });
    }
    void ProcessPendingBrand(PendingBrandGridRow selected, PendingBrandResolution resolution)
    {
        var brandName = Rules.Normalize(resolution.BrandName);
        var brand = ledger.Brands.FirstOrDefault(x => Rules.Normalize(x.Name).Equals(brandName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("این برند ثبت نشده است. ابتدا آن را در مدیریت برندها ایجاد کنید.");
        var prefixes = BrandPrefixRules.Parse(resolution.Prefix);
        if (resolution.ApplyToPrefix && prefixes.Count != 1) throw new InvalidDataException("برای ثبت قانون، یک پیشوند عددی وارد کنید.");
        var targets = !resolution.ApplyToPrefix
            ? selected.Values
            : ledger.PendingBrandTransactions.Where(x => Rules.Digits(x.Transaction.Code).StartsWith(prefixes[0], StringComparison.Ordinal)).ToList();
        if (targets.Count == 0) throw new InvalidDataException("ردیفی برای پردازش یافت نشد.");
        foreach (var row in targets) if (!CanEdit(Rules.MonthOf(row.Transaction.Date))) throw new InvalidOperationException($"ماه {Rules.MonthOf(row.Transaction.Date)} بسته است؛ ابتدا آن را باز کنید.");

        var brands = ledger.Brands.ToList();
        if (resolution.ApplyToPrefix && !brand.CodePrefixes.Select(Rules.Digits).Contains(prefixes[0], StringComparer.Ordinal))
        {
            var index = brands.FindIndex(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(brand.Name), StringComparison.OrdinalIgnoreCase));
            brands[index] = brand with { CodePrefixes = [.. brand.CodePrefixes, prefixes[0]] };
            brand = brands[index]; BrandPrefixRules.ValidateUnique(brands);
        }
        var items = ledger.Items.ToList();
        foreach (var group in targets.GroupBy(x => x.Transaction.Code, StringComparer.OrdinalIgnoreCase))
        {
            var first = group.First().Transaction; var itemIndex = items.FindIndex(x => x.Code.Equals(first.Code, StringComparison.OrdinalIgnoreCase));
            if (itemIndex < 0) items.Add(new CatalogItem { Code = first.Code, Name = first.Name, Brand = brand.Name });
            else if (!Rules.Normalize(items[itemIndex].Brand).Equals(Rules.Normalize(brand.Name), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"کد {first.Code} قبلاً با برند دیگری ثبت شده است.");
        }
        var purchases = targets.Where(x => x.Kind == TransactionKind.Purchase).Select(x => x.Transaction).Select(row => new Purchase { Date = row.Date, Code = row.Code, Supplier = row.Account, Quantity = row.Quantity, UnitPrice = row.UnitPrice, Total = row.Total, Deductions = row.Deductions, BrandDiscount = brand.PurchaseDiscount, Offer = brand.Offer, ImportKey = row.ImportKey, Note = "ورود اکسل پس از تعیین برند" }).ToList();
        var sales = targets.Where(x => x.Kind == TransactionKind.Sale).Select(x => x.Transaction).Select(row => new Sale { Date = row.Date, Code = row.Code, Customer = row.Account, Quantity = row.Quantity, UnitPrice = row.UnitPrice, Total = row.Total, Deductions = row.Deductions, CashShare = brand.CashShare, CreditShare = brand.CreditShare, CashDiscount = brand.CashDiscount, ImportKey = row.ImportKey }).ToList();
        var ids = targets.Select(x => x.Id).ToHashSet();
        var next = ledger with { Brands = brands, Items = items, Purchases = [.. ledger.Purchases, .. purchases], Sales = [.. ledger.Sales, .. sales], ImportedRows = [.. ledger.ImportedRows, .. targets.Select(x => x.Transaction.ImportKey)], PendingBrandTransactions = ledger.PendingBrandTransactions.Where(x => !ids.Contains(x.Id)).ToList() };
        var scope = resolution.ApplyToPrefix ? $"پیشوند {prefixes[0]}" : $"کد {selected.Code}";
        SaveLedger(next, $"{targets.Count} ردیف با برند «{brand.Name}» و {scope} پردازش شد", Rules.MonthOf(targets.MaxBy(x => x.Transaction.Date)!.Transaction.Date));
    }
    void ImportPurchases(object s, RoutedEventArgs e) => ImportTransactions(TransactionKind.Purchase);
    void ImportSales(object s, RoutedEventArgs e) => ImportTransactions(TransactionKind.Sale);
    void ImportOpeningInventory(object s, RoutedEventArgs e)
    {
        var file = new OpenFileDialog { Filter = "فایل فشرده ۱۴۰۴|*.zip", Title = "استخراج موجودی افتتاحیه ۱۴۰۵ از خرید و فروش ۱۴۰۴" }; if (file.ShowDialog(this) != true) return;
        Guard(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "monthly-profit-opening-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            try
            {
                using var zip = ZipFile.OpenRead(file.FileName);
                var files = new List<(string Name, string Path)>();
                foreach (var entry in zip.Entries.Where(x => x.Name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)))
                {
                    var destination = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".xls"); using var input = entry.Open(); using var output = File.Create(destination); input.CopyTo(output); files.Add((entry.Name, destination));
                }
                var purchaseFiles = files.Where(x => x.Name.Contains("kharid", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("خرید", StringComparison.Ordinal)).ToList();
                var saleFiles = files.Where(x => x.Name.Contains("sell", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("فروش", StringComparison.Ordinal)).ToList();
                if (purchaseFiles.Count == 0 || saleFiles.Count == 0) throw new InvalidDataException("فایل خرید و فروش ۱۴۰۴ در ZIP یافت نشد.");
                TransactionImportReview Merge(IEnumerable<string> paths)
                {
                    var all = paths.Select(TransactionImport.Review).ToList(); return new TransactionImportReview([.. all.SelectMany(x => x.Rows)], [.. all.SelectMany(x => x.Issues)], all.Sum(x => x.Ignored));
                }
                var purchaseReview = Merge(purchaseFiles.Select(x => x.Path)); var saleReview = Merge(saleFiles.Select(x => x.Path));
                var review = OpeningInventory.Build(purchaseReview, saleReview);
                var note = $"{review.Purchases} خرید و {review.Sales} فروش ۱۴۰۴ بررسی شد. {review.Lots.Count} لایهٔ موجودی افتتاحیه ساخته می‌شود.";
                if (review.Issues.Count > 0) note += $"\n{review.Issues.Count} مغایرت تاریخی وجود دارد و خودکار وارد موجودی نمی‌شود؛ بعداً دستی کنترل کنید.";
                if (ledger.OpeningLots.Count > 0) note += "\nموجودی افتتاحیه قبلی جایگزین خواهد شد.";
                if (MessageBox.Show(this, note, "موجودی افتتاحیه", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                // برای نمایش موجودی افتتاحیه، فقط کدهایی که قاعدهٔ برند دارند در کاتالوگ ثبت می‌شوند.
                var next = BrandPrefixRules.EnsureDefaults(ledger);
                var knownOpeningRows = purchaseReview.Rows.Where(x => next.Items.Any(y => y.Code.Equals(x.Code, StringComparison.OrdinalIgnoreCase)) || BrandPrefixRules.Detect(next.Brands, x.Code) != null).ToList();
                ResolveKnownImportItems(ref next, knownOpeningRows);
                SaveLedger(next with { OpeningLots = review.Lots }, "موجودی افتتاحیه از ۱۴۰۴ استخراج شد");
                if (review.Issues.Count > 0) MessageBox.Show(this, string.Join("\n", review.Issues.Take(20).Select(x => "ردیف " + x.Row + ": " + x.Message)), "مغایرت‌های تاریخی");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
    }
    void ImportTransactions(TransactionKind kind)
    {
        var title = kind == TransactionKind.Purchase ? "ورود خرید از اکسل" : "ورود فروش از اکسل"; var file = new OpenFileDialog { Filter = "فایل اکسل|*.xls;*.xlsx", Title = title }; if (file.ShowDialog(this) != true) return;
        Guard(() =>
        {
            var review = TransactionImport.Review(file.FileName); if (review.Rows.Count == 0) throw new InvalidDataException("ردیف معتبر برای ورود یافت نشد.");
            if (review.Rows.Any(x => int.Parse(x.Date[..4], CultureInfo.InvariantCulture) < 1405)) throw new InvalidDataException("فایل‌های ۱۴۰۴ فقط برای موجودی افتتاحیه استفاده می‌شوند و در این ورود ثبت نمی‌شوند.");
            var importedKeys = ledger.ImportedRows.Concat(ledger.PendingBrandTransactions.Select(x => x.Transaction.ImportKey)).ToHashSet();
            var duplicate = review.Rows.Count(x => importedKeys.Contains(x.ImportKey)); var candidates = review.Rows.Where(x => !importedKeys.Contains(x.ImportKey)).ToList();
            if (candidates.Count == 0) { MessageBox.Show(this, "همه ردیف‌های معتبر این فایل قبلاً وارد شده‌اند.", title); return; }
            var text = $"{candidates.Count} ردیف معتبر آمادهٔ ورود است. {review.Ignored} ردیف آفر/بدون مبلغ نادیده گرفته شد؛ {review.Issues.Count} ردیف خطادار و {duplicate} ردیف تکراری است. ادامه می‌دهید؟";
            if (MessageBox.Show(this, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ImportRows(kind, candidates);
        });
    }
    void ImportRows(TransactionKind kind, List<ImportedTransaction> rows)
    {
        foreach (var row in rows) if (!CanEdit(Rules.MonthOf(row.Date))) throw new InvalidOperationException($"ماه {Rules.MonthOf(row.Date)} بسته است.");
        var staged = BrandPrefixRules.EnsureDefaults(ledger);
        var existingCodes = staged.Items.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownRows = rows.Where(x => existingCodes.Contains(x.Code) || BrandPrefixRules.Detect(staged.Brands, x.Code) != null).ToList();
        var unknownRows = rows.Where(x => !existingCodes.Contains(x.Code) && BrandPrefixRules.Detect(staged.Brands, x.Code) == null).ToList();
        var items = ResolveKnownImportItems(ref staged, knownRows);
        if (kind == TransactionKind.Purchase)
        {
            var purchases = knownRows.Select(row =>
            {
                var item = items[row.Code];
                var profile = staged.Brands.Single(x => Rules.Normalize(x.Name) == Rules.Normalize(item.Brand));
                return new Purchase { Date = row.Date, Code = item.Code, Supplier = row.Account, Quantity = row.Quantity, UnitPrice = row.UnitPrice, Total = row.Total, Deductions = row.Deductions, BrandDiscount = profile.PurchaseDiscount, Offer = profile.Offer, ImportKey = row.ImportKey, Note = "ورود اکسل" };
            }).ToList();
            staged = staged with { Purchases = [.. staged.Purchases, .. purchases], ImportedRows = [.. staged.ImportedRows, .. knownRows.Select(x => x.ImportKey)] };
        }
        else
        {
            var sales = knownRows.Select(row =>
            {
                var item = items[row.Code]; var profile = staged.Brands.Single(x => Rules.Normalize(x.Name) == Rules.Normalize(item.Brand));
                return new Sale { Date = row.Date, Code = item.Code, Customer = row.Account, Quantity = row.Quantity, UnitPrice = row.UnitPrice, Total = row.Total, Deductions = row.Deductions, CashShare = profile.CashShare, CreditShare = profile.CreditShare, CashDiscount = profile.CashDiscount, ImportKey = row.ImportKey };
            }).ToList();
            staged = staged with { Sales = [.. staged.Sales, .. sales], ImportedRows = [.. staged.ImportedRows, .. knownRows.Select(x => x.ImportKey)] };
        }
        if (unknownRows.Count > 0) staged = staged with { PendingBrandTransactions = [.. staged.PendingBrandTransactions, .. unknownRows.Select(x => new PendingBrandTransaction { Kind = kind, Transaction = x })] };
        var label = kind == TransactionKind.Purchase ? "خرید" : "فروش";
        var message = $"{knownRows.Count} ردیف {label} وارد شد" + (unknownRows.Count == 0 ? "." : $"؛ {unknownRows.Count} ردیف با کد ناشناخته در تب «کدهای ناشناخته» منتظر تعیین برند است.");
        SaveLedger(staged, message, Rules.MonthOf(rows.MaxBy(x => x.Date)!.Date));
        if (unknownRows.Count > 0)
        {
            MainTabs.SelectedIndex = 5;
            PendingBrandSearch.Focus();
        }
    }

    Dictionary<string, CatalogItem> ResolveKnownImportItems(ref Ledger state, List<ImportedTransaction> rows)
    {
        state = BrandPrefixRules.EnsureDefaults(state);
        var currentBrands = state.Brands;
        var result = state.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (result.ContainsKey(row.Code)) continue;
            var profile = BrandPrefixRules.Detect(currentBrands, row.Code) ?? throw new InvalidDataException($"برای کد {row.Code} برند ثبت نشده است.");
            result[row.Code] = new CatalogItem { Code = row.Code, Name = row.Name, Brand = profile.Name };
        }
        state = state with { Items = result.Values.OrderBy(x => x.Code).ToList() };
        return result;
    }
    void DrawTransactions()
    {
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var purchases = ledger.Purchases.Where(x => Rules.MonthOf(x.Date) == current.Key).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList();
        purchaseRows = purchases.Select(x => new PurchaseGridRow(x, items[x.Code])).ToList();
        SetBrandFilter(PurchaseBrandFilter, purchaseRows.Select(x => x.Brand).Distinct(StringComparer.OrdinalIgnoreCase));
        ApplyPurchaseFilter();

        var calc = LedgerCalculator.Calculate(ledger);
        var sales = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == current.Key).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).ToList();
        saleRows = sales.Select(x => new SaleGridRow(x, items[x.Code], calc.Sales[x.Id])).ToList();
        SetBrandFilter(SaleBrandFilter, saleRows.Select(x => x.Brand).Distinct(StringComparer.OrdinalIgnoreCase));
        ApplySaleFilter();
    }

    static string PreviousMonth(string key)
    {
        var year = int.Parse(key[..4], CultureInfo.InvariantCulture); var month = int.Parse(key[5..], CultureInfo.InvariantCulture) - 1;
        if (month == 0) { year--; month = 12; }
        return $"{year:0000}/{month:00}";
    }

    void DrawInventory()
    {
        var closing = LedgerCalculator.CalculateAtEndOfMonth(ledger, current.Key).Stock.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var opening = LedgerCalculator.CalculateAtEndOfMonth(ledger, PreviousMonth(current.Key)).Stock.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var purchases = ledger.Purchases.Where(x => Rules.MonthOf(x.Date) == current.Key).GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity), StringComparer.OrdinalIgnoreCase);
        var sales = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == current.Key).GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity), StringComparer.OrdinalIgnoreCase);
        var codes = opening.Keys.Concat(closing.Keys).Concat(purchases.Keys).Concat(sales.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var rows = codes.Where(items.ContainsKey).Select(code => new InventoryGridRow(items[code], opening.GetValueOrDefault(code)?.Quantity ?? 0, purchases.GetValueOrDefault(code), sales.GetValueOrDefault(code), closing.GetValueOrDefault(code)?.Quantity ?? 0, closing.GetValueOrDefault(code)?.Cost ?? 0)).OrderBy(x => x.Brand).ThenBy(x => x.Code).ToList();
        InventoryGrid.ItemsSource = rows;
        InventorySummary.Text = $"{rows.Count} کالا · موجودی پایان ماه: {Rules.Money(rows.Sum(x => x.Closing))} عدد · ارزش موجودی: {Rules.ReportMoney(rows.Sum(x => x.Cost))} ریال";
    }

    void DrawReconciliations()
    {
        reconciliationCandidates = ReconciliationPlanner.Existing(ledger);
        ApplyReconciliationFilter();
        ReconciliationSummary.Text = reconciliationCandidates.Count == 0 ? "همه فروش‌ها با موجودی قابل‌ردیابی پوشش داده شده‌اند." : $"{reconciliationCandidates.Count} کد کالا، در مجموع {Rules.Money(reconciliationCandidates.Sum(x => x.Quantity))} عدد کسری دارد. برای رسیدگی یا ویرایش تعدیل‌های قبلی، روی ردیف کالا دوبار کلیک کنید.";
    }
    void ApplyReconciliationFilter()
    {
        if (ReconciliationsGrid == null) return;
        var needle = Rules.Normalize(ReconciliationSearch?.Text ?? "");
        var visible = reconciliationCandidates.Where(x => needle.Length == 0 || x.Code.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Brand.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .Select(x => new ReconciliationGridRow(x)).ToList();
        ReconciliationsGrid.ItemsSource = visible;
    }

    void DrawPendingBrands()
    {
        pendingBrandRows = ledger.PendingBrandTransactions.GroupBy(x => x.Transaction.Code, StringComparer.OrdinalIgnoreCase).Select(x => new PendingBrandGridRow(x.ToList()))
            .OrderBy(x => x.FirstDate, StringComparer.Ordinal).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToList();
        ApplyPendingBrandFilter();
        PendingBrandSummary.Text = pendingBrandRows.Count == 0 ? "همهٔ کدهای واردشده با قواعد برند تشخیص داده شده‌اند." : $"{pendingBrandRows.Count} کد و {ledger.PendingBrandTransactions.Count} ردیف هنوز برند ندارند. برای رسیدگی، روی یک ردیف دوبار کلیک کنید؛ در دیالوگ، برند را انتخاب و تعیین کنید فقط همان کد یا تمام کدهای یک پیشوند پردازش شوند.";
    }
    void ApplyPendingBrandFilter()
    {
        if (PendingBrandsGrid == null) return;
        var needle = Rules.Normalize(PendingBrandSearch?.Text ?? "");
        PendingBrandsGrid.ItemsSource = pendingBrandRows.Where(x => needle.Length == 0 || x.Code.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    void DrawBrandSettings()
    {
        BrandSettingsGrid.ItemsSource = ledger.Brands.OrderBy(x => x.Name).Select(x => new BrandSettingsRow(x)).ToList();
        BrandSummary.Text = $"{ledger.Brands.Count} برند فعال؛ پیشوند کد، برند کالاهای واردشده را تعیین می‌کند و درصدها برای ثبت‌های دستی و محاسبه سهم نقدی/چکی استفاده می‌شوند.";
    }
    List<ItemMonthRow> ItemRows()
    {
        var calc = LedgerCalculator.Calculate(ledger); var stock = LedgerCalculator.CalculateAtEndOfMonth(ledger, current.Key).Stock.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var purchases = ledger.Purchases.Where(x => Rules.MonthOf(x.Date) == current.Key).GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Sum(Rules.NetPurchase), StringComparer.OrdinalIgnoreCase);
        var sales = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == current.Key).GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        return ledger.Items.Where(x => purchases.ContainsKey(x.Code) || sales.ContainsKey(x.Code) || stock.ContainsKey(x.Code)).Select(item => { var saleList = sales.GetValueOrDefault(item.Code, []); var settled = saleList.Select(x => calc.Sales[x.Id]).ToList(); var s = stock.GetValueOrDefault(item.Code); return new ItemMonthRow(item, s?.Quantity ?? 0, purchases.GetValueOrDefault(item.Code), settled.Sum(x => x.Sales), settled.Sum(x => x.Cost), settled.Sum(x => x.Profit), settled.Sum(x => x.Shortage)); }).OrderBy(x => x.Brand).ThenBy(x => x.Code).ToList();
    }
    void DrawBrands()
    {
        var groups = ItemRows().GroupBy(x => x.Brand).Select(g => new BrandRow(g.Key, g.Count(), g.Sum(x => x.Sales), g.Sum(x => x.Profit), g.Sum(x => x.Shortage))).OrderByDescending(x => x.Profit).ToList(); foreach (var row in groups) row.Rank = 1 + groups.Count(x => x.Profit > row.Profit); BrandsGrid.ItemsSource = groups;
    }
    void HistoryFilterChanged(object s, SelectionChangedEventArgs e) { if (!loading) DrawHistory(); }
    void ClearHistoryFilter(object s, RoutedEventArgs e) { loading = true; HistoryFrom.SelectedItem = null; HistoryTo.SelectedItem = null; loading = false; DrawHistory(); }
    LedgerTotal Total(Month month) => LedgerCalculator.SummarizeMonth(ledger, month);
    void DrawHistory()
    {
        var keys = store.Keys(); var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        if (from != null && to != null && string.CompareOrdinal(from, to) > 0) { reportMonths = []; HistoryGrid.ItemsSource = Array.Empty<HistoryRow>(); HistorySummary.Text = "بازه ماه نامعتبر است."; DrawTrend(); return; }
        reportMonths = keys.Where(k => (from == null || k.CompareTo(from) >= 0) && (to == null || k.CompareTo(to) <= 0)).OrderBy(k => k).Select(store.Load).ToList(); var totals = reportMonths.Select(Total).ToList(); HistoryGrid.ItemsSource = reportMonths.OrderByDescending(x => x.Key).Select(x => new HistoryRow(x, Total(x))).ToList();
        var cost = totals.Sum(x => x.Cost); var sales = totals.Sum(x => x.Sales); var profit = totals.Sum(x => x.Profit); var fixedCost = totals.Sum(x => x.FixedCost); var net = totals.Sum(x => x.Net); RangeSales.Text = Rules.ReportMoney(sales); RangeProfit.Text = Rules.ReportMoney(profit); RangeNet.Text = Rules.ReportMoney(net); RangeNet.Foreground = net < 0 ? Brushes.Firebrick : net > 0 ? Brushes.SeaGreen : Brushes.SlateGray; RangeOutcome.Text = Outcome(net) + " نهایی"; HistorySummary.Text = reportMonths.Count == 0 ? "در این بازه ماهی ثبت نشده است." : $"{reportMonths.Count} ماه · بهای تمام‌شده: {Rules.ReportMoney(cost)} ریال · هزینه ثابت: {Rules.ReportMoney(fixedCost)} ریال · حاشیه سود: {Rules.Percent(sales == 0 ? null : profit / sales)}"; DrawTrend();
    }
    void TrendFilterChanged(object s, RoutedEventArgs e) => DrawTrend(); void TrendSizeChanged(object s, SizeChangedEventArgs e) => DrawTrend();
    void DrawTrend()
    {
        if (TrendCanvas == null || TrendEmpty == null) return; TrendCanvas.Children.Clear(); if (reportMonths.Count < 2) { TrendEmpty.Visibility = Visibility.Visible; return; } TrendEmpty.Visibility = Visibility.Collapsed; var width = TrendCanvas.ActualWidth; var height = TrendCanvas.ActualHeight; if (width < 80 || height < 80) return;
        var selected = new List<(string Name, Brush Brush, Func<LedgerTotal, decimal> Get)>(); if (TrendSales.IsChecked == true) selected.Add(("فروش", Brushes.SteelBlue, x => x.Sales)); if (TrendProfit.IsChecked == true) selected.Add(("سود", Brushes.SeaGreen, x => x.Profit)); if (TrendNet.IsChecked == true) selected.Add(("نتیجه", Brushes.Firebrick, x => x.Net)); if (selected.Count == 0) return;
        var totals = reportMonths.Select(Total).ToList(); var values = selected.SelectMany(s => totals.Select(s.Get)).ToList(); var min = Math.Min(0, values.Min()); var max = Math.Max(0, values.Max()); if (min == max) { min--; max++; } var pad = 30d; var chartW = width - pad * 2; var chartH = height - 48; double X(int i) => pad + chartW * i / (reportMonths.Count - 1); double Y(decimal v) => 16 + (double)((max - v) / (max - min)) * chartH;
        TrendCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = pad, X2 = width - pad, Y1 = Y(0), Y2 = Y(0), Stroke = Brushes.LightGray, StrokeThickness = 1 });
        foreach (var series in selected) { var line = new System.Windows.Shapes.Polyline { Stroke = series.Brush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round }; for (var i = 0; i < totals.Count; i++) line.Points.Add(new Point(X(i), Y(series.Get(totals[i])))); TrendCanvas.Children.Add(line); for (var i = 0; i < totals.Count; i++) { var p = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = series.Brush, ToolTip = $"{reportMonths[i].Key}\n{series.Name}: {Rules.ReportMoney(series.Get(totals[i]))} ریال" }; Canvas.SetLeft(p, X(i) - 4); Canvas.SetTop(p, Y(series.Get(totals[i])) - 4); TrendCanvas.Children.Add(p); } }
        for (var i = 0; i < reportMonths.Count; i++) { var label = new TextBlock { Text = reportMonths[i].Key, FontSize = 10, Foreground = Brushes.DimGray }; Canvas.SetLeft(label, X(i) - 24); Canvas.SetTop(label, height - 22); TrendCanvas.Children.Add(label); }
    }
    void PrintReport(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return; Guard(() => { var print = new PrintDialog(); if (print.ShowDialog() != true) return; var t = Total(current); var doc = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("pack://application:,,,/Assets/Fonts/#Vazirmatn"), FontSize = 11, PagePadding = new Thickness(35), ColumnWidth = double.PositiveInfinity, PageWidth = print.PrintableAreaWidth }; doc.Blocks.Add(new Paragraph(new Run("شرکت متحد توزیع ایرانیان")) { FontSize = 21, FontWeight = FontWeights.Bold }); doc.Blocks.Add(new Paragraph(new Run($"گزارش ماه {current.Key} — فروش: {Rules.ReportMoney(t.Sales)} ریال — سود ناخالص: {Rules.ReportMoney(t.Profit)} ریال — نتیجه: {Rules.ReportMoney(t.Net)} ریال ({Outcome(t.Net)})"))); print.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Monthly Profit " + current.Key); });
    }
    void ExportExcel(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var f = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = "MonthlyProfit-" + current.Key.Replace('/', '-') + ".xlsx" };
        if (f.ShowDialog(this) == true) Guard(() => { ExcelTransfer.ExportLedgerMonth(ledger, current, f.FileName); Status.Text = "خروجی اکسل ماه ذخیره شد."; });
    }
    void ExportRange(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit() || reportMonths.Count == 0) { MessageBox.Show(this, "ابتدا یک بازه معتبر انتخاب کنید."); return; }
        var f = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = $"MonthlyProfit-Range-{reportMonths.First().Key.Replace('/', '-')}-{reportMonths.Last().Key.Replace('/', '-')}.xlsx" };
        if (f.ShowDialog(this) == true) Guard(() => { ExcelTransfer.ExportLedgerRange(ledger, reportMonths, f.FileName); Status.Text = "خروجی اکسل بازه ذخیره شد."; });
    }
    void Backup(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return; var f = new SaveFileDialog { Filter = "Monthly Profit Backup|*.sqlite", FileName = "MonthlyProfit-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sqlite" }; if (f.ShowDialog(this) == true) Guard(() => { store.Backup(f.FileName); preferences = preferences with { LastBackupUtc = DateTime.UtcNow, LastBackupPath = f.FileName }; store.SavePreferences(preferences); DrawBackupStatus(); Status.Text = "پشتیبان ذخیره شد."; });
    }
    void Restore(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return; var f = new OpenFileDialog { Filter = "Monthly Profit Backup|*.sqlite" }; if (f.ShowDialog(this) != true) return; Guard(() => { Store.CheckBackup(f.FileName); if (MessageBox.Show(this, "همه اطلاعات فعلی جایگزین شوند؟", "بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; var safety = store.Restore(f.FileName); Reload(); Status.Text = "بازیابی انجام شد. پشتیبان: " + safety; });
    }
    void OpenBackupFolder(object s, RoutedEventArgs e) => Guard(() => { var p = Path.Combine(DataDirectory, "backups"); Directory.CreateDirectory(p); Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true }); });
    void AutoBackupChanged(object s, RoutedEventArgs e) { if (!loading) Guard(() => { preferences = preferences with { AutoBackupOnExit = AutoBackup.IsChecked == true }; store.SavePreferences(preferences); DrawBackupStatus(); }); }
    void DrawBackupStatus()
    {
        var auto = store.LastAutomaticBackup(); var enabled = preferences.AutoBackupOnExit; AutoBackupBadgeText.Text = enabled ? "فعال" : "غیرفعال"; AutoBackupBadgeText.Foreground = enabled ? Brushes.SeaGreen : Brushes.SlateGray; AutoBackupBadge.Background = enabled ? new SolidColorBrush(Color.FromRgb(236, 253, 243)) : new SolidColorBrush(Color.FromRgb(238, 242, 246)); AutoBackupState.Text = enabled ? "زمان اجرا: هنگام خروج از برنامه" : "زمان اجرا: در حال حاضر غیرفعال است"; LastAutoBackup.Text = auto == null ? "آخرین پشتیبان خودکار: هنوز نسخه‌ای ایجاد نشده است." : "آخرین پشتیبان خودکار: ‎" + File.GetLastWriteTime(auto).ToString("yyyy/MM/dd HH:mm"); var manual = preferences.LastBackupUtc != null; ManualBackupBadgeText.Text = manual ? "ثبت شده" : "ثبت نشده"; ManualBackupBadgeText.Foreground = manual ? Brushes.SeaGreen : Brushes.SlateGray; ManualBackupBadge.Background = manual ? new SolidColorBrush(Color.FromRgb(236, 253, 243)) : new SolidColorBrush(Color.FromRgb(238, 242, 246)); LastManualBackup.Text = manual ? "آخرین پشتیبان دستی: ‎" + preferences.LastBackupUtc!.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm") : "هنوز یک پشتیبان دستی ایجاد نشده است.";
    }
    void CopyVersion(object s, RoutedEventArgs e) { Clipboard.SetText("شرکت متحد توزیع ایرانیان | سامانه مدیریت سود ماهانه | نسخه ۰٫۴٫۱۵ | گردش تاریخ‌دار کالا"); Status.Text = "اطلاعات نسخه کپی شد."; }
}

public sealed class ItemMonthRow(CatalogItem item, decimal stock, decimal purchase, decimal sales, decimal cost, decimal profit, decimal shortage)
{
    public string Code => item.Code; public string Brand => item.Brand; public string Name => item.Name; public decimal Sales => sales; public decimal Profit => profit; public decimal Shortage => shortage; public string StockText => Rules.Money(stock); public string PurchaseText => Rules.Money(purchase); public string SalesText => Rules.Money(sales); public string CostText => Rules.Money(cost); public string ProfitText => Rules.Money(profit);
}
public sealed class PurchaseGridRow(Purchase purchase, CatalogItem item)
{
    public Purchase Value => purchase;
    public string Type => purchase.IsAdjustment ? "تعدیل" : "خرید"; public string Date => purchase.Date; public string Supplier => purchase.Supplier; public string Code => purchase.Code; public string Brand => item.Brand; public string Name => item.Name;
    public string QuantityText => Rules.Money(purchase.Quantity); public string TotalText => Rules.ReportMoney(purchase.Total); public string DeductionsText => Rules.ReportMoney(purchase.Deductions); public string NetText => Rules.ReportMoney(Rules.NetPurchase(purchase));
}
public sealed class SaleGridRow(Sale sale, CatalogItem item, SaleSettlement settlement)
{
    public Sale Value => sale; public SaleSettlement Settlement => settlement;
    public string Date => sale.Date; public string Customer => sale.Customer; public string Code => sale.Code; public string Brand => item.Brand; public string Name => item.Name;
    public string QuantityText => Rules.Money(sale.Quantity); public string InvoiceNetText => Rules.ReportMoney(Rules.NetSaleBase(sale)); public string CashDiscountText => Rules.ReportMoney(Rules.CashDiscountAmount(sale)); public string SalesText => Rules.ReportMoney(settlement.Sales); public string CostText => Rules.ReportMoney(settlement.Cost);
    public string Status => settlement.Shortage > 0 ? $"کسری {Rules.Money(settlement.Shortage)}" : "تأییدشده";
}
public sealed class InventoryGridRow(CatalogItem item, decimal opening, decimal purchased, decimal sold, decimal closing, decimal cost)
{
    public string Code => item.Code; public string Brand => item.Brand; public string Name => item.Name; public decimal Closing => closing; public decimal Cost => cost;
    public string OpeningText => Rules.Money(opening); public string PurchasedText => Rules.Money(purchased); public string SoldText => Rules.Money(sold); public string ClosingText => Rules.Money(closing); public string CostText => Rules.ReportMoney(cost);
}
public sealed class ReconciliationGridRow(ReconciliationCandidate value)
{
    public ReconciliationCandidate Candidate => value;
    public string FirstDate => value.FirstDate; public string Code => value.Code; public string Brand => value.Brand; public string Name => value.Name; public decimal Quantity => value.Quantity; public int SaleRows => value.SaleRows;
    public string QuantityText => Rules.Money(value.Quantity); public string SuggestedText => value.SuggestedUnitCost <= 0 ? "نیازمند ورود دستی" : Rules.ReportMoney(value.SuggestedUnitCost);
}
public sealed class PendingBrandGridRow(List<PendingBrandTransaction> values)
{
    public List<PendingBrandTransaction> Values => values;
    public string Code => values[0].Transaction.Code;
    public string Name => values[0].Transaction.Name;
    public int PurchaseRows => values.Count(x => x.Kind == TransactionKind.Purchase);
    public int SaleRows => values.Count(x => x.Kind == TransactionKind.Sale);
    public string QuantityText => Rules.Money(values.Sum(x => x.Transaction.Quantity));
    public string FirstDate => values.Min(x => x.Transaction.Date);
}
public sealed class BrandSettingsRow(Brand brand)
{
    public Brand Value => brand;
    public string Name => brand.Name; public string Prefixes => BrandPrefixRules.Display(brand.CodePrefixes); public string PurchaseDiscountText => Rules.Percent(brand.PurchaseDiscount); public string OfferText => Rules.Percent(brand.Offer); public string MarkupText => Rules.Percent(brand.Markup);
    public string CashShareText => Rules.Percent(brand.CashShare); public string CreditShareText => Rules.Percent(brand.CreditShare); public string CashDiscountText => Rules.Percent(brand.CashDiscount);
}
public sealed class BrandRow(string brand, int count, decimal sales, decimal profit, decimal shortage)
{
    public int Rank { get; set; } public string Brand => brand; public int Count => count; public decimal Profit => profit; public string SalesText => Rules.ReportMoney(sales); public string ProfitText => Rules.ReportMoney(profit); public string MarginText => Rules.Percent(sales == 0 ? null : profit / sales); public string Status => shortage > 0 ? $"کسری {Rules.Money(shortage)}" : "تأییدشده";
}
public sealed class HistoryRow(Month month, LedgerTotal total)
{
    public string Key => month.Key; public string CostText => Rules.ReportMoney(total.Cost); public string SalesText => Rules.ReportMoney(total.Sales); public string ProfitText => Rules.ReportMoney(total.Profit); public string FixedText => Rules.ReportMoney(total.FixedCost); public string NetText => Rules.ReportMoney(total.Net); public string OutcomeText => total.Net < 0 ? "زیان" : total.Net > 0 ? "سود" : "سر‌به‌سر";
}
