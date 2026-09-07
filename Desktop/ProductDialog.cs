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
    readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap, LineHeight = 27 };
    readonly Product original;
    readonly List<Product> others;
    bool initialized;

    public ProductDialog(Product? product, List<Product> all)
    {
        original = product ?? new Product(); others = all;
        Title = product == null ? "افزودن کالا" : "ویرایش کالا";
        Width = 820; Height = 780; MinWidth = 660; MinHeight = 590; WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(26, 19, 26, 18) };
        header.Child = new StackPanel { Children = { new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White }, new TextBlock { Text = "اطلاعات و درصدهای این کالا فقط در ماه فعال ثبت می‌شوند.", Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), Margin = new Thickness(0, 6, 0, 0) } } };
        root.Children.Add(header);

        var outer = new StackPanel { Margin = new Thickness(26, 22, 26, 26) };
        var scroll = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        TextBlock Section(string title, string subtitle)
        {
            var box = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 13, 0, 4) }; outer.Children.Add(box);
            outer.Children.Add(new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 0, 0, 7) }); return box;
        }
        StackPanel FieldPanel(string label)
        {
            var panel = new StackPanel { Margin = new Thickness(6) };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)) });
            return panel;
        }
        void Field(Panel grid, string key, string label, string value, bool numeric = true)
        {
            var panel = FieldPanel(label); var tb = new TextBox { Text = value, FlowDirection = numeric ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
            fields[key] = tb; panel.Children.Add(tb); grid.Children.Add(panel); tb.TextChanged += (_, _) => { if (initialized) Preview(); };
        }
        string Num(decimal x) => x.ToString("0.########", CultureInfo.InvariantCulture);

        Section("مشخصات کالا", "برند را انتخاب کنید یا نام برند تازه‌ای تایپ کنید.");
        var identity = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 }; outer.Children.Add(identity);
        var brandPanel = FieldPanel("برند"); brand.ItemsSource = all.Select(x => x.Brand).Distinct().OrderBy(x => x).ToList(); brand.Text = original.Brand; brandPanel.Children.Add(brand); identity.Children.Add(brandPanel);
        Field(identity, "name", "نام کالا", original.Name, false); Field(identity, "price", "قیمت خرید واحد — ریال", product == null ? "" : Num(original.Price)); Field(identity, "quantity", "تعداد / مقدار", Num(original.Quantity));

        Section("شرایط خرید", "تخفیف و آفر از مبلغ اولیهٔ خرید محاسبه می‌شوند.");
        var purchase = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 }; outer.Children.Add(purchase);
        Field(purchase, "discount", "تخفیف خرید ٪", Num(original.Discount * 100)); Field(purchase, "offer", "آفر خرید ٪", Num(original.Offer * 100)); Field(purchase, "markup", "سود اضافه ٪", Num(original.Markup * 100));

        Section("ترکیب فروش", "سهم فروش نقدی و چکی باید در مجموع ۱۰۰٪ باشد.");
        var sale = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 }; outer.Children.Add(sale);
        Field(sale, "credit", "سهم فروش چکی ٪", Num(original.CreditShare * 100)); Field(sale, "cash", "سهم فروش نقدی ٪", Num(original.CashShare * 100)); Field(sale, "cashdiscount", "تخفیف نقدی ٪", Num(original.CashDiscount * 100));

        outer.Children.Add(error);
        var previewBox = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 248, 248)), BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 198)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(15), Margin = new Thickness(0, 8, 0, 0) };
        previewBox.Child = new StackPanel { Children = { new TextBlock { Text = "پیش‌نمایش محاسبه", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(143, 32, 32)), Margin = new Thickness(0, 0, 0, 5) }, preview } }; outer.Children.Add(previewBox);
        outer.Children.Add(new TextBlock { Text = "اعداد را بدون جداکننده یا با جداکنندهٔ رایج وارد کنید؛ محاسبه هم‌زمان انجام می‌شود.", Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 11, 0, 5) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        var save = new Button { Content = "ثبت کالا", Style = (Style)FindResource("Primary"), IsDefault = true }; save.Click += (_, _) => { try { Value = Read(); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
        buttons.Children.Add(save); buttons.Children.Add(new Button { Content = "انصراف", IsCancel = true }); outer.Children.Add(buttons);
        initialized = true; if (product != null) Preview();
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
        Title = copy ? "کپی به ماه جدید" : "ماه جدید"; Width = 440; Height = 335; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(24, 18, 24, 17), Child = new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White } }; root.Children.Add(header);
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 24) }; Grid.SetRow(panel, 1); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "ماه شمسی", FontWeight = FontWeights.SemiBold }); var input = new TextBox { Text = suggested, FlowDirection = FlowDirection.LeftToRight }; panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = copy ? "کالاها و درصدها به‌صورت مستقل کپی می‌شوند." : "ماه خالی با هزینهٔ ثابت ماه جاری ساخته می‌شود.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 4, 0, 7) });
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var button = new Button { Content = "ایجاد ماه", IsDefault = true, Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(button);
        button.Click += (_, _) => { var k = Rules.Digits(input.Text); if (!Rules.ValidMonth(k)) { error.Text = "قالب ماه باید مانند 1405/06 باشد."; return; } Key = k; DialogResult = true; };
    }
}
