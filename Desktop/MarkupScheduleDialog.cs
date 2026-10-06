using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class MarkupScheduleDialog : Window
{
    public sealed record PeriodRow(MarkupPeriod? Period, string Start, string End, decimal Markup, int Sales, int Exceptions)
    {
        public string Percent => Rules.Percent(Markup);
        public string Kind => Period is null ? "پایه ماه" : "بازه اختصاصی";
    }
    public MarkupScheduleDialog(Func<Ledger> getLedger, Brand brand, string month, bool monthClosed, Action<Ledger> validate, Action<Ledger, string> commit)
    {
        Title = "بازه‌های مارک‌آپ"; Width = Math.Min(940, SystemParameters.WorkArea.Width - 32); Height = Math.Min(660, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(580, Width); MinHeight = Math.Min(400, Height); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header($"مارک‌آپ {brand.Name} · {month}", "شروع و پایان داخل بازه‌اند. روزهای خارج از بازه با پایه ماه محاسبه می‌شوند؛ مارک‌آپ مخصوص فروش اولویت دارد."));
        var body = new Grid { Margin = new Thickness(22, 16, 22, 12) }; body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(body, 1); root.Children.Add(body);
        var note = new TextBlock { Style = (Style)FindResource("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) }; body.Children.Add(note);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(grid, 1); body.Children.Add(grid);
        void Column(string title, string property) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(property), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Column("از تاریخ", nameof(PeriodRow.Start)); Column("تا تاریخ", nameof(PeriodRow.End)); Column("مارک‌آپ", nameof(PeriodRow.Percent)); Column("نوع", nameof(PeriodRow.Kind)); Column("فروش", nameof(PeriodRow.Sales)); Column("استثنای فروش", nameof(PeriodRow.Exceptions));
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(22, 0, 22, 18) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var add = new Button { Content = "افزودن بازه", Style = (Style)FindResource("Primary") }; var edit = new Button { Content = "ویرایش بازه", IsEnabled = false }; var delete = new Button { Content = "حذف بازه", IsEnabled = false };
        footer.Children.Add(add); footer.Children.Add(edit); footer.Children.Add(delete); footer.Children.Add(new Button { Content = "بستن", IsCancel = true });
        bool editable = false;
        void Refresh()
        {
            var ledger = MarkupRules.UpgradeLegacy(getLedger()); var rate = BrandMonthRules.DraftFor(ledger, month).Rates.First(r => MarkupRules.SameBrand(r.BrandName, brand.Name));
            editable = !monthClosed && brand.IsActive && rate.IsConfirmed == true; add.IsEnabled = editable; edit.IsEnabled = delete.IsEnabled = false;
            note.Text = monthClosed ? "ماه بسته است؛ مشاهده مجاز است. برای تغییر ابتدا ماه را باز کنید." : !brand.IsActive ? "برند غیرفعال است؛ برای تغییر بازه‌ها برند را فعال کنید." : rate.IsConfirmed != true ? "ابتدا درصدهای همین برند و ماه را تأیید کنید." : "برای ویرایش یا حذف، بازه اختصاصی را انتخاب کنید. مقدار پایه از درصدهای ماه ویرایش می‌شود.";
            var codes = ledger.Items.Where(i => MarkupRules.SameBrand(i.Brand, brand.Name)).Select(i => i.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sales = ledger.Sales.Where(s => codes.Contains(s.Code) && Rules.MonthOf(s.Date) == month).ToList();
            var rows = new List<PeriodRow>(); var cursor = month.Replace("/", "") + "01"; var last = Rules.LastDate(month);
            void Row(MarkupPeriod? p, string start, string end, decimal markup) => rows.Add(new(p, start, end, markup, sales.Count(s => string.CompareOrdinal(s.Date, start) >= 0 && string.CompareOrdinal(s.Date, end) <= 0), sales.Count(s => s.MarkupOverride.HasValue && string.CompareOrdinal(s.Date, start) >= 0 && string.CompareOrdinal(s.Date, end) <= 0)));
            foreach (var p in ledger.MarkupPeriods.Where(p => MarkupRules.SameBrand(p.BrandName, brand.Name) && Rules.MonthOf(p.StartDate) == month).OrderBy(p => p.StartDate))
            {
                if (string.CompareOrdinal(cursor, p.StartDate) < 0) Row(null, cursor, Rules.PreviousDate(p.StartDate), rate.Markup);
                Row(p, p.StartDate, p.EndDate, p.Markup); cursor = p.EndDate[..6] + (int.Parse(p.EndDate[6..]) + 1).ToString("00");
            }
            if (string.CompareOrdinal(cursor, last) <= 0) Row(null, cursor, last, rate.Markup);
            grid.ItemsSource = rows;
        }
        grid.SelectionChanged += (_, _) => edit.IsEnabled = delete.IsEnabled = editable && grid.SelectedItem is PeriodRow { Period: not null };
        void Open(MarkupPeriod? p, bool remove = false)
        {
            var dialog = new MarkupPeriodDialog(getLedger(), brand, month, p, remove, validate, commit) { Owner = this };
            if (dialog.ShowDialog() == true) Refresh();
        }
        add.Click += (_, _) => Open(null);
        edit.Click += (_, _) => { if (grid.SelectedItem is PeriodRow { Period: not null } row) Open(row.Period); };
        delete.Click += (_, _) => { if (grid.SelectedItem is PeriodRow { Period: not null } row) Open(row.Period, true); };
        Refresh();
    }
}

public sealed class MarkupPeriodDialog : BrandDialogShell
{
    public MarkupPeriodDialog(Ledger ledger, Brand brand, string month, MarkupPeriod? period, bool remove, Action<Ledger> validate, Action<Ledger, string> commit)
        : base(ledger, remove ? "حذف بازه مارک‌آپ" : period is null ? "افزودن بازه مارک‌آپ" : "ویرایش بازه مارک‌آپ", $"{brand.Name} · {month}\nبازه باید داخل همین ماه باشد؛ هم‌پوشانی مجاز نیست.", validate, commit, 740, 600)
    {
        var panel = ScrollForm(); var start = Field(panel, "از تاریخ؛ شامل این روز", period?.StartDate ?? month.Replace("/", "") + "01");
        var startError = new TextBlock { Foreground = Brushes.Firebrick, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left }; panel.Children.Add(startError);
        var end = Field(panel, "تا تاریخ؛ شامل این روز", period?.EndDate ?? Rules.LastDate(month));
        var endError = new TextBlock { Foreground = Brushes.Firebrick, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left }; panel.Children.Add(endError);
        var rate = BrandMonthRules.RequireConfirmed(ledger, brand.Name, month);
        var markup = Field(panel, "مارک‌آپ ٪"); DialogUi.Percent(markup, period?.Markup ?? rate.Markup);
        panel.Children.Add(new TextBlock { Text = $"روزهای خارج از بازه‌ها: پایه ماه {Rules.Percent(rate.Markup)}. استثناهای فروش حفظ می‌شوند.", Style = (Style)FindResource("MutedText"), TextWrapping = TextWrapping.Wrap });
        if (remove) { start.IsReadOnly = end.IsReadOnly = markup.IsReadOnly = true; PreviewButton.Content = "بررسی حذف بازه"; }
        void Check()
        {
            startError.Text = Rules.ValidDate(Rules.Digits(start.Text)) ? "" : "تاریخ شمسی معتبر وارد کنید.";
            endError.Text = Rules.ValidDate(Rules.Digits(end.Text)) ? "" : "تاریخ شمسی معتبر وارد کنید.";
            if (startError.Text.Length == 0 && endError.Text.Length == 0 && string.CompareOrdinal(Rules.Digits(start.Text), Rules.Digits(end.Text)) > 0) endError.Text = "پایان نباید قبل از شروع باشد.";
        }
        start.TextChanged += (_, _) => Check(); end.TextChanged += (_, _) => Check();
        ReadChange = () =>
        {
            if (remove) return MarkupRules.RemovePeriod(ledger, period!.Id);
            Check(); if (startError.Text.Length > 0 || endError.Text.Length > 0) throw new InvalidDataException("تاریخ‌های مشخص‌شده را اصلاح کنید.");
            var value = (period ?? new MarkupPeriod { BrandName = brand.Name }) with { StartDate = Rules.Digits(start.Text), EndDate = Rules.Digits(end.Text), Markup = DialogUi.Percent(markup) };
            if (Rules.MonthOf(value.StartDate) != month || Rules.MonthOf(value.EndDate) != month) throw new InvalidDataException("شروع و پایان باید داخل ماه انتخاب‌شده باشند.");
            return MarkupRules.SetPeriod(ledger, value, period is not null);
        };
        Scope = remove ? "بازه حذف می‌شود؛ روزهای آزادشده با پایه ماه محاسبه خواهند شد." : "شروع و پایان هر دو داخل بازه‌اند. فروش‌های واردشده یا خارج‌شده از بازه دوباره محاسبه می‌شوند.";
        ActionText = remove ? $"بازه مارک‌آپ «{brand.Name}» حذف شد." : $"بازه مارک‌آپ «{brand.Name}» ذخیره شد.";
    }
}
