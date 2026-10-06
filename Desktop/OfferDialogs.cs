using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class ImportReviewDialog : Window
{
    public ImportReviewDialog(IEnumerable<string> changes, string? explanation = null, string? acceptText = null)
    {
        Title = "بازبینی ردیف‌های اصلاح‌شده"; Width = 850; Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        FlowDirection = FlowDirection.LeftToRight;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var accept = new Button { Content = acceptText ?? "تأیید جایگزینی و ورود فایل", Style = (Style)FindResource("Primary") };
        accept.Click += (_, _) => DialogResult = true; footer.Children.Add(accept);
        footer.Children.Add(new Button { Content = "انصراف؛ هیچ تغییری ثبت نشود", IsCancel = true });
        var header = new TextBlock { Text = explanation ?? "این ردیف‌ها قبلاً وارد شده‌اند و محتوایشان تغییر کرده است. با تأیید، رکورد قبلی جایگزین و محاسبات دوباره انجام می‌شود.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = string.Join("\n\n", changes), TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left } });
    }
}

public sealed class OfferEditDialog : Window
{
    sealed record SaleOption(Sale Value, string Display);
    public OfferEntry? Value { get; private set; }

    public OfferEditDialog(OfferEntry entry, IEnumerable<Sale> sales)
    {
        Title = entry.IsReturn ? "ارزش‌گذاری آفر برگشتی" : "تعیین فروش مرتبط با آفر";
        Width = 760; Height = 540; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var save = new Button { Content = "ثبت", Style = (Style)FindResource("Primary") }; footer.Children.Add(save);
        var clear = new Button { Content = "حذف تعیین دستی" }; footer.Children.Add(clear);
        footer.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        body.Children.Add(new TextBlock { Text = $"{entry.Date} · {entry.Customer}\n{entry.Code} · {entry.Name}\nتعداد: {Rules.Money(entry.Quantity)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var options = sales.Where(s => Rules.MatchText(s.Customer).Equals(Rules.MatchText(entry.Customer), StringComparison.OrdinalIgnoreCase) && s.Date == entry.Date)
            .Select(s => new SaleOption(s, $"فاکتور {s.InvoiceNumber} · کد {s.Code} · تعداد {Rules.Money(s.Quantity)} · قیمت {Rules.Money(s.UnitPrice)} ریال")).ToList();
        var selection = new ComboBox { ItemsSource = options, DisplayMemberPath = nameof(SaleOption.Display), Margin = new Thickness(0, 8, 0, 14) };
        selection.SelectedItem = options.FirstOrDefault(x => x.Value.Id == entry.ManualSaleId);
        var price = new TextBox { Text = entry.ManualUnitPrice.HasValue ? Rules.Money(entry.ManualUnitPrice.Value) : "", Margin = new Thickness(0, 8, 0, 14) }; MoneyInput.Attach(price);
        body.Children.Add(new TextBlock { Text = entry.IsReturn ? "قیمت واحد فروش اصلی، ریال" : "فروش مرتبط در همان تاریخ و برای همین مشتری" });
        body.Children.Add(entry.IsReturn ? (UIElement)price : selection);
        if (entry.IsReturn) body.Children.Add(new TextBlock { Text = "فقط در نبود آفر اصلی: مبلغ کامل به ماه برگشت اضافه می‌شود، بدون حدس‌زدن سهم نقدی یا تخفیف فروش قدیمی. اگر بعداً تطبیق خودکار ممکن شود، این ارزش‌گذاری دوباره اعمال نمی‌شود.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(0, 0, 0, 12) });
        body.Children.Add(new TextBlock { Text = "دلیل تعیین دستی" });
        var reason = new TextBox { Text = entry.ManualReason, Margin = new Thickness(0, 8, 0, 8) }; body.Children.Add(reason);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap }; body.Children.Add(error);
        clear.Click += (_, _) => { Value = entry with { ManualSaleId = "", ManualUnitPrice = null, ManualReason = "", ManualUpdatedUtc = DateTime.UtcNow }; DialogResult = true; };
        save.Click += (_, _) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reason.Text)) throw new InvalidDataException("دلیل را وارد کنید.");
                if (entry.IsReturn)
                {
                    var value = Rules.Number(price.Text); if (value <= 1) throw new InvalidDataException("قیمت فروش اصلی باید بیشتر از ۱ ریال باشد.");
                    Value = entry with { ManualUnitPrice = value, ManualReason = reason.Text.Trim(), ManualUpdatedUtc = DateTime.UtcNow };
                }
                else
                {
                    if (selection.SelectedItem is not SaleOption option) throw new InvalidDataException("فروش مرتبط را انتخاب کنید.");
                    Value = entry with { ManualSaleId = option.Value.Id, ManualReason = reason.Text.Trim(), ManualUpdatedUtc = DateTime.UtcNow };
                }
                DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
    }
}
