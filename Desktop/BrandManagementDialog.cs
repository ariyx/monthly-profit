using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class BrandManagementDialog : Window
{
    public BrandManagementDialog(Func<Ledger> getLedger, string brandName, string selectedMonth, IEnumerable<string> months, Func<string, bool> isClosed, Action<Ledger> validate, Action<Ledger, string> commit)
    {
        Title = "مدیریت برند"; Width = Math.Min(900, SystemParameters.WorkArea.Width - 32); Height = Math.Min(680, SystemParameters.WorkArea.Height - 32);
        MinWidth = Math.Min(580, Width); MinHeight = Math.Min(400, Height); WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("مدیریت برند", "مشخصات برند عمومی است؛ درصدها و بازه‌ها به ماه انتخاب‌شده مربوط‌اند."));
        var body = new Grid { Margin = new Thickness(22, 16, 22, 12) }; body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(body, 1); root.Children.Add(body);
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.RightToLeft }; body.Children.Add(nav);
        var identityTab = new Button { Content = "مشخصات" }; var monthTab = new Button { Content = "درصدهای ماه" }; var rangesTab = new Button { Content = "بازه‌های مارک‌آپ" }; nav.Children.Add(identityTab); nav.Children.Add(monthTab); nav.Children.Add(rangesTab);
        var panel = new StackPanel(); var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); body.Children.Add(scroll);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(22, 0, 22, 16) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var lifecycle = new Button(); footer.Children.Add(lifecycle); footer.Children.Add(new Button { Content = "بستن", IsCancel = true });
        var currentSection = 0; var currentMonth = selectedMonth;
        var history = new Button { Content = "لاگ تغییرات ماه" }; nav.Children.Add(history);
        history.Click += (_, _) => new BrandHistoryDialog(getLedger(), currentMonth, months, brandName) { Owner = this }.ShowDialog();
        void Text(string value) => panel.Children.Add(new TextBlock { Text = value, Style = (Style)FindResource("MutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        void Refresh()
        {
            var ledger = getLedger(); var brand = ledger.Brands.First(b => MarkupRules.SameBrand(b.Name, brandName));
            panel.Children.Clear(); identityTab.FontWeight = currentSection == 0 ? FontWeights.Bold : FontWeights.Normal; monthTab.FontWeight = currentSection == 1 ? FontWeights.Bold : FontWeights.Normal; rangesTab.FontWeight = currentSection == 2 ? FontWeights.Bold : FontWeights.Normal;
            panel.Children.Add(DialogUi.Label(brand.Name + (brand.IsActive ? " · فعال" : " · غیرفعال")));
            lifecycle.Content = !brand.IsActive ? "فعال‌سازی مجدد" : BrandLifecycle.RelatedTransactions(ledger, brand.Name) > 0 ? "غیرفعال‌سازی برند" : "حذف برند";
            if (currentSection == 0)
            {
                Text("پیشوندها: " + BrandPrefixRules.Display(brand.CodePrefixes)); Text("کدهای دقیق: " + BrandPrefixRules.Display(brand.ExactProductCodes));
                Text($"{ledger.Items.Count(i => MarkupRules.SameBrand(i.Brand, brand.Name))} کالا · {BrandLifecycle.RelatedTransactions(ledger, brand.Name)} ردیف فروش، برگشت یا آفر");
                var edit = new Button { Content = "ویرایش مشخصات", HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(edit);
                edit.Click += (_, _) => { var dialog = new BrandIdentityDialog(getLedger(), brand, validate, commit) { Owner = this }; if (dialog.ShowDialog() == true) { brandName = dialog.CurrentName; Refresh(); } };
            }
            else
            {
                panel.Children.Add(DialogUi.Label("ماه")); var selector = new ComboBox { ItemsSource = months.Append(selectedMonth).Distinct().OrderByDescending(m => m).ToList(), SelectedItem = currentMonth, Width = 180, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(selector);
                selector.SelectionChanged += (_, _) => { if (selector.SelectedItem is string month && month != currentMonth) { currentMonth = month; Refresh(); } };
                var rate = BrandMonthRules.DraftFor(ledger, currentMonth).Rates.First(r => MarkupRules.SameBrand(r.BrandName, brand.Name));
                Text($"منبع: {rate.SourceMonthKey} · {(rate.IsConfirmed == true ? "تأیید شده" : "نیازمند تأیید")}");
                if (isClosed(currentMonth)) Text("ماه بسته است؛ مشاهده مجاز است. برای تغییر مالی ابتدا ماه را باز کنید.");
                if (currentSection == 1)
                {
                    new BrandRateFields(panel, rate, false);
                    var edit = new Button { Content = rate.IsConfirmed == true ? "ویرایش درصدهای ماه" : "تأیید درصدهای این برند", IsEnabled = !isClosed(currentMonth), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(edit);
                    edit.Click += (_, _) => { var dialog = new BrandPercentDialog(getLedger(), brand, currentMonth, validate, commit) { Owner = this }; if (dialog.ShowDialog() == true) Refresh(); };
                }
                else
                {
                    Text($"پایه ماه: {Rules.Percent(rate.Markup)}. روزهای خارج از بازه‌ها از این مقدار استفاده می‌کنند.");
                    Text($"{ledger.MarkupPeriods.Count(p => MarkupRules.SameBrand(p.BrandName, brand.Name) && Rules.MonthOf(p.StartDate) == currentMonth)} بازه اختصاصی در این ماه");
                    var open = new Button { Content = "مشاهده / مدیریت بازه‌ها", HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(open);
                    open.Click += (_, _) => { new MarkupScheduleDialog(getLedger, brand, currentMonth, isClosed(currentMonth), validate, commit) { Owner = this }.ShowDialog(); Refresh(); };
                }
            }
        }
        identityTab.Click += (_, _) => { currentSection = 0; Refresh(); }; monthTab.Click += (_, _) => { currentSection = 1; Refresh(); }; rangesTab.Click += (_, _) => { currentSection = 2; Refresh(); };
        lifecycle.Click += (_, _) =>
        {
            try
            {
                var ledger = getLedger(); var brand = ledger.Brands.First(b => MarkupRules.SameBrand(b.Name, brandName));
                if (!brand.IsActive) { commit(ledger with { Brands = ledger.Brands.Select(b => b == brand ? b with { IsActive = true } : b).ToList() }, "برند فعال شد."); Refresh(); return; }
                var count = BrandLifecycle.RelatedTransactions(ledger, brandName); var items = ledger.Items.Count(i => MarkupRules.SameBrand(i.Brand, brandName)); var action = count > 0 ? "غیرفعال‌سازی" : "حذف";
                if (MessageBox.Show(this, $"{action} «{brandName}»؟\n{items} کالا و {count} تراکنش مرتبط.\n" + (count > 0 ? "سوابق و محاسبات حفظ می‌شوند." : "کالاهای بدون فروش با برند خالی باقی می‌مانند."), action, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                var next = BrandLifecycle.RemoveOrDeactivate(ledger, brandName); validate(next); commit(next, action + " برند انجام شد.");
                if (count == 0) Close(); else Refresh();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "عملیات انجام نشد", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        Refresh();
    }
}
