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
    Month current = new();
    Product? deleted;
    bool loading;
    string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonthlyProfit", "Data");
    public MainWindow()
    {
        InitializeComponent(); store = new Store(Path.Combine(DataDirectory, "monthly-profit.sqlite"));
        Fixed.LostFocus += (_, _) => FormatMoney(Fixed);
        if (store.Keys().Count == 0) { var pc = new PersianCalendar(); var now = DateTime.Today; store.Save(new Month { Key = $"{pc.GetYear(now):0000}/{pc.GetMonth(now):00}" }); }
        Reload();
        Closing += (_, e) => { if (!ResolveFixedEdit()) e.Cancel = true; };
    }
    void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "عملیات انجام نشد", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    void Reload(string? key = null)
    {
        loading = true; var keys = store.Keys(); var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        Months.ItemsSource = keys; Months.SelectedItem = key != null && keys.Contains(key) ? key : keys.FirstOrDefault();
        HistoryFrom.ItemsSource = keys; HistoryTo.ItemsSource = keys;
        HistoryFrom.SelectedItem = from != null && keys.Contains(from) ? from : null;
        HistoryTo.SelectedItem = to != null && keys.Contains(to) ? to : null;
        loading = false;
        if (Months.SelectedItem is string k) { current = store.Load(k); deleted = null; Draw(); }
    }
    bool ResolveFixedEdit()
    {
        if (current.Revision == 0 || Fixed.Text == Rules.Money(current.FixedCost)) return true;
        var choice = MessageBox.Show(this, "هزینه ثابت ویرایش شده است. ذخیره شود؟", "تغییر ذخیره‌نشده", MessageBoxButton.YesNoCancel);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.No) { Fixed.Text = Rules.Money(current.FixedCost); return true; }
        try { var m = current with { FixedCost = Rules.Number(Fixed.Text) }; store.Save(m); current = m; Draw(); return true; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); return false; }
    }
    void MonthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || Months.SelectedItem is not string key || key == current.Key) return;
        if (!ResolveFixedEdit()) { loading = true; Months.SelectedItem = current.Key; loading = false; return; }
        Guard(() => { current = store.Load(key); deleted = null; Draw(); });
    }
    void Draw()
    {
        var t = Rules.Summarize(current);
        Sales.Text = Rules.Money(t.Sales); Profit.Text = Rules.Money(t.Profit); Net.Text = Rules.Money(t.Net);
        Net.Foreground = t.Net < 0 ? Brushes.Firebrick : t.Net > 0 ? Brushes.SeaGreen : Brushes.SlateGray;
        NetLabel.Text = t.Net < 0 ? "زیان ماه" : t.Net > 0 ? "سود ماه" : "پوشش هزینه ثابت";
        NetLabel.Foreground = t.Net < 0 ? Brushes.Firebrick : t.Net > 0 ? Brushes.SeaGreen : Brushes.SlateGray;
        MarginLabel.Text = "حاشیه سود کل: " + Rules.Percent(t.Margin);
        Fixed.Text = Rules.Money(current.FixedCost);
        Breakdown.Text = $"بهای تمام‌شده خرید: {Rules.Money(t.Cost)} ریال\nفروش نقدی: {Rules.Money(t.Cash)} ریال\nفروش چکی: {Rules.Money(t.Credit)} ریال\nبرند: {t.Brands}   |   کالا: {t.Products}";
        FixedHint.Text = t.Net < 0 ? "کسری برای پوشش هزینه ثابت: " + Rules.Money(-t.Net) + " ریال" : "هزینه ثابت پوشش داده شده است.";
        ItemsGrid.ItemsSource = current.Products.Select(p => new ProductRow(p)).ToList();
        var groups = current.Products.GroupBy(p => Rules.Normalize(p.Brand)).Select(g => new BrandRow(g.Key, g.Count(), g.Sum(p => Rules.Calculate(p).Sales), g.Sum(p => Rules.Calculate(p).Profit))).OrderByDescending(g => g.Profit).ToList();
        foreach (var g in groups) g.Rank = 1 + groups.Count(x => x.Profit > g.Profit);
        BrandsGrid.ItemsSource = groups;
        DrawHistory();
        Status.Text = $"ماه {current.Key} · تغییرات ثبت‌شده · نسخه پرونده {current.Revision}";
    }
    void Save(Month next)
    {
        store.Save(next); current = next; Draw();
    }
    void FormatMoney(TextBox box)
    {
        try { if (!string.IsNullOrWhiteSpace(box.Text)) box.Text = Rules.Money(Rules.Number(box.Text)); }
        catch { /* خطا در اعتبارسنجی هنگام ثبت نمایش داده می‌شود. */ }
    }
    void CreateMonth(bool clone)
    {
        if (!ResolveFixedEdit()) return;
        var parts = current.Key.Split('/'); int y = int.Parse(parts[0]), m = int.Parse(parts[1]) + 1; if (m == 13) { y++; m = 1; }
        var dialog = new MonthDialog($"{y:0000}/{m:00}", clone) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Guard(() => { if (store.Keys().Contains(dialog.Key)) throw new InvalidDataException("این ماه قبلاً ایجاد شده است."); var next = clone ? current.CopyTo(dialog.Key) : new Month { Key = dialog.Key, FixedCost = current.FixedCost }; store.Save(next); Reload(next.Key); });
    }
    void NewMonth(object s, RoutedEventArgs e) => CreateMonth(false);
    void CloneMonth(object s, RoutedEventArgs e) => CreateMonth(true);
    void EditMonth(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var dialog = new MonthDialog(current.Key, false, true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Key == current.Key) return;
        Guard(() => {
            var safety = store.CreateSafetyBackup("before-rename");
            var renamed = store.Rename(current, dialog.Key); Reload(renamed.Key);
            Status.Text = "ماه ویرایش شد. پشتیبان ایمنی: " + safety;
        });
    }
    void DeleteMonth(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        if (store.Keys().Count <= 1) { MessageBox.Show(this, "آخرین ماه قابل حذف نیست. ابتدا یک ماه جدید ایجاد کنید.", "حذف ماه", MessageBoxButton.OK, MessageBoxImage.Information); return; }
        var total = Rules.Summarize(current);
        var prompt = $"ماه «{current.Key}» با {current.Products.Count} کالا و هزینه ثابت {Rules.Money(total.FixedCost)} ریال حذف شود؟\nاین کار فقط با بازیابی پشتیبان قابل برگشت است.";
        if (MessageBox.Show(this, prompt, "حذف ماه", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Guard(() => {
            var safety = store.CreateSafetyBackup("before-delete"); store.Delete(current); Reload();
            Status.Text = "ماه حذف شد. پشتیبان ایمنی: " + safety;
        });
    }
    void SaveFixed(object s, RoutedEventArgs e) => Guard(() => Save(current with { FixedCost = Rules.Number(Fixed.Text) }));
    void AddProduct(object s, RoutedEventArgs e) => OpenEditor(null);
    void EditProduct(object s, RoutedEventArgs e) { if (ItemsGrid.SelectedItem is ProductRow r) OpenEditor(r.Product); }
    void OpenEditor(Product? product)
    {
        if (!ResolveFixedEdit()) return;
        var dialog = new ProductDialog(product, current.Products) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Value is null) return;
        Guard(() => { var list = current.Products.ToList(); var i = list.FindIndex(x => x.Id == dialog.Value.Id); if (i < 0) list.Add(dialog.Value); else list[i] = dialog.Value; Save(current with { Products = list }); });
    }
    void DeleteProduct(object s, RoutedEventArgs e)
    {
        if (ItemsGrid.SelectedItem is not ProductRow row || !ResolveFixedEdit()) return;
        if (MessageBox.Show(this, "کالای «" + row.Name + "» از همین ماه حذف شود؟", "حذف کالا", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Guard(() => { Save(current with { Products = current.Products.Where(x => x.Id != row.Product.Id).ToList() }); deleted = row.Product; });
    }
    void UndoDelete(object s, RoutedEventArgs e)
    {
        if (deleted == null) { MessageBox.Show(this, "حذفی برای بازگردانی در این ماه وجود ندارد."); return; }
        if (!ResolveFixedEdit()) return;
        Guard(() => { Save(current with { Products = [.. current.Products, deleted] }); deleted = null; });
    }
    void ImportExcel(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var dialog = new OpenFileDialog { Filter = "Excel workbook|*.xlsx", Title = "ورود کالاها به ماه " + current.Key };
        if (dialog.ShowDialog(this) != true) return;
        Guard(() => {
            var rows = ExcelTransfer.Import(dialog.FileName); if (rows.Count == 0) throw new InvalidDataException("کالایی برای ورود پیدا نشد.");
            var next = current with { Products = [.. current.Products, .. rows] }; Rules.Validate(next);
            if (MessageBox.Show(this, $"{rows.Count} کالا به ماه {current.Key} افزوده شود؟\nهزینه ثابت از فایل وارد نمی‌شود. نام کالاهای فایل اولیه به‌صورت موقت ساخته می‌شود.", "تأیید ورود", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Save(next);
        });
    }
    void ExportExcel(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var dialog = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = "MonthlyProfit-" + current.Key.Replace('/', '-') + ".xlsx" };
        if (dialog.ShowDialog(this) == true) Guard(() => { ExcelTransfer.Export(current, dialog.FileName); Status.Text = "گزارش اکسل ذخیره شد."; });
    }
    void Backup(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var dialog = new SaveFileDialog { Filter = "Monthly Profit Backup|*.sqlite", FileName = "MonthlyProfit-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".sqlite" };
        if (dialog.ShowDialog(this) == true) Guard(() => { store.Backup(dialog.FileName); Status.Text = "پشتیبان تمام ماه‌ها ذخیره شد."; });
    }
    void Restore(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        var dialog = new OpenFileDialog { Filter = "Monthly Profit Backup|*.sqlite" }; if (dialog.ShowDialog(this) != true) return;
        Guard(() => {
            Store.CheckBackup(dialog.FileName);
            if (MessageBox.Show(this, "همه ماه‌های فعلی با اطلاعات پشتیبان جایگزین شوند؟\nابتدا یک پشتیبان ایمنی از اطلاعات فعلی ذخیره می‌شود.", "بازیابی همه اطلاعات", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var safety = store.Restore(dialog.FileName);
            if (store.Keys().Count == 0) store.Save(new Month { Key = current.Key });
            Reload(); Status.Text = "بازیابی انجام شد. پشتیبان پیش از بازیابی: " + safety;
        });
    }
    void OpenDataFolder(object s, RoutedEventArgs e) => Guard(() => Process.Start(new ProcessStartInfo { FileName = DataDirectory, UseShellExecute = true }));
    void HistoryFilterChanged(object s, SelectionChangedEventArgs e) { if (!loading) DrawHistory(); }
    void ClearHistoryFilter(object s, RoutedEventArgs e)
    {
        loading = true; HistoryFrom.SelectedItem = null; HistoryTo.SelectedItem = null; loading = false; DrawHistory();
    }
    void DrawHistory()
    {
        var keys = store.Keys(); var from = HistoryFrom.SelectedItem as string; var to = HistoryTo.SelectedItem as string;
        if (from != null && to != null && string.CompareOrdinal(from, to) > 0)
        {
            HistoryGrid.ItemsSource = Array.Empty<HistoryRow>();
            HistorySummary.Text = "بازه ماه نامعتبر است: ماه آغاز باید پیش از ماه پایان باشد.";
            return;
        }
        var months = keys.Where(k => (from == null || string.CompareOrdinal(k, from) >= 0) && (to == null || string.CompareOrdinal(k, to) <= 0)).Select(store.Load).ToList();
        HistoryGrid.ItemsSource = months.Select(m => new HistoryRow(m)).ToList();
        var totals = months.Select(Rules.Summarize).ToList();
        var cost = totals.Sum(x => x.Cost); var sales = totals.Sum(x => x.Sales); var profit = totals.Sum(x => x.Profit); var fixedCost = totals.Sum(x => x.FixedCost); var net = totals.Sum(x => x.Net);
        HistorySummary.Text = months.Count == 0 ? "در این بازه ماهی ثبت نشده است." : $"{months.Count} ماه · بهای تمام‌شده: {Rules.Money(cost)} ریال · فروش: {Rules.Money(sales)} ریال · سود ناخالص: {Rules.Money(profit)} ریال · هزینه ثابت: {Rules.Money(fixedCost)} ریال · نتیجه: {Rules.Money(net)} ریال · حاشیه سود: {Rules.Percent(sales == 0 ? null : profit / sales)}";
    }
    void LoadSample(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        Guard(() => { if (current.Products.Count != 0) throw new InvalidDataException("نمونه فقط در ماه خالی وارد می‌شود. ابتدا یک ماه خالی بسازید.");
            if (MessageBox.Show(this, "شش کالای نمونه با داده‌های اکسل اولیه وارد شوند؟", "داده نمونه", MessageBoxButton.YesNo) == MessageBoxResult.Yes) Save(Rules.Sample(current.Key) with { Revision = current.Revision, FixedCost = current.FixedCost }); });
    }
    void PrintReport(object s, RoutedEventArgs e)
    {
        if (!ResolveFixedEdit()) return;
        Guard(() => {
            var print = new PrintDialog(); if (print.ShowDialog() != true) return;
            var doc = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("pack://application:,,,/Assets/Fonts/#Vazirmatn"), FontSize = 11, PagePadding = new Thickness(35), ColumnWidth = double.PositiveInfinity, PageWidth = print.PrintableAreaWidth };
            var t = Rules.Summarize(current);
            doc.Blocks.Add(new Paragraph(new Run("متحد توزیع ایرانیان")) { FontSize = 21, FontWeight = FontWeights.Bold });
            doc.Blocks.Add(new Paragraph(new Run("گزارش ماه " + current.Key + " — همه مبلغ‌ها ریال")) { FontSize = 15 });
            doc.Blocks.Add(new Paragraph(new Run($"خرید: {Rules.Money(t.Cost)}\nفروش: {Rules.Money(t.Sales)}\nسود ناخالص: {Rules.Money(t.Profit)}\nهزینه ثابت: {Rules.Money(t.FixedCost)}\nنتیجه ماه: {Rules.Money(t.Net)}\nحاشیه سود کل: {Rules.Percent(t.Margin)}")));
            var table = new Table { CellSpacing = 0 }; for (int i = 0; i < 5; i++) table.Columns.Add(new TableColumn()); var body = new TableRowGroup(); table.RowGroups.Add(body);
            void Row(params string[] texts) { var row = new TableRow(); foreach (var text in texts) row.Cells.Add(new TableCell(new Paragraph(new Run(text))) { Padding = new Thickness(5), BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1) }); body.Rows.Add(row); }
            Row("برند / کالا", "مقدار", "فروش", "سود", "حاشیه"); foreach (var p in current.Products) { var r = Rules.Calculate(p); Row(p.Brand + " / " + p.Name, Rules.Money(p.Quantity), Rules.Money(r.Sales), Rules.Money(r.Profit), Rules.Percent(r.Margin)); }
            doc.Blocks.Add(table); print.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Mottahed Tovzie Iranian " + current.Key);
        });
    }
}
public sealed class ProductRow(Product p)
{
    public Product Product => p;
    public string Brand => p.Brand; public string Name => p.Name;
    public string PriceText => Rules.Money(p.Price); public string QuantityText => Rules.Money(p.Quantity);
    public string CostText => Rules.Money(Rules.Calculate(p).Cost);
    public string SalesText => Rules.Money(Rules.Calculate(p).Sales); public string ProfitText => Rules.Money(Rules.Calculate(p).Profit); public string MarginText => Rules.Percent(Rules.Calculate(p).Margin);
}
public sealed class BrandRow(string brand, int count, decimal sales, decimal profit)
{
    public int Rank { get; set; } public string Brand => brand; public int Count => count; public decimal Profit => profit;
    public string SalesText => Rules.Money(sales); public string ProfitText => Rules.Money(profit); public string MarginText => Rules.Percent(sales == 0 ? null : profit / sales);
}
public sealed class HistoryRow(Month m)
{
    readonly Total total = Rules.Summarize(m);
    public string Key => m.Key; public string CostText => Rules.Money(total.Cost); public string SalesText => Rules.Money(total.Sales); public string ProfitText => Rules.Money(total.Profit); public string FixedText => Rules.Money(total.FixedCost); public string NetText => Rules.Money(total.Net);
}
