using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    Preferences preferences = new();
    LedgerCalculation calculation = new();
    List<SaleGridRow> saleRows = [];
    List<Month> reportMonths = [];
    List<LedgerTotal> reportTotals = [];
    bool loading;
    bool firstRender = true;
    string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonthlyProfit", "Data");

    public MainWindow()
    {
        InitializeComponent();
        store = new Store(Path.Combine(DataDirectory, "monthly-profit.sqlite"));
        MoneyInput.Attach(Fixed);
        preferences = store.LoadPreferences();
        loading = true; AutoBackup.IsChecked = preferences.AutoBackupOnExit; loading = false;
        AboutVersion.Text = "نسخه ۰٫۶٫۰ · محاسبه فروش‌محور · داده‌ها فقط محلی هستند.";
        ContentRendered += OnFirstContentRendered;
        Closing += OnClosing;
    }

    void OnFirstContentRendered(object? sender, EventArgs e)
    {
        if (!firstRender) return;
        firstRender = false; ContentRendered -= OnFirstContentRendered;
        Guard(Reload);
    }
    void Guard(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "عملیات انجام نشد", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    static string CurrentMonth()
    {
        var calendar = new PersianCalendar(); var today = DateTime.Today;
        return $"{calendar.GetYear(today):0000}/{calendar.GetMonth(today):00}";
    }
    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ResolveFixedEdit()) { e.Cancel = true; return; }
        if (preferences.AutoBackupOnExit) Guard(() => { store.CreateAutomaticBackup(preferences.AutoBackupKeep); preferences = preferences with { LastAutoBackupUtc = DateTime.UtcNow }; store.SavePreferences(preferences); });
    }

    void Reload()
    {
        var raw = store.LoadLedger();
        ledger = BrandPrefixRules.EnsureDefaults(raw);
        if (ledger != raw) store.SaveLedger(ledger);
        var keys = store.Keys();
        if (keys.Count == 0) { store.EnsureMonth(CurrentMonth()); keys = store.Keys(); }
        loading = true;
        var oldSelected = Months.SelectedItem as string;
        var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        Months.ItemsSource = keys; Months.SelectedItem = oldSelected != null && keys.Contains(oldSelected) ? oldSelected : keys.First();
        HistoryFrom.ItemsSource = keys; HistoryTo.ItemsSource = keys; HistoryFrom.SelectedItem = from != null && keys.Contains(from) ? from : null; HistoryTo.SelectedItem = to != null && keys.Contains(to) ? to : null;
        loading = false;
        current = store.Load((string)Months.SelectedItem);
        EnsureBrandMonthConfirmed(current.Key);
        Draw();
    }
    bool CanEdit(string key, bool show = true)
    {
        if (!store.Keys().Contains(key) || !store.Load(key).IsClosed) return true;
        if (show) MessageBox.Show(this, $"ماه {key} بسته است. ابتدا آن را باز کنید.", "ماه بسته", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }
    bool CanEdit() => CanEdit(current.Key);
    bool EnsureBrandMonthConfirmed(string monthKey)
    {
        if (BrandMonthRules.IsConfirmed(ledger, monthKey)) return true;
        var dialog = new BrandMonthConfirmationDialog(BrandMonthRules.DraftFor(ledger, monthKey)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null) { Status.Text = $"تنظیمات برندهای ماه {monthKey} تأیید نشده است."; return false; }
        ledger = BrandMonthRules.Confirm(ledger, monthKey, dialog.Value); store.SaveLedger(ledger);
        Status.Text = $"تنظیمات برندهای ماه {monthKey} تأیید شد."; return true;
    }
    bool EnsureBrandMonthsConfirmed(IEnumerable<string> keys)
    {
        foreach (var key in keys.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
            if (!EnsureBrandMonthConfirmed(key)) return false;
        return true;
    }

    void Draw()
    {
        calculation = LedgerCalculator.Calculate(ledger);
        var total = LedgerCalculator.SummarizeMonth(ledger, current, calculation);
        Sales.Text = Rules.ReportMoney(total.Sales); Profit.Text = Rules.ReportMoney(total.Profit); Net.Text = Rules.ReportMoney(total.Net);
        var color = total.Net < 0 ? Brushes.Firebrick : total.Net > 0 ? Brushes.SeaGreen : Brushes.SlateGray;
        Net.Foreground = color; NetLabel.Text = Outcome(total.Net); NetLabel.Foreground = color;
        ClosedBadge.Text = current.IsClosed ? "ماه بسته" : "ماه باز"; ClosedBadge.Foreground = current.IsClosed ? Brushes.IndianRed : Brushes.SeaGreen;
        Fixed.Text = Rules.Money(current.FixedCost);
        Breakdown.Text = $"فروش پس از کسورات فاکتور: {Rules.ReportMoney(total.InvoiceSales)} ریال\nتخفیف نقدی: {Rules.ReportMoney(total.CashDiscountAmount)} ریال\nدریافتی نقدی: {Rules.ReportMoney(total.Cash)} ریال\nفروش چکی: {Rules.ReportMoney(total.Credit)} ریال\nبهای تمام‌شده فروش‌ها: {Rules.ReportMoney(total.Cost)} ریال\nبرند: {total.Brands}   |   کالا: {total.Products}" + (total.PendingSalesCount == 0 ? "" : $"\n{total.PendingSalesCount} فروش به مبلغ {Rules.ReportMoney(total.PendingInvoiceSales)} ریال، منتظر تعیین برند یا تأیید تنظیمات است.");
        DrawSales(); DrawBrands(); DrawBrandSettings(); DrawUnknown(); DrawHistory(); DrawBackupStatus();
        Status.Text = $"ماه {current.Key} · {ledger.Sales.Count(x => Rules.MonthOf(x.Date) == current.Key)} فروش ثبت شده" + (total.PendingSalesCount == 0 ? "" : $" · {total.PendingSalesCount} فروش معلق");
    }
    static string Outcome(decimal net) => net < 0 ? "زیان" : net > 0 ? "سود" : "سر‌به‌سر";

    void SaveLedger(Ledger next, string action, string? month = null)
    {
        if (month != null && !CanEdit(month)) throw new InvalidOperationException($"ماه {month} بسته است.");
        store.SaveLedger(next); Reload(); Status.Text = action;
    }
    void SaveMonth(Month next, string action)
    {
        if (!CanEdit(next.Key)) return;
        next = next with { Audit = [.. next.Audit.TakeLast(499), new AuditEntry { Action = action }] };
        store.Save(next); current = store.Load(next.Key); Draw(); Status.Text = action;
    }
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
        Guard(() => { current = store.Load(key); EnsureBrandMonthConfirmed(key); Draw(); });
    }
    void OpenMonthMenu(object s, RoutedEventArgs e)
    {
        if (s is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }
    void ToggleMonthClosed(object s, RoutedEventArgs e)
    {
        var target = !current.IsClosed;
        if (MessageBox.Show(this, target ? "ماه بسته شود؟" : "ماه دوباره باز شود؟", target ? "بستن ماه" : "بازکردن ماه", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Guard(() => { current = store.SetClosed(current, target); Draw(); });
    }
    void SaveFixed(object s, RoutedEventArgs e) => Guard(() => SaveMonth(current with { FixedCost = Rules.Number(Fixed.Text), FixedExpenses = [] }, "هزینه ثابت ویرایش شد"));
    void FixedExpenses(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new FixedExpensesDialog(current) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Value != null) Guard(() => SaveMonth(current with { FixedExpenses = dialog.Value, FixedCost = dialog.Value.Sum(x => x.Amount) }, "ریز هزینه‌های ثابت ویرایش شد"));
    }

    void AddBrand(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new BrandEditorDialog(null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null) return;
        Guard(() =>
        {
            if (ledger.Brands.Any(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(dialog.Value.Name), StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("این برند قبلاً ثبت شده است.");
            SaveLedger(ledger with { Brands = [.. ledger.Brands, dialog.Value] }, $"برند «{dialog.Value.Name}» افزوده شد. درصدهای ماه را پیش از محاسبه تأیید کنید.");
        });
    }
    void BrandSettingsGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        if (BrandSettingsGrid.SelectedItem is not BrandSettingsRow row || !CanEdit()) return;
        var dialog = new BrandEditorDialog(row.Brand, row.Rate, current.Key) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null || dialog.RateValue == null) return;
        var editedRate = dialog.RateValue!;
        Guard(() =>
        {
            var old = row.Brand; var next = dialog.Value;
            if (!Rules.Normalize(old.Name).Equals(Rules.Normalize(next.Name), StringComparison.OrdinalIgnoreCase) && ledger.Items.Any(x => Rules.Normalize(x.Brand).Equals(Rules.Normalize(old.Name), StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("نام برند استفاده‌شده در فروش قابل تغییر نیست.");
            var brands = ledger.Brands.Select(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(old.Name), StringComparison.OrdinalIgnoreCase) ? next : x).ToList();
            BrandPrefixRules.ValidateUnique(brands);
            var settings = ledger.BrandMonths.Select(setting => setting with { Rates = setting.Rates.Select(rate => Rules.Normalize(rate.BrandName).Equals(Rules.Normalize(old.Name), StringComparison.OrdinalIgnoreCase) ? rate with { BrandName = next.Name } : rate).ToList() }).ToList();
            var renamed = ledger with { Brands = brands, BrandMonths = settings };
            var rates = BrandMonthRules.DraftFor(renamed, current.Key).Rates.Select(rate => Rules.Normalize(rate.BrandName).Equals(Rules.Normalize(next.Name), StringComparison.OrdinalIgnoreCase) ? editedRate with { BrandName = next.Name } : rate).ToList();
            SaveLedger(BrandMonthRules.Confirm(renamed, current.Key, rates), $"برند «{next.Name}» و درصدهای ماه {current.Key} ویرایش شد؛ فروش‌های همان برند دوباره محاسبه شدند.");
        });
    }
    void ConfirmBrandMonth(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new BrandMonthConfirmationDialog(BrandMonthRules.DraftFor(ledger, current.Key)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null) return;
        Guard(() => { ledger = BrandMonthRules.Confirm(ledger, current.Key, dialog.Value); store.SaveLedger(ledger); Draw(); Status.Text = $"تنظیمات ماه {current.Key} تأیید شد."; });
    }

    void AddSale(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return;
        var dialog = new SaleDialog(ledger, current.Key.Replace("/", "") + "01") { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null || dialog.Item == null) return;
        Guard(() =>
        {
            var items = ledger.Items.ToList(); var index = items.FindIndex(x => x.Code.Equals(dialog.Item.Code, StringComparison.OrdinalIgnoreCase));
            if (index < 0) items.Add(dialog.Item);
            else if (!Rules.Normalize(items[index].Brand).Equals(Rules.Normalize(dialog.Item.Brand), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("کد کالا قبلاً با برند دیگری ثبت شده است.");
            SaveLedger(ledger with { Items = items, Sales = [.. ledger.Sales, dialog.Value] }, "فروش دستی ثبت شد.", Rules.MonthOf(dialog.Value.Date));
        });
    }

    // تب‌های خرید، موجودی و مغایرت از رابط پنهان شده‌اند. این handlerها فقط برای
    // سازگاری با XAML نسخهٔ مرجع باقی مانده‌اند و هیچ مسیر محاسباتی ندارند.
    void AddPurchase(object s, RoutedEventArgs e) => ShowRemovedModuleMessage();
    void ImportPurchases(object s, RoutedEventArgs e) => ShowRemovedModuleMessage();
    void ImportOpeningInventory(object s, RoutedEventArgs e) => ShowRemovedModuleMessage();
    void OpenReconciliations(object s, RoutedEventArgs e) => ShowRemovedModuleMessage();
    void PurchaseSearchChanged(object s, TextChangedEventArgs e) { }
    void PurchaseFilterChanged(object s, SelectionChangedEventArgs e) { }
    void ClearPurchaseFilters(object s, RoutedEventArgs e) { }
    void ReconciliationSearchChanged(object s, TextChangedEventArgs e) { }
    void ClearReconciliationSearch(object s, RoutedEventArgs e) { }
    void ReconciliationsGrid_MouseDoubleClick(object s, MouseButtonEventArgs e) { }
    void ShowRemovedModuleMessage() => MessageBox.Show(this, "این بخش در مدل فروش‌محور استفاده نمی‌شود.", "مدل فروش‌محور", MessageBoxButton.OK, MessageBoxImage.Information);
    void SalesGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        if (SalesGrid.SelectedItem is not SaleGridRow row || !CanEdit(Rules.MonthOf(row.Value.Date))) return;
        var dialog = new SaleEditorDialog(ledger, row.Value) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null) return;
        Guard(() =>
        {
            var sales = ledger.Sales.ToList(); var index = sales.FindIndex(x => x.Id == row.Value.Id);
            if (index < 0) throw new InvalidOperationException("فروش انتخاب‌شده یافت نشد.");
            sales[index] = dialog.Value; SaveLedger(ledger with { Sales = sales }, "فروش ویرایش شد.", Rules.MonthOf(dialog.Value.Date));
        });
    }
    void DrawSales()
    {
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        saleRows = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == current.Key).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).Select(x => new SaleGridRow(x, items[x.Code], calculation.Sales.GetValueOrDefault(x.Id))).ToList();
        ApplySalesFilter();
    }
    void SaleSearchChanged(object s, TextChangedEventArgs e) => ApplySalesFilter();
    void SaleFilterChanged(object s, SelectionChangedEventArgs e) => ApplySalesFilter();
    void ClearSaleFilters(object s, RoutedEventArgs e)
    {
        SaleSearch.Text = "";
        SaleBrandFilter.SelectedItem = "همه برندها";
    }
    void ApplySalesFilter()
    {
        if (SalesGrid == null) return;
        var needle = Rules.Normalize(SaleSearch.Text ?? "");
        var brand = SaleBrandFilter.SelectedItem as string ?? "همه برندها";
        var visible = saleRows.Where(x => (brand == "همه برندها" || x.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase)) && (needle.Length == 0 || x.Code.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Customer.Contains(needle, StringComparison.OrdinalIgnoreCase) || x.Brand.Contains(needle, StringComparison.OrdinalIgnoreCase))).ToList();
        var calculated = visible.Where(x => x.Settlement != null).ToList();
        var pending = visible.Count - calculated.Count;
        SalesGrid.ItemsSource = visible; SaleSummary.Text = $"{visible.Count} از {saleRows.Count} فروش · فروش واقعی: {Rules.ReportMoney(calculated.Sum(x => x.Settlement!.Sales))} ریال · سود: {Rules.ReportMoney(calculated.Sum(x => x.Settlement!.Profit))} ریال" + (pending == 0 ? "" : $" · {pending} فروش معلق");
    }
    void ImportSales(object s, RoutedEventArgs e)
    {
        var file = new OpenFileDialog { Filter = "فایل اکسل|*.xls;*.xlsx", Title = "ورود فروش از اکسل" };
        if (file.ShowDialog(this) != true) return;
        Guard(() =>
        {
            var review = TransactionImport.Review(file.FileName);
            if (review.Issues.Count > 0) throw new InvalidDataException($"{review.Issues.Count} ردیف اکسل نامعتبر است. نمونه: ردیف {review.Issues[0].Row} — {review.Issues[0].Message}");
            var known = review.Rows.Where(x => !ledger.ImportedRows.Contains(x.ImportKey)).ToList();
            var importMonths = known.Select(x => Rules.MonthOf(x.Date)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (importMonths.Any(x => !CanEdit(x))) return;
            var items = ledger.Items.ToList();
            var sales = new List<Sale>();
            foreach (var row in known)
            {
                var item = items.FirstOrDefault(x => x.Code.Equals(row.Code, StringComparison.OrdinalIgnoreCase));
                var brand = item?.Brand ?? BrandPrefixRules.Detect(ledger.Brands, row.Code)?.Name ?? "";
                if (item == null) { item = new CatalogItem { Code = row.Code, Name = row.Name, Brand = brand }; items.Add(item); }
                var rawSale = new Sale { Date = row.Date, Code = row.Code, Customer = row.Account, Quantity = row.Quantity, UnitPrice = row.UnitPrice, Total = row.Total, Deductions = row.Deductions, ImportKey = row.ImportKey };
                var month = Rules.MonthOf(row.Date); var rate = string.IsNullOrWhiteSpace(brand) ? null : BrandMonthRules.TryConfirmed(ledger, brand, month);
                sales.Add(rate is null ? rawSale : BrandMonthRules.Apply(rawSale, rate, month));
            }
            var pending = sales.Count(x => !Rules.HasRateSnapshot(x));
            SaveLedger(ledger with { Items = items, Sales = [.. ledger.Sales, .. sales], ImportedRows = [.. ledger.ImportedRows, .. known.Select(x => x.ImportKey)] }, $"{sales.Count} فروش از اکسل وارد شد." + (pending == 0 ? "" : $" {pending} ردیف تا تعیین برند یا تأیید ماه، معلق است."));
        });
    }

    void DrawBrandSettings()
    {
        var draft = BrandMonthRules.DraftFor(ledger, current.Key);
        var rates = draft.Rates.ToDictionary(x => Rules.Normalize(x.BrandName), StringComparer.OrdinalIgnoreCase);
        BrandSettingsGrid.ItemsSource = ledger.Brands.OrderBy(x => x.Name).Select(x => new BrandSettingsRow(x, rates[Rules.Normalize(x.Name)])).ToList();
        BrandSummary.Text = draft.IsConfirmed ? $"{ledger.Brands.Count} برند · درصدهای ماه {current.Key} تأیید شده‌اند · مبنا: {draft.SourceMonthKey}." : $"درصدهای ماه {current.Key} هنوز تأیید نشده‌اند؛ مبنای اولیه: {draft.SourceMonthKey}.";
        var selected = SaleBrandFilter.SelectedItem as string;
        var options = new[] { "همه برندها" }.Concat(ledger.Brands.Select(x => x.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)).ToList();
        SaleBrandFilter.ItemsSource = options;
        SaleBrandFilter.SelectedItem = options.Contains(selected ?? "", StringComparer.OrdinalIgnoreCase) ? selected : "همه برندها";
    }
    void DrawBrands()
    {
        var items = ledger.Items.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var groups = ledger.Sales.Where(x => Rules.MonthOf(x.Date) == current.Key && calculation.Sales.ContainsKey(x.Id)).GroupBy(x => items[x.Code].Brand).Select(g => new BrandRow(g.Key, g.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count(), g.Sum(x => calculation.Sales[x.Id].Sales), g.Sum(x => calculation.Sales[x.Id].Profit))).OrderByDescending(x => x.Profit).ToList();
        foreach (var row in groups) row.Rank = 1 + groups.Count(x => x.Profit > row.Profit);
        BrandsGrid.ItemsSource = groups;
    }

    void DrawUnknown()
    {
        var rows = ledger.Items.Where(x => string.IsNullOrWhiteSpace(x.Brand)).Select(item =>
        {
            var pending = ledger.Sales.Where(s => s.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase) && !calculation.Sales.ContainsKey(s.Id)).ToList();
            return new UnknownCodeRow(item, pending.Count, pending.Sum(Rules.NetSaleBase));
        }).Where(x => x.PendingCount > 0).OrderByDescending(x => x.PendingSales).ToList();
        PendingBrandsGrid.ItemsSource = rows;
        PendingBrandSummary.Text = rows.Count == 0 ? "کد ناشناخته‌ای در فروش‌ها وجود ندارد." : $"{rows.Count} کد کالا در {rows.Sum(x => x.PendingCount)} فروش معلق است. برای تعیین برند، روی ردیف دوبار کلیک کنید.";
    }

    void PendingBrandsGrid_MouseDoubleClick(object s, MouseButtonEventArgs e)
    {
        if (PendingBrandsGrid.SelectedItem is not UnknownCodeRow row || !CanEdit()) return;
        var dialog = new BrandAssignmentDialog(row.Item, ledger.Brands) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value == null) return;
        Guard(() =>
        {
            var brand = dialog.Value;
            var brands = ledger.Brands.Any(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(brand.Name), StringComparison.OrdinalIgnoreCase))
                ? ledger.Brands.Select(x => Rules.Normalize(x.Name).Equals(Rules.Normalize(brand.Name), StringComparison.OrdinalIgnoreCase) ? brand : x).ToList()
                : ledger.Brands.Append(brand).ToList();
            BrandPrefixRules.ValidateUnique(brands);
            var prefix = Rules.Digits(row.Item.Code); prefix = prefix[..Math.Min(3, prefix.Length)];
            var affectedCodes = ledger.Items.Where(x => string.IsNullOrWhiteSpace(x.Brand) && Rules.Digits(x.Code).StartsWith(prefix, StringComparison.Ordinal)).Select(x => x.Code).ToList();
            var items = ledger.Items.Select(x => affectedCodes.Contains(x.Code, StringComparer.OrdinalIgnoreCase) ? x with { Brand = brand.Name } : x).ToList();
            var next = ledger with { Brands = brands, Items = items };
            foreach (var code in affectedCodes) next = BrandMonthRules.ApplyPendingSalesForItem(next, code);
            SaveLedger(next, $"برند «{brand.Name}» برای {affectedCodes.Count} کد ناشناخته با پیشوند {prefix} ثبت شد.");
        });
    }

    void HistoryFilterChanged(object s, SelectionChangedEventArgs e) { if (!loading) DrawHistory(); }
    void ClearHistoryFilter(object s, RoutedEventArgs e) { loading = true; HistoryFrom.SelectedItem = null; HistoryTo.SelectedItem = null; loading = false; DrawHistory(); }
    void DrawHistory()
    {
        var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        if (from != null && to != null && string.CompareOrdinal(from, to) > 0) { reportMonths = []; reportTotals = []; HistoryGrid.ItemsSource = Array.Empty<HistoryRow>(); DrawTrend(); return; }
        reportMonths = store.Keys().Where(x => (from == null || x.CompareTo(from) >= 0) && (to == null || x.CompareTo(to) <= 0)).OrderBy(x => x).Select(store.Load).ToList();
        reportTotals = reportMonths.Select(x => LedgerCalculator.SummarizeMonth(ledger, x, calculation)).ToList();
        HistoryGrid.ItemsSource = reportMonths.Zip(reportTotals).OrderByDescending(x => x.First.Key).Select(x => new HistoryRow(x.First, x.Second)).ToList();
        var sales = reportTotals.Sum(x => x.Sales); var profit = reportTotals.Sum(x => x.Profit); var net = reportTotals.Sum(x => x.Net);
        RangeSales.Text = Rules.ReportMoney(sales); RangeProfit.Text = Rules.ReportMoney(profit); RangeNet.Text = Rules.ReportMoney(net); RangeNet.Foreground = net < 0 ? Brushes.Firebrick : net > 0 ? Brushes.SeaGreen : Brushes.SlateGray; RangeOutcome.Text = Outcome(net) + " نهایی";
        DrawTrend();
    }
    void TrendFilterChanged(object s, RoutedEventArgs e) => DrawTrend();
    void TrendSizeChanged(object s, SizeChangedEventArgs e) => DrawTrend();
    void DrawTrend()
    {
        if (TrendCanvas == null) return;
        TrendCanvas.Children.Clear(); TrendEmpty.Visibility = reportMonths.Count < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (reportMonths.Count < 2 || TrendCanvas.ActualWidth < 100 || TrendCanvas.ActualHeight < 80) return;
        var series = new List<(Brush Brush, Func<LedgerTotal, decimal> Get)>(); if (TrendNet.IsChecked == true) series.Add((Brushes.Firebrick, x => x.Net)); if (TrendProfit.IsChecked == true) series.Add((Brushes.SeaGreen, x => x.Profit)); if (TrendSales.IsChecked == true) series.Add((Brushes.SteelBlue, x => x.Sales)); if (series.Count == 0) return;
        var values = series.SelectMany(x => reportTotals.Select(x.Get)).ToList(); var min = Math.Min(0, values.Min()); var max = Math.Max(0, values.Max()); if (min == max) { min--; max++; }
        var width = TrendCanvas.ActualWidth; var height = TrendCanvas.ActualHeight; var pad = 30d; double X(int i) => pad + (width - pad * 2) * i / (reportMonths.Count - 1); double Y(decimal value) => 16 + (double)((max - value) / (max - min)) * (height - 48);
        TrendCanvas.Children.Add(new System.Windows.Shapes.Line { X1 = pad, X2 = width - pad, Y1 = Y(0), Y2 = Y(0), Stroke = Brushes.LightGray });
        foreach (var item in series) { var line = new System.Windows.Shapes.Polyline { Stroke = item.Brush, StrokeThickness = 2.5 }; for (var i = 0; i < reportTotals.Count; i++) line.Points.Add(new Point(X(i), Y(item.Get(reportTotals[i])))); TrendCanvas.Children.Add(line); }
        for (var i = 0; i < reportMonths.Count; i++) { var label = new TextBlock { Text = reportMonths[i].Key, FontSize = 10, Foreground = Brushes.DimGray, FlowDirection = FlowDirection.LeftToRight }; Canvas.SetLeft(label, X(i) - 24); Canvas.SetTop(label, height - 22); TrendCanvas.Children.Add(label); }
    }
    void PrintReport(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        Guard(() => { var print = new PrintDialog(); if (print.ShowDialog() != true) return; var total = LedgerCalculator.SummarizeMonth(ledger, current, calculation); var doc = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("pack://application:,,,/Assets/Fonts/#Vazirmatn"), FontSize = 11, PagePadding = new Thickness(35), ColumnWidth = double.PositiveInfinity, PageWidth = print.PrintableAreaWidth }; doc.Blocks.Add(new Paragraph(new Run("گزارش سود فروش")) { FontSize = 21, FontWeight = FontWeights.Bold }); doc.Blocks.Add(new Paragraph(new Run($"ماه {current.Key} — فروش واقعی: {Rules.ReportMoney(total.Sales)} ریال — هزینه تخمینی: {Rules.ReportMoney(total.Cost)} ریال — سود خالص فروش: {Rules.ReportMoney(total.Profit)} ریال — نتیجه: {Rules.ReportMoney(total.Net)} ریال"))); print.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Monthly Profit " + current.Key); });
    }
    void ExportExcel(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var file = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = "MonthlyProfit-" + current.Key.Replace('/', '-') + ".xlsx" };
        if (file.ShowDialog(this) == true) Guard(() => { ExcelTransfer.ExportLedgerMonth(ledger, current, file.FileName); Status.Text = "خروجی اکسل ماه فعال ذخیره شد."; });
    }
    void ExportRange(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit() || reportMonths.Count == 0) { MessageBox.Show(this, "ابتدا یک بازه معتبر انتخاب کنید."); return; }
        var file = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = $"MonthlyProfit-Range-{reportMonths.First().Key.Replace('/', '-')}-{reportMonths.Last().Key.Replace('/', '-')}.xlsx" };
        if (file.ShowDialog(this) == true) Guard(() => { ExcelTransfer.ExportLedgerRange(ledger, reportMonths, file.FileName); Status.Text = "خروجی اکسل بازه ذخیره شد."; });
    }
    void Backup(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var file = new SaveFileDialog { Filter = "Monthly Profit Backup|*.sqlite", FileName = "MonthlyProfit-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sqlite" };
        if (file.ShowDialog(this) == true) Guard(() => { store.Backup(file.FileName); preferences = preferences with { LastBackupUtc = DateTime.UtcNow, LastBackupPath = file.FileName }; store.SavePreferences(preferences); DrawBackupStatus(); Status.Text = "پشتیبان ذخیره شد."; });
    }
    void Restore(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var file = new OpenFileDialog { Filter = "Monthly Profit Backup|*.sqlite" }; if (file.ShowDialog(this) != true) return;
        Guard(() => { Store.CheckBackup(file.FileName); if (MessageBox.Show(this, "همه اطلاعات فعلی جایگزین شوند؟", "بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; store.Restore(file.FileName); Reload(); Status.Text = "بازیابی انجام شد."; });
    }
    void OpenBackupFolder(object s, RoutedEventArgs e) => Guard(() => { var path = Path.Combine(DataDirectory, "backups"); Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); });
    void AutoBackupChanged(object s, RoutedEventArgs e) { if (!loading) Guard(() => { preferences = preferences with { AutoBackupOnExit = AutoBackup.IsChecked == true }; store.SavePreferences(preferences); DrawBackupStatus(); }); }
    void DrawBackupStatus()
    {
        AutoBackupState.Text = preferences.AutoBackupOnExit ? "زمان اجرا: هنگام خروج از برنامه" : "زمان اجرا: در حال حاضر غیرفعال است";
        var auto = store.LastAutomaticBackup(); LastAutoBackup.Text = auto == null ? "آخرین پشتیبان خودکار: هنوز نسخه‌ای ایجاد نشده است." : "آخرین پشتیبان خودکار: " + File.GetLastWriteTime(auto).ToString("yyyy/MM/dd HH:mm");
        LastManualBackup.Text = preferences.LastBackupUtc == null ? "هنوز یک پشتیبان دستی ایجاد نشده است." : "آخرین پشتیبان دستی: " + preferences.LastBackupUtc.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    }
    void CopyVersion(object s, RoutedEventArgs e) { Clipboard.SetText("شرکت متحد توزیع ایرانیان | سامانه مدیریت سود فروش | نسخه ۰٫۶٫۰"); Status.Text = "اطلاعات نسخه کپی شد."; }
}

public sealed class SaleGridRow(Sale sale, CatalogItem item, SaleSettlement? settlement)
{
    public Sale Value => sale; public SaleSettlement? Settlement => settlement;
    public string Date => sale.Date; public string Customer => sale.Customer; public string Code => sale.Code; public string Brand => string.IsNullOrWhiteSpace(item.Brand) ? "تعیین‌نشده" : item.Brand; public string Name => item.Name;
    public string QuantityText => Rules.Money(sale.Quantity);
    public string InvoiceNetText => Rules.ReportMoney(Rules.NetSaleBase(sale));
    public string CashDiscountText => settlement == null ? "—" : Rules.ReportMoney(Rules.CashDiscountAmount(sale));
    public string SalesText => settlement == null ? "—" : Rules.ReportMoney(settlement.Sales);
    public string CostText => settlement == null ? "—" : Rules.ReportMoney(settlement.Cost);
    public string ProfitText => settlement == null ? "—" : Rules.ReportMoney(settlement.Profit);
    public string Status => settlement == null ? "در انتظار تعیین برند یا تأیید ماه" : settlement.HasManualCost ? "هزینه دستی" : "محاسبه‌شده";
}
public sealed class UnknownCodeRow(CatalogItem item, int pendingCount, decimal pendingSales)
{
    public CatalogItem Item => item; public string Code => item.Code; public string Name => item.Name; public int PendingCount => pendingCount; public decimal PendingSales => pendingSales;
    public string PendingSalesText => Rules.ReportMoney(pendingSales); public string Action => "تعیین برند";
}
public sealed class BrandSettingsRow(Brand brand, BrandRate rate)
{
    public Brand Brand => brand; public BrandRate Rate => rate;
    public string Name => brand.Name; public string Prefixes => BrandPrefixRules.Display(brand.CodePrefixes); public string PurchaseDiscountText => Rules.Percent(rate.PurchaseDiscount); public string OfferText => Rules.Percent(rate.Offer); public string MarkupText => Rules.Percent(rate.Markup); public string CashShareText => Rules.Percent(rate.CashShare); public string CreditShareText => Rules.Percent(rate.CreditShare); public string CashDiscountText => Rules.Percent(rate.CashDiscount);
}
public sealed class BrandRow(string brand, int count, decimal sales, decimal profit)
{
    public int Rank { get; set; } public string Brand => brand; public int Count => count; public decimal Profit => profit; public string SalesText => Rules.ReportMoney(sales); public string ProfitText => Rules.ReportMoney(profit); public string MarginText => Rules.Percent(sales == 0 ? null : profit / sales);
}
public sealed class HistoryRow(Month month, LedgerTotal total)
{
    public string Key => month.Key; public string CostText => Rules.ReportMoney(total.Cost); public string SalesText => Rules.ReportMoney(total.Sales); public string ProfitText => Rules.ReportMoney(total.Profit); public string FixedText => Rules.ReportMoney(total.FixedCost); public string NetText => Rules.ReportMoney(total.Net); public string OutcomeText => total.Net < 0 ? "زیان" : total.Net > 0 ? "سود" : "سر‌به‌سر";
}
