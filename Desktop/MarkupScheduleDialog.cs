using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class MarkupScheduleDialog : Window
{
    public sealed record PeriodRow(MarkupPeriod? Period, string Start, string End, decimal Markup)
    {
        public string Percent => Rules.Percent(Markup);
        public string Kind => Period is null ? "پایه ماه" : "تغییر از تاریخ";
    }
    public Ledger? Value { get; private set; }
    public string ActionText { get; private set; } = "";

    public MarkupScheduleDialog(Ledger ledger, Brand brand, string month)
    {
        var rate = BrandMonthRules.RequireConfirmed(ledger, brand.Name, month);
        var periods = ledger.MarkupPeriods.Where(x => MarkupRules.SameBrand(x.BrandName, brand.Name) && Rules.MonthOf(x.StartDate) == month).OrderBy(x => x.StartDate).ToList();
        var first = month.Replace("/", "") + "01";
        var starts = new[] { first }.Concat(periods.Select(x => x.StartDate)).ToList();
        string End(int index) => index + 1 == starts.Count ? "پایان ماه" : starts[index + 1][..6] + (int.Parse(starts[index + 1][6..]) - 1).ToString("00");
        var rows = new List<PeriodRow> { new(null, first, End(0), rate.Markup) };
        rows.AddRange(periods.Select((p, i) => new PeriodRow(p, p.StartDate, End(i + 1), p.Markup)));
        Title = "بازه‌های مارک‌آپ"; Width = 850; Height = 650; MinHeight = 520; MinWidth = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FlowDirection = FlowDirection.LeftToRight;
        FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header($"مارک‌آپ {brand.Name} · {month}", "فقط مارک‌آپ بازه‌ای است؛ بقیه درصدها ماهانه می‌مانند. مارک‌آپ اختصاصی فروش با تغییر بازه جایگزین نمی‌شود."));
        var body = new Grid { Margin = new Thickness(22, 16, 22, 12) }; body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(body, 1); root.Children.Add(body);
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = rows, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DataGridTextColumn Column(string title, string path) => new() { Header = title, Binding = new Binding(path), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        grid.Columns.Add(Column("از تاریخ", nameof(PeriodRow.Start))); grid.Columns.Add(Column("تا تاریخ", nameof(PeriodRow.End))); grid.Columns.Add(Column("مارک‌آپ", nameof(PeriodRow.Percent))); grid.Columns.Add(Column("نوع", nameof(PeriodRow.Kind))); body.Children.Add(grid);
        var fields = new StackPanel { Margin = new Thickness(0, 16, 0, 0) }; Grid.SetRow(fields, 1); body.Children.Add(fields);
        fields.Children.Add(DialogUi.Label("تاریخ شروع اعتبار (مانند 14050615)")); var date = DialogUi.Input(month.Replace("/", "") + "15"); fields.Children.Add(date);
        fields.Children.Add(DialogUi.Label("مارک‌آپ ٪")); var markup = DialogUi.Input(); DialogUi.Percent(markup, rate.Markup); fields.Children.Add(markup);
        var note = new TextBlock { Text = "برای اصلاح، یک بازه از جدول انتخاب کنید؛ تاریخ شروع آن ثابت می‌ماند. تغییر جدید تا شروع بازه بعدی یا پایان ماه معتبر است.", Style = (Style)FindResource("MutedText"), TextWrapping = TextWrapping.Wrap }; fields.Children.Add(note);
        var error = new TextBlock { Foreground = Brushes.Firebrick, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap }; fields.Children.Add(error);
        grid.SelectionChanged += (_, _) => { if (grid.SelectedItem is PeriodRow row) { date.Text = row.Start; DialogUi.Percent(markup, row.Markup); } };
        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 0, 22, 18) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var add = new Button { Content = "ثبت تغییر از تاریخ", Style = (Style)FindResource("Primary") }; footer.Children.Add(add);
        var correct = new Button { Content = "اصلاح بازه انتخاب‌شده" }; footer.Children.Add(correct); footer.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        void Submit(bool correction)
        {
            try
            {
                var start = Rules.Digits(date.Text); var value = DialogUi.Percent(markup);
                if (Rules.MonthOf(start) != month || value < 0) throw new InvalidDataException("تاریخ باید داخل ماه انتخاب‌شده و مارک‌آپ نامنفی باشد.");
                var selected = grid.SelectedItem as PeriodRow;
                if (correction && (selected is null || selected.Start != start)) throw new InvalidDataException("یک بازه انتخاب کنید؛ تاریخ شروع آن نباید تغییر کند.");
                if (correction && selected!.Period is null)
                {
                    var rates = BrandMonthRules.DraftFor(ledger, month).Rates.Select(r => MarkupRules.SameBrand(r.BrandName, brand.Name) ? r with { Markup = value } : r);
                    Value = BrandMonthRules.Confirm(ledger, month, rates);
                }
                else
                {
                    var period = correction ? (selected!.Period! with { Markup = value }) : new MarkupPeriod { BrandName = brand.Name, StartDate = start, Markup = value };
                    Value = MarkupRules.SetPeriod(ledger, period, correction);
                }
                ActionText = $"مارک‌آپ «{brand.Name}» از {start} {(correction ? "اصلاح" : "ثبت")} شد.";
                DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
        add.Click += (_, _) => Submit(false); correct.Click += (_, _) => Submit(true);
    }
}
