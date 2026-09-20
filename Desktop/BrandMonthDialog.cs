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

        public static RateRow From(BrandRate rate) => new()
        {
            BrandName = rate.BrandName,
            PurchaseDiscount = Percent(rate.PurchaseDiscount),
            Offer = Percent(rate.Offer),
            Markup = Percent(rate.Markup),
            CashShare = Percent(rate.CashShare),
            CreditShare = Percent(rate.CreditShare),
            CashDiscount = Percent(rate.CashDiscount)
        };

        public BrandRate ToValue() => new()
        {
            BrandName = BrandName,
            PurchaseDiscount = Number(PurchaseDiscount),
            Offer = Number(Offer),
            Markup = Number(Markup),
            CashShare = Number(CashShare),
            CreditShare = Number(CreditShare),
            CashDiscount = Number(CashDiscount)
        };

        static string Percent(decimal value) => (value * 100m).ToString("0.##", CultureInfo.InvariantCulture);
        static decimal Number(string value) => Rules.Number(value) / 100m;
    }

    readonly List<RateRow> rows;
    public List<BrandRate>? Value { get; private set; }

    public BrandMonthConfirmationDialog(BrandMonthSettings draft)
    {
        rows = draft.Rates.OrderBy(x => x.BrandName, StringComparer.OrdinalIgnoreCase).Select(RateRow.From).ToList();
        Title = $"تأیید تنظیمات برندهای {draft.MonthKey}";
        Width = 1120; Height = 650; MinWidth = 920; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;
        root.Children.Add(DialogUi.Header($"تأیید درصدهای برند در ماه {draft.MonthKey}",
            $"مقادیر از «{draft.SourceMonthKey}» برای شروع پر شده‌اند. آن‌ها را بررسی یا اصلاح کنید؛ تأیید مجدد فقط Snapshot تراکنش‌های همین ماه را هماهنگ می‌کند و ماه‌های دیگر تغییر نمی‌کنند."));

        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 12), FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(body, 1); root.Children.Add(body);
        body.Children.Add(new TextBlock
        {
            Text = "همه اعداد درصد هستند. جمع سهم نقدی و چکی باید دقیقاً ۱۰۰٪ باشد.",
            Foreground = new SolidColorBrush(Color.FromRgb(84, 98, 124)),
            Margin = new Thickness(0, 0, 0, 10), TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft
        });

        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = false, CanUserAddRows = false, CanUserDeleteRows = false, ItemsSource = rows, MinHeight = 320 };
        DataGridTextColumn Column(string header, string path, double width, bool readOnly = false) => new()
        {
            Header = header,
            Binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus },
            Width = new DataGridLength(width, DataGridLengthUnitType.Star),
            IsReadOnly = readOnly
        };
        grid.Columns.Add(Column("برند", nameof(RateRow.BrandName), 1.45, true));
        grid.Columns.Add(Column("تخفیف خرید ٪", nameof(RateRow.PurchaseDiscount), 1));
        grid.Columns.Add(Column("آفر ٪", nameof(RateRow.Offer), .75));
        grid.Columns.Add(Column("مارک‌آپ ٪", nameof(RateRow.Markup), .85));
        grid.Columns.Add(Column("سهم نقدی ٪", nameof(RateRow.CashShare), .9));
        grid.Columns.Add(Column("سهم چکی ٪", nameof(RateRow.CreditShare), .9));
        grid.Columns.Add(Column("تخفیف نقدی ٪", nameof(RateRow.CashDiscount), 1));
        body.Children.Add(grid);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 10, 0, 0) };
        body.Children.Add(error);

        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 0, 22, 18) };
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        var confirm = new Button { Content = $"تأیید تنظیمات {draft.MonthKey}", Style = (Style)FindResource("Primary") };
        var cancel = new Button { Content = "فعلاً تأیید نمی‌کنم" };
        footer.Children.Add(confirm); footer.Children.Add(cancel);
        confirm.Click += (_, _) =>
        {
            try
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
                var values = rows.Select(x => x.ToValue()).ToList();
                foreach (var value in values) BrandMonthRules.Validate(value);
                Value = values; DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        cancel.Click += (_, _) => DialogResult = false;
    }
}
