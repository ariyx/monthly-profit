using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class BrandMonthConfirmationDialog : Window
{
    sealed class RateRow
    {
        public string BrandName { get; set; } = "";
        public string PurchaseDiscount { get; set; } = "0";
        public string Offer { get; set; } = "0";
        public string Markup { get; set; } = "0";
        public string CashShare { get; set; } = "0";
        public string CreditShare { get; set; } = "0";
        public string CashDiscount { get; set; } = "0";
        public static RateRow From(BrandRate rate) => new() { BrandName = rate.BrandName, PurchaseDiscount = P(rate.PurchaseDiscount), Offer = P(rate.Offer), Markup = P(rate.Markup), CashShare = P(rate.CashShare), CreditShare = P(rate.CreditShare), CashDiscount = P(rate.CashDiscount) };
        public BrandRate ToValue() => new() { BrandName = BrandName, PurchaseDiscount = N(PurchaseDiscount), Offer = N(Offer), Markup = N(Markup), CashShare = N(CashShare), CreditShare = N(CreditShare), CashDiscount = N(CashDiscount) };
        static string P(decimal value) => (value * 100m).ToString("0.##", CultureInfo.InvariantCulture);
        static decimal N(string value) => Rules.Number(value) / 100m;
    }

    readonly List<RateRow> rows;
    public List<BrandRate>? Value { get; private set; }

    public BrandMonthConfirmationDialog(BrandMonthSettings draft, IEnumerable<string>? brandNames = null)
    {
        var selected = brandNames == null ? draft.Rates.Where(x => !x.IsConfirmed) : draft.Rates.Where(x => brandNames.Contains(x.BrandName, StringComparer.OrdinalIgnoreCase));
        rows = selected.OrderBy(x => x.BrandName, StringComparer.OrdinalIgnoreCase).Select(RateRow.From).ToList();
        if (rows.Count == 0) throw new InvalidOperationException("درصد تأییدنشده‌ای برای این ماه وجود ندارد.");
        var title = rows.Count == 1 ? $"تأیید درصدهای برند «{rows[0].BrandName}»" : "تأیید درصدهای برندها";
        Title = $"{title} در {draft.MonthKey}";
        Width = 1120; Height = 650; MinWidth = 860; MinHeight = 480; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header($"{title} در ماه {draft.MonthKey}", $"مقادیر از «{draft.SourceMonthKey}» برای شروع پر شده‌اند. تأیید فقط فروش‌های همین برند در همین ماه را محاسبه می‌کند."));
        var body = new Grid { Margin = new Thickness(22, 18, 22, 12) }; body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(body, 1); root.Children.Add(body);
        body.Children.Add(new TextBlock { Text = "همه اعداد درصد هستند. جمع سهم نقدی و چکی باید دقیقاً ۱۰۰٪ باشد.", Foreground = new SolidColorBrush(Color.FromRgb(84, 98, 124)), FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, Margin = new Thickness(0, 0, 0, 10) });
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = false, CanUserAddRows = false, CanUserDeleteRows = false, ItemsSource = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
        DataGridTextColumn Column(string header, string path, double width, bool readOnly = false) => new() { Header = header, Binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus }, Width = new DataGridLength(width, DataGridLengthUnitType.Star), IsReadOnly = readOnly };
        grid.Columns.Add(Column("برند", nameof(RateRow.BrandName), 1.45, true)); grid.Columns.Add(Column("تخفیف خرید ٪", nameof(RateRow.PurchaseDiscount), 1)); grid.Columns.Add(Column("آفر ٪", nameof(RateRow.Offer), .75)); grid.Columns.Add(Column("مارک‌آپ ٪", nameof(RateRow.Markup), .85)); grid.Columns.Add(Column("سهم نقدی ٪", nameof(RateRow.CashShare), .9)); grid.Columns.Add(Column("سهم چکی ٪", nameof(RateRow.CreditShare), .9)); grid.Columns.Add(Column("تخفیف نقدی ٪", nameof(RateRow.CashDiscount), 1));
        Grid.SetRow(grid, 1); body.Children.Add(grid);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(error, 2); body.Children.Add(error);
        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 0, 22, 18) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var confirm = new Button { Content = $"تأیید تنظیمات {draft.MonthKey}", Style = (Style)FindResource("Primary") }; footer.Children.Add(confirm); footer.Children.Add(new Button { Content = "فعلاً تأیید نمی‌کنم", IsCancel = true });
        confirm.Click += (_, _) => { try { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); var values = rows.Select(x => x.ToValue()).ToList(); foreach (var value in values) BrandMonthRules.Validate(value); Value = values; DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
}
