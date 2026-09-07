using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;
public sealed class ProductDialog : Window
{
    public Product? Value { get; private set; }
    readonly Dictionary<string, TextBox> fields = [];
    readonly ComboBox brand = new() { IsEditable = true, IsTextSearchEnabled = true };
    readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
    readonly Product original;
    readonly List<Product> others;
    bool initialized;
    public ProductDialog(Product? product, List<Product> all)
    {
        original = product ?? new Product(); others = all; Title = product == null ? "افزودن کالا" : "ویرایش کالا";
        Width = 740; Height = 730; MinWidth = 620; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var outer = new StackPanel { Margin = new Thickness(22) }; var scroll = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Content = scroll;
        outer.Children.Add(new TextBlock { Text = Title, FontSize = 22, Margin = new Thickness(0, 0, 0, 18) });
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; outer.Children.Add(grid);
        var brandPanel = new StackPanel { Margin = new Thickness(8) }; brandPanel.Children.Add(new TextBlock { Text = "برند — انتخاب یا تایپ نام جدید" }); brand.ItemsSource = all.Select(x => x.Brand).Distinct().OrderBy(x => x).ToList(); brand.Text = original.Brand; brandPanel.Children.Add(brand); grid.Children.Add(brandPanel);
        void Field(string key, string label, string value, bool numeric = true)
        {
            var panel = new StackPanel { Margin = new Thickness(8) }; panel.Children.Add(new TextBlock { Text = label });
            var tb = new TextBox { Text = value, FlowDirection = numeric ? FlowDirection.LeftToRight : FlowDirection.RightToLeft }; fields[key] = tb; panel.Children.Add(tb); grid.Children.Add(panel); tb.TextChanged += (_, _) => { if (initialized) Preview(); };
        }
        string Num(decimal x) => x.ToString("0.########", CultureInfo.InvariantCulture);
        Field("name", "نام کالا", original.Name, false); Field("price", "قیمت خرید واحد — ریال", product == null ? "" : Num(original.Price)); Field("quantity", "تعداد / مقدار", Num(original.Quantity));
        Field("discount", "تخفیف خرید ٪", Num(original.Discount * 100)); Field("offer", "آفر خرید ٪", Num(original.Offer * 100)); Field("markup", "سود اضافه ٪", Num(original.Markup * 100)); Field("credit", "سهم فروش چکی ٪", Num(original.CreditShare * 100)); Field("cash", "سهم فروش نقدی ٪", Num(original.CashShare * 100)); Field("cashdiscount", "تخفیف نقدی ٪", Num(original.CashDiscount * 100));
        outer.Children.Add(error); outer.Children.Add(preview); outer.Children.Add(new TextBlock { Text = "درصدهای این فرم فقط متعلق به همین کالا و همین ماه هستند.", Foreground = Brushes.SlateGray });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0) };
        var save = new Button { Content = "ثبت کالا", Style = (Style)FindResource("Primary"), IsDefault = true };
        save.Click += (_, _) => { try { Value = Read(); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
        buttons.Children.Add(save); buttons.Children.Add(new Button { Content = "انصراف", IsCancel = true }); outer.Children.Add(buttons); initialized = true;
        if (product != null) Preview();
    }
    Product Read()
    {
        decimal N(string k) => Rules.Number(fields[k].Text);
        var p = Rules.Clean(original with { Brand = brand.Text, Name = fields["name"].Text, Price = N("price"), Quantity = N("quantity"), Discount = N("discount") / 100, Offer = N("offer") / 100, Markup = N("markup") / 100, CreditShare = N("credit") / 100, CashShare = N("cash") / 100, CashDiscount = N("cashdiscount") / 100 });
        var e = Rules.Validate(p); if (others.Any(x => x.Id != p.Id && Rules.Normalize(x.Brand) == p.Brand && Rules.Normalize(x.Name) == p.Name)) e.Add("این نام کالا در برند انتخابی تکراری است.");
        if (e.Count != 0) throw new InvalidDataException(string.Join("\n", e)); return p;
    }
    void Preview()
    {
        try { var r = Rules.Calculate(Read()); error.Text = ""; preview.Text = $"فروش واحد: {Rules.Money(r.UnitSale)} ریال\nفروش کل: {Rules.Money(r.Sales)} ریال   |   سود: {Rules.Money(r.Profit)} ریال\nحاشیه سود: {Rules.Percent(r.Margin)}"; }
        catch (Exception ex) { error.Text = ex.Message; preview.Text = ""; }
    }
}
public sealed class MonthDialog : Window
{
    public string Key { get; private set; } = "";
    public MonthDialog(string suggested, bool copy)
    {
        Title = copy ? "کپی به ماه جدید" : "ماه جدید"; Width = 410; Height = 300; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "ماه شمسی — نمونه ۱۴۰۵/۰۶" }); var input = new TextBox { Text = suggested, FlowDirection = FlowDirection.LeftToRight }; panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = copy ? "کالاها و درصدها به‌صورت مستقل کپی می‌شوند." : "ماه خالی با هزینه ثابت ماه جاری ساخته می‌شود.", TextWrapping = TextWrapping.Wrap });
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(error);
        var button = new Button { Content = "ایجاد ماه", IsDefault = true, Style = (Style)FindResource("Primary") }; panel.Children.Add(button);
        button.Click += (_, _) => { var k = Rules.Digits(input.Text); if (!Rules.ValidMonth(k)) { error.Text = "قالب ماه باید مانند 1405/06 باشد."; return; } Key = k; DialogResult = true; };
    }
}
