using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using Profit.Core;

namespace Profit.Desktop;

public partial class MainWindow : Window
{
    readonly Store store;
    Month current = new(); Product? deleted; bool loading;
    Preferences preferences = new(); List<Month> reportMonths = [];
    string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonthlyProfit", "Data");
    public MainWindow()
    {
        InitializeComponent(); store = new Store(Path.Combine(DataDirectory, "monthly-profit.sqlite")); MoneyInput.Attach(Fixed);
        if (store.Keys().Count == 0) { var pc = new PersianCalendar(); var d = DateTime.Today; store.Save(new Month { Key = $"{pc.GetYear(d):0000}/{pc.GetMonth(d):00}" }); }
        preferences = store.LoadPreferences(); loading = true; AutoBackup.IsChecked = preferences.AutoBackupOnExit; loading = false;
        AboutVersion.Text = "نسخه برنامه ۰٫۲ · قواعد محاسبات ۱ · داده‌ها فقط محلی هستند.";
        Reload(); Closing += OnClosing;
    }
    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ResolveFixedEdit()) { e.Cancel = true; return; }
        if (preferences.AutoBackupOnExit) Guard(() => { var file = store.CreateAutomaticBackup(preferences.AutoBackupKeep); preferences = preferences with { LastAutoBackupUtc = DateTime.UtcNow }; store.SavePreferences(preferences); Status.Text = "پشتیبان خودکار ذخیره شد: " + file; });
    }
    void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "عملیات انجام نشد", MessageBoxButton.OK, MessageBoxImage.Warning); } }
    void Reload(string? key = null)
    {
        loading = true; var keys = store.Keys(); var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        Months.ItemsSource = keys; Months.SelectedItem = key != null && keys.Contains(key) ? key : keys.FirstOrDefault();
        HistoryFrom.ItemsSource = keys; HistoryTo.ItemsSource = keys; HistoryFrom.SelectedItem = from != null && keys.Contains(from) ? from : null; HistoryTo.SelectedItem = to != null && keys.Contains(to) ? to : null; loading = false;
        if (Months.SelectedItem is string k) { current = store.Load(k); deleted = null; Draw(); }
    }
    bool CanEdit()
    {
        if (!current.IsClosed) return true;
        MessageBox.Show(this, "این ماه بسته است. از «مدیریت ماه» آن را باز کنید.", "ماه بسته", MessageBoxButton.OK, MessageBoxImage.Information); return false;
    }
    bool ResolveFixedEdit()
    {
        if (current.Revision == 0 || Fixed.Text == Rules.Money(current.FixedCost)) return true;
        if (!CanEdit()) { Fixed.Text = Rules.Money(current.FixedCost); return true; }
        var choice = MessageBox.Show(this, "هزینه ثابت ویرایش شده است. ذخیره شود؟", "تغییر ذخیره‌نشده", MessageBoxButton.YesNoCancel);
        if (choice == MessageBoxResult.Cancel) return false; if (choice == MessageBoxResult.No) { Fixed.Text = Rules.Money(current.FixedCost); return true; }
        try { Save(current with { FixedCost = Rules.Number(Fixed.Text), FixedExpenses = [] }, "هزینه ثابت ویرایش شد"); return true; } catch (Exception ex) { MessageBox.Show(this, ex.Message); return false; }
    }
    void MonthChanged(object s, SelectionChangedEventArgs e)
    {
        if (loading || Months.SelectedItem is not string key || key == current.Key) return;
        if (!ResolveFixedEdit()) { loading = true; Months.SelectedItem = current.Key; loading = false; return; }
        Guard(() => { current = store.Load(key); deleted = null; Draw(); });
    }
    void Draw()
    {
        var t = Rules.Summarize(current); Sales.Text = Rules.Money(t.Sales); Profit.Text = Rules.Money(t.Profit); Net.Text = Rules.Money(t.Net);
        var color = t.Net < 0 ? Brushes.Firebrick : t.Net > 0 ? Brushes.SeaGreen : Brushes.SlateGray; Net.Foreground = color; NetLabel.Text = Outcome(t.Net); NetLabel.Foreground = color;
        ClosedBadge.Text = current.IsClosed ? "ماه بسته" : "ماه باز"; ClosedBadge.Foreground = current.IsClosed ? Brushes.Firebrick : Brushes.SeaGreen;
        MarginLabel.Text = "حاشیه سود کل: " + Rules.Percent(t.Margin); Fixed.Text = Rules.Money(current.FixedCost);
        Breakdown.Text = $"بهای تمام‌شده خرید: {Rules.Money(t.Cost)} ریال\nفروش نقدی: {Rules.Money(t.Cash)} ریال\nفروش چکی: {Rules.Money(t.Credit)} ریال\nبرند: {t.Brands}   |   کالا: {t.Products}";
        DrawItems(); DrawBrands(); DrawHistory(); DrawBackupStatus(); Status.Text = $"ماه {current.Key} · نسخه پرونده {current.Revision}";
    }
    static string Outcome(decimal net) => net < 0 ? "زیان" : net > 0 ? "سود" : "سر‌به‌سر";
    void Save(Month next, string action)
    {
        if (!CanEdit()) return; next = next with { Audit = [.. next.Audit.TakeLast(499), new AuditEntry { Action = action }] }; store.Save(next); current = next; Draw();
    }
    void OpenMonthMenu(object s, RoutedEventArgs e) { if (s is Button b && b.ContextMenu != null) { b.ContextMenu.PlacementTarget = b; b.ContextMenu.IsOpen = true; } }
    void CreateMonth(bool clone)
    {
        if (!ResolveFixedEdit()) return; var parts = current.Key.Split('/'); var y = int.Parse(parts[0]); var m = int.Parse(parts[1]) + 1; if (m == 13) { y++; m = 1; }
        var d = new MonthDialog($"{y:0000}/{m:00}", clone) { Owner = this }; if (d.ShowDialog() != true) return;
        Guard(() => { if (store.Keys().Contains(d.Key)) throw new InvalidDataException("این ماه قبلاً ایجاد شده است."); var next = clone ? current.CopyTo(d.Key) : new Month { Key = d.Key, FixedCost = current.FixedCost, FixedExpenses = current.FixedExpenses.Select(x => x with { Id = Guid.NewGuid().ToString("N") }).ToList() }; store.Save(next); Reload(next.Key); });
    }
    void NewMonth(object s, RoutedEventArgs e) => CreateMonth(false); void CloneMonth(object s, RoutedEventArgs e) => CreateMonth(true);
    void EditMonth(object s, RoutedEventArgs e)
    {
        if (!CanEdit() || !ResolveFixedEdit()) return; var d = new MonthDialog(current.Key, false, true) { Owner = this }; if (d.ShowDialog() != true || d.Key == current.Key) return;
        Guard(() => { var backup = store.CreateSafetyBackup("before-rename"); var next = store.Rename(current, d.Key); Reload(next.Key); Status.Text = "ماه ویرایش شد. پشتیبان ایمنی: " + backup; });
    }
    void ToggleMonthClosed(object s, RoutedEventArgs e)
    {
        var target = !current.IsClosed; var text = target ? "ماه پس از بسته‌شدن قابل ویرایش نیست. ادامه می‌دهید؟" : "ماه دوباره قابل ویرایش می‌شود. ادامه می‌دهید؟";
        if (MessageBox.Show(this, text, target ? "بستن ماه" : "باز کردن ماه", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Guard(() => { current = store.SetClosed(current, target); Draw(); });
    }
    void DeleteMonth(object s, RoutedEventArgs e)
    {
        if (!CanEdit() || !ResolveFixedEdit()) return; if (store.Keys().Count <= 1) { MessageBox.Show(this, "آخرین ماه قابل حذف نیست."); return; }
        if (MessageBox.Show(this, $"ماه «{current.Key}» با {current.Products.Count} کالا حذف شود؟", "حذف ماه", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Guard(() => { var backup = store.CreateSafetyBackup("before-delete"); store.Delete(current); Reload(); Status.Text = "ماه حذف شد. پشتیبان: " + backup; });
    }
    void SaveFixed(object s, RoutedEventArgs e) => Guard(() => Save(current with { FixedCost = Rules.Number(Fixed.Text), FixedExpenses = [] }, "هزینه ثابت ویرایش شد"));
    void FixedExpenses(object s, RoutedEventArgs e)
    {
        if (!CanEdit()) return; var d = new FixedExpensesDialog(current) { Owner = this }; if (d.ShowDialog() != true || d.Value == null) return;
        Guard(() => Save(current with { FixedExpenses = d.Value, FixedCost = d.Value.Sum(x => x.Amount) }, "ریز هزینه‌های ثابت ویرایش شد"));
    }
    void AddProduct(object s, RoutedEventArgs e) => OpenEditor(null);
    void EditProduct(object s, RoutedEventArgs e) { if (ItemsGrid.SelectedItem is ProductRow r) OpenEditor(r.Product); }
    void OpenEditor(Product? p)
    {
        if (!CanEdit() || !ResolveFixedEdit()) return; var d = new ProductDialog(p, current.Products) { Owner = this }; if (d.ShowDialog() != true || d.Value == null) return;
        Guard(() => { var list = current.Products.ToList(); var i = list.FindIndex(x => x.Id == d.Value.Id); if (i < 0) list.Add(d.Value); else list[i] = d.Value; Save(current with { Products = list }, i < 0 ? "کالا افزوده شد" : "کالا ویرایش شد"); });
    }
    void DeleteProduct(object s, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not ProductRow row || !CanEdit() || !ResolveFixedEdit()) return;
        if (MessageBox.Show(this, "کالای «" + row.Name + "» حذف شود؟", "حذف کالا", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Guard(() => { Save(current with { Products = current.Products.Where(x => x.Id != row.Product.Id).ToList() }, "کالا حذف شد"); deleted = row.Product; });
    }
    void UndoDelete(object s, RoutedEventArgs e) { if (deleted == null || !CanEdit()) return; Guard(() => { Save(current with { Products = [.. current.Products, deleted] }, "حذف کالا بازگردانی شد"); deleted = null; }); }
    void ProductFilterChanged(object s, RoutedEventArgs e) { if (!loading) DrawItems(); }
    void ClearProductFilter(object s, RoutedEventArgs e) { loading = true; ProductSearch.Text = ""; BrandFilter.SelectedItem = "همه برندها"; loading = false; DrawItems(); }
    void ProductFilterChanged(object s, TextChangedEventArgs e) { if (!loading) DrawItems(); }
    void DrawItems()
    {
        var brands = current.Products.Select(x => x.Brand).Distinct().OrderBy(x => x).ToList(); var keep = BrandFilter.SelectedItem as string;
        loading = true; BrandFilter.ItemsSource = new[] { "همه برندها" }.Concat(brands).ToList(); BrandFilter.SelectedItem = brands.Contains(keep ?? "") ? keep : "همه برندها"; loading = false;
        var term = Rules.Normalize(ProductSearch.Text); var brand = BrandFilter.SelectedItem as string; var rows = current.Products.Where(p => (brand == "همه برندها" || brand == null || p.Brand == brand) && (string.IsNullOrWhiteSpace(term) || Rules.Normalize(p.Brand).Contains(term) || Rules.Normalize(p.Name).Contains(term))).Select(p => new ProductRow(p)).ToList();
        ItemsGrid.ItemsSource = rows; ItemsEmpty.Visibility = current.Products.Count == 0 ? Visibility.Visible : Visibility.Collapsed; ProductCount.Text = rows.Count == current.Products.Count ? $"{rows.Count} کالا در ماه فعال" : $"{rows.Count} کالا از {current.Products.Count} کالا";
    }
    void DrawBrands()
    {
        var groups = current.Products.GroupBy(p => Rules.Normalize(p.Brand)).Select(g => new BrandRow(g.Key, g.Count(), g.Sum(p => Rules.Calculate(p).Sales), g.Sum(p => Rules.Calculate(p).Profit))).OrderByDescending(g => g.Profit).ToList(); foreach (var g in groups) g.Rank = 1 + groups.Count(x => x.Profit > g.Profit); BrandsGrid.ItemsSource = groups;
    }
    void ImportExcel(object s, RoutedEventArgs e)
    {
        if (!CanEdit() || !ResolveFixedEdit()) return; var f = new OpenFileDialog { Filter = "Excel workbook|*.xlsx", Title = "ورود کالاها به ماه " + current.Key }; if (f.ShowDialog(this) != true) return;
        Guard(() => { var d = new ImportReviewDialog(ExcelTransfer.Review(f.FileName), current.Products) { Owner = this }; if (d.ShowDialog() == true && d.Products.Count > 0) Save(current with { Products = [.. current.Products, .. d.Products] }, "کالاها از Excel وارد شدند"); });
    }
    void ExportExcel(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return; var f = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = "MonthlyProfit-" + current.Key.Replace('/', '-') + ".xlsx" }; if (f.ShowDialog(this) == true) Guard(() => { ExcelTransfer.Export(current, f.FileName); Status.Text = "گزارش اکسل ذخیره شد."; });
    }
    void ExportRange(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit() || reportMonths.Count == 0) { MessageBox.Show(this, "ابتدا یک بازه معتبر انتخاب کنید."); return; }
        var f = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = $"MonthlyProfit-Range-{reportMonths.First().Key.Replace('/', '-')}-{reportMonths.Last().Key.Replace('/', '-')}.xlsx" };
        if (f.ShowDialog(this) == true) Guard(() => { ExcelTransfer.ExportRange(reportMonths, f.FileName); Status.Text = "خروجی بازه ذخیره شد."; });
    }
    void Backup(object s, RoutedEventArgs e) { if (!ResolveFixedEdit()) return; var f = new SaveFileDialog { Filter = "Monthly Profit Backup|*.sqlite", FileName = "MonthlyProfit-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sqlite" }; if (f.ShowDialog(this) == true) Guard(() => { store.Backup(f.FileName); preferences = preferences with { LastBackupUtc = DateTime.UtcNow, LastBackupPath = f.FileName }; store.SavePreferences(preferences); DrawBackupStatus(); Status.Text = "پشتیبان ذخیره شد."; }); }
    void Restore(object s, RoutedEventArgs e) { if (!ResolveFixedEdit()) return; var f = new OpenFileDialog { Filter = "Monthly Profit Backup|*.sqlite" }; if (f.ShowDialog(this) != true) return; Guard(() => { Store.CheckBackup(f.FileName); if (MessageBox.Show(this, "همه اطلاعات فعلی جایگزین شوند؟", "بازیابی", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return; var safety = store.Restore(f.FileName); Reload(); Status.Text = "بازیابی انجام شد. پشتیبان: " + safety; }); }
    void OpenDataFolder(object s, RoutedEventArgs e) => Guard(() => Process.Start(new ProcessStartInfo { FileName = DataDirectory, UseShellExecute = true }));
    void OpenBackupFolder(object s, RoutedEventArgs e) => Guard(() => { var p = Path.Combine(DataDirectory, "backups"); Directory.CreateDirectory(p); Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true }); });
    void AutoBackupChanged(object s, RoutedEventArgs e) { if (loading) return; Guard(() => { preferences = preferences with { AutoBackupOnExit = AutoBackup.IsChecked == true }; store.SavePreferences(preferences); DrawBackupStatus(); }); }
    void DrawBackupStatus()
    {
        var auto = store.LastAutomaticBackup();
        var enabled = preferences.AutoBackupOnExit;
        AutoBackupState.Text = enabled ? "فعال است · هنگام خروج از برنامه یک نسخهٔ امن ذخیره می‌شود." : "غیرفعال است · هنگام خروج نسخهٔ خودکار ساخته نمی‌شود.";
        AutoBackupState.Foreground = enabled ? Brushes.SeaGreen : Brushes.SlateGray;
        LastAutoBackup.Text = auto == null ? "آخرین پشتیبان خودکار: هنوز نسخه‌ای ایجاد نشده است." : "آخرین پشتیبان خودکار: " + File.GetLastWriteTime(auto).ToString("yyyy/MM/dd HH:mm");
        LastManualBackup.Text = preferences.LastBackupUtc == null ? "هنوز یک پشتیبان دستی ایجاد نشده است." : "آخرین پشتیبان دستی: " + preferences.LastBackupUtc.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
    }
    void HistoryFilterChanged(object s, SelectionChangedEventArgs e) { if (!loading) DrawHistory(); }
    void ClearHistoryFilter(object s, RoutedEventArgs e) { loading = true; HistoryFrom.SelectedItem = null; HistoryTo.SelectedItem = null; loading = false; DrawHistory(); }
    void DrawHistory()
    {
        var keys = store.Keys(); var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        if (from != null && to != null && string.CompareOrdinal(from, to) > 0) { reportMonths = []; HistoryGrid.ItemsSource = Array.Empty<HistoryRow>(); HistorySummary.Text = "بازه ماه نامعتبر است."; DrawTrend(); return; }
        reportMonths = keys.Where(k => (from == null || k.CompareTo(from) >= 0) && (to == null || k.CompareTo(to) <= 0)).OrderBy(k => k).Select(store.Load).ToList(); var totals = reportMonths.Select(Rules.Summarize).ToList();
        HistoryGrid.ItemsSource = reportMonths.OrderByDescending(x => x.Key).Select(x => new HistoryRow(x)).ToList(); var cost = totals.Sum(x => x.Cost); var sales = totals.Sum(x => x.Sales); var profit = totals.Sum(x => x.Profit); var fixedCost = totals.Sum(x => x.FixedCost); var net = totals.Sum(x => x.Net);
        RangeSales.Text = Rules.Money(sales); RangeProfit.Text = Rules.Money(profit); RangeNet.Text = Rules.Money(net); RangeNet.Foreground = net < 0 ? Brushes.Firebrick : net > 0 ? Brushes.SeaGreen : Brushes.SlateGray; RangeOutcome.Text = Outcome(net) + " نهایی";
        HistorySummary.Text = reportMonths.Count == 0 ? "در این بازه ماهی ثبت نشده است." : $"{reportMonths.Count} ماه · بهای تمام‌شده: {Rules.Money(cost)} ریال · هزینه ثابت: {Rules.Money(fixedCost)} ریال · حاشیه سود: {Rules.Percent(sales == 0 ? null : profit / sales)}"; DrawTrend();
    }
    void TrendFilterChanged(object s, RoutedEventArgs e) => DrawTrend(); void TrendSizeChanged(object s, SizeChangedEventArgs e) => DrawTrend();
    void DrawTrend()
    {
        if (TrendCanvas == null || TrendEmpty == null) return;
        TrendCanvas.Children.Clear(); if (reportMonths.Count < 2) { TrendEmpty.Visibility = Visibility.Visible; return; } TrendEmpty.Visibility = Visibility.Collapsed; var width = TrendCanvas.ActualWidth; var height = TrendCanvas.ActualHeight; if (width < 80 || height < 80) return;
        var selected = new List<(string Name, Brush Brush, Func<Total, decimal> Get)>(); if (TrendSales.IsChecked == true) selected.Add(("فروش", Brushes.SteelBlue, x => x.Sales)); if (TrendProfit.IsChecked == true) selected.Add(("سود", Brushes.SeaGreen, x => x.Profit)); if (TrendNet.IsChecked == true) selected.Add(("نتیجه", Brushes.Firebrick, x => x.Net)); if (selected.Count == 0) return;
        var totals = reportMonths.Select(Rules.Summarize).ToList(); var values = selected.SelectMany(s => totals.Select(s.Get)).ToList(); var min = Math.Min(0, values.Min()); var max = Math.Max(0, values.Max()); if (min == max) { min -= 1; max += 1; } var pad = 30d; var chartW = width - pad * 2; var chartH = height - 48;
        double X(int i) => pad + chartW * i / (reportMonths.Count - 1); double Y(decimal v) => 16 + (double)((max - v) / (max - min)) * chartH;
        var zero = new System.Windows.Shapes.Line { X1 = pad, X2 = width - pad, Y1 = Y(0), Y2 = Y(0), Stroke = Brushes.LightGray, StrokeThickness = 1 }; TrendCanvas.Children.Add(zero);
        foreach (var series in selected)
        {
            var line = new System.Windows.Shapes.Polyline { Stroke = series.Brush, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round }; for (var i = 0; i < totals.Count; i++) line.Points.Add(new Point(X(i), Y(series.Get(totals[i])))); TrendCanvas.Children.Add(line);
            for (var i = 0; i < totals.Count; i++) { var p = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = series.Brush, ToolTip = $"{reportMonths[i].Key}\n{series.Name}: {Rules.Money(series.Get(totals[i]))} ریال\nفروش: {Rules.Money(totals[i].Sales)} ریال\nسود ناخالص: {Rules.Money(totals[i].Profit)} ریال\nهزینه ثابت: {Rules.Money(totals[i].FixedCost)} ریال\nنتیجه: {Rules.Money(totals[i].Net)} ریال" }; Canvas.SetLeft(p, X(i) - 4); Canvas.SetTop(p, Y(series.Get(totals[i])) - 4); TrendCanvas.Children.Add(p); }
        }
        for (var i = 0; i < reportMonths.Count; i++) { var label = new TextBlock { Text = reportMonths[i].Key, FontSize = 10, Foreground = Brushes.DimGray }; Canvas.SetLeft(label, X(i) - 24); Canvas.SetTop(label, height - 22); TrendCanvas.Children.Add(label); }
    }
    void PrintReport(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return; Guard(() => { var print = new PrintDialog(); if (print.ShowDialog() != true) return; var doc = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("pack://application:,,,/Assets/Fonts/#Vazirmatn"), FontSize = 11, PagePadding = new Thickness(35), ColumnWidth = double.PositiveInfinity, PageWidth = print.PrintableAreaWidth }; var t = Rules.Summarize(current); doc.Blocks.Add(new Paragraph(new Run("شرکت متحد توزیع ایرانیان")) { FontSize = 21, FontWeight = FontWeights.Bold }); doc.Blocks.Add(new Paragraph(new Run($"گزارش ماه {current.Key} — فروش: {Rules.Money(t.Sales)} ریال — سود ناخالص: {Rules.Money(t.Profit)} ریال — نتیجه: {Rules.Money(t.Net)} ریال ({Outcome(t.Net)})"))); print.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Monthly Profit " + current.Key); });
    }
    void CopyVersion(object s, RoutedEventArgs e) { Clipboard.SetText("شرکت متحد توزیع ایرانیان | سامانه مدیریت سود ماهانه | نسخه ۰٫۲ | قواعد محاسبات ۱"); Status.Text = "اطلاعات نسخه کپی شد."; }
}
public sealed class ProductRow(Product p) { public Product Product => p; public string Brand => p.Brand; public string Name => p.Name; public string PriceText => Rules.Money(p.Price); public string QuantityText => Rules.Money(p.Quantity); public string CostText => Rules.Money(Rules.Calculate(p).Cost); public string SalesText => Rules.Money(Rules.Calculate(p).Sales); public string ProfitText => Rules.Money(Rules.Calculate(p).Profit); public string MarginText => Rules.Percent(Rules.Calculate(p).Margin); }
public sealed class BrandRow(string brand, int count, decimal sales, decimal profit) { public int Rank { get; set; } public string Brand => brand; public int Count => count; public decimal Profit => profit; public string SalesText => Rules.Money(sales); public string ProfitText => Rules.Money(profit); public string MarginText => Rules.Percent(sales == 0 ? null : profit / sales); }
public sealed class HistoryRow(Month m) { readonly Total total = Rules.Summarize(m); public string Key => m.Key; public string CostText => Rules.Money(total.Cost); public string SalesText => Rules.Money(total.Sales); public string ProfitText => Rules.Money(total.Profit); public string FixedText => Rules.Money(total.FixedCost); public string NetText => Rules.Money(total.Net); public string OutcomeText => total.Net < 0 ? "زیان" : total.Net > 0 ? "سود" : "سر‌به‌سر"; }
