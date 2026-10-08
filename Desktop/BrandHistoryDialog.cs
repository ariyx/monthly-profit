using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class BrandHistoryDialog : Window
{
    public sealed record HistoryRow(BrandChangeEntry Entry)
    {
        public string RecordedAt
        {
            get
            {
                var local = Entry.AtUtc.ToLocalTime(); var calendar = new PersianCalendar();
                return $"{calendar.GetYear(local):0000}/{calendar.GetMonth(local):00}/{calendar.GetDayOfMonth(local):00} {local:HH:mm:ss}";
            }
        }
        public string Brand => Entry.BrandName;
        public string Action => Entry.Action;
        public string Details => Entry.Details;
    }
    public BrandHistoryDialog(Ledger ledger, string selectedMonth, IEnumerable<string> months, string? brand = null)
    {
        Title = "لاگ تغییرات ماه"; Width = Math.Min(1100, SystemParameters.WorkArea.Width - 32); Height = Math.Min(680, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(580, Width); MinHeight = Math.Min(400, Height); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("لاگ تغییرات ماه" + (brand is null ? "" : " · " + brand), "زمان ثبت با تاریخ اثر تغییر متفاوت است. فقط تغییرات ثبت نهایی از این نسخه به بعد نمایش داده می‌شوند."));
        var body = new Grid { Margin = new Thickness(22, 14, 22, 12) }; Grid.SetRow(body, 1); root.Children.Add(body);
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
        var filters = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.RightToLeft }; body.Children.Add(filters);
        filters.Children.Add(DialogUi.Label("ماه:"));
        var selector = new ComboBox { ItemsSource = months.Concat(ledger.BrandChanges.Select(e => e.MonthKey)).Append(selectedMonth).Distinct().OrderByDescending(m => m).ToList(), SelectedItem = selectedMonth, Width = 150, Margin = new Thickness(8, 0, 8, 8) }; filters.Children.Add(selector);
        var count = new TextBlock { Style = (Style)FindResource("MutedText"), VerticalAlignment = VerticalAlignment.Center }; filters.Children.Add(count);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, RowHeight = double.NaN, CanUserAddRows = false, CanUserDeleteRows = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(grid, 1); body.Children.Add(grid);
        void Column(string title, string path, double width, string style = "TableText") => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, ElementStyle = (Style)FindResource(style) });
        Column("زمان ثبت (محلی)", nameof(HistoryRow.RecordedAt), 185, "TableNumber"); Column("برند", nameof(HistoryRow.Brand), 140); Column("عملیات", nameof(HistoryRow.Action), 170);
        var detailsStyle = new Style(typeof(TextBlock), (Style)FindResource("TableText")); detailsStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap)); detailsStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8)));
        grid.Columns.Add(new DataGridTextColumn { Header = "درصد قبل/بعد و تاریخ اثر", Binding = new Binding(nameof(HistoryRow.Details)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 260, ElementStyle = detailsStyle });
        void Refresh()
        {
            var month = selector.SelectedItem as string ?? selectedMonth;
            var rows = BrandChangeHistory.ForMonth(ledger, month, brand).Select(e => new HistoryRow(e)).ToList();
            grid.ItemsSource = rows; count.Text = rows.Count == 0 ? "تغییر ثبت‌شده‌ای برای این ماه وجود ندارد." : $"{rows.Count} تغییر ثبت‌شده";
        }
        selector.SelectionChanged += (_, _) => Refresh();
        var close = new Button { Content = "بستن", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 0, 22, 16) }; Grid.SetRow(close, 2); root.Children.Add(close);
        Refresh();
    }
}
