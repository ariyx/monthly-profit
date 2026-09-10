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
    readonly ComboBox brand = new() { IsEditable = true, IsTextSearchEnabled = true, FlowDirection = FlowDirection.RightToLeft, HorizontalContentAlignment = HorizontalAlignment.Right };
    readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 8, 0, 8) };
    readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, LineHeight = 27 };
    readonly Product original;
    readonly List<Product> others;
    bool initialized;

    public ProductDialog(Product? product, List<Product> all)
    {
        original = product ?? new Product(); others = all;
        Title = product == null ? "افزودن کالا" : "ویرایش کالا";
        Width = 820; Height = 780; MinWidth = 660; MinHeight = 590; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");

        var root = new Grid { FlowDirection = FlowDirection.RightToLeft };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        var headerText = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft };
        headerText.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Right });
        headerText.Children.Add(new TextBlock { Text = "اطلاعات و درصدهای این کالا فقط در ماه فعال ثبت می‌شوند.", Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)), Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Right });
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(26, 19, 26, 18), Child = headerText };
        root.Children.Add(header);

        var outer = new StackPanel { Margin = new Thickness(26, 22, 26, 26), FlowDirection = FlowDirection.RightToLeft };
        var scroll = new ScrollViewer { Content = outer, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.RightToLeft }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        void Section(string title, string subtitle)
        {
            outer.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 13, 0, 4), TextAlignment = TextAlignment.Right });
            outer.Children.Add(new TextBlock { Text = subtitle, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 0, 0, 7), TextAlignment = TextAlignment.Right });
        }
        StackPanel FieldPanel(string label)
        {
            var panel = new StackPanel { Margin = new Thickness(6), FlowDirection = FlowDirection.RightToLeft };
            panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(52, 64, 84)), TextAlignment = TextAlignment.Right });
            return panel;
        }
        Grid FieldsGrid(int columns, int rows)
        {
            var grid = new Grid { FlowDirection = FlowDirection.RightToLeft };
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outer.Children.Add(grid); return grid;
        }
        void Place(UIElement element, Grid grid, int column, int row)
        {
            Grid.SetColumn(element, column); Grid.SetRow(element, row); grid.Children.Add(element);
        }
        void Field(Grid grid, int column, int row, string key, string label, string value)
        {
            var panel = FieldPanel(label); var tb = new TextBox { Text = value, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right };
            fields[key] = tb; panel.Children.Add(tb); Place(panel, grid, column, row); tb.TextChanged += (_, _) => { if (initialized) Preview(); };
            if (key is "price" or "quantity") MoneyInput.Attach(tb);
        }
        string Num(decimal x) => x.ToString("0.########", CultureInfo.InvariantCulture);

        Section("مشخصات کالا", "برند را انتخاب کنید یا نام برند تازه‌ای تایپ کنید.");
        var identity = FieldsGrid(2, 2);
        var brandPanel = FieldPanel("برند"); brand.ItemsSource = all.Select(x => x.Brand).Distinct().OrderBy(x => x).ToList(); brand.Text = original.Brand; brandPanel.Children.Add(brand); Place(brandPanel, identity, 0, 0);
        Field(identity, 1, 0, "name", "نام کالا", original.Name); Field(identity, 0, 1, "price", "قیمت خرید واحد — ریال", product == null ? "" : Rules.Money(original.Price)); Field(identity, 1, 1, "quantity", "تعداد / مقدار", Rules.Money(original.Quantity));

        Section("شرایط خرید", "تخفیف و آفر از مبلغ اولیهٔ خرید محاسبه می‌شوند.");
        var purchase = FieldsGrid(3, 1);
        Field(purchase, 2, 0, "discount", "تخفیف خرید ٪", Num(original.Discount * 100)); Field(purchase, 1, 0, "offer", "آفر خرید ٪", Num(original.Offer * 100)); Field(purchase, 0, 0, "markup", "سود اضافه ٪", Num(original.Markup * 100));

        Section("ترکیب فروش", "سهم فروش نقدی و چکی باید در مجموع ۱۰۰٪ باشد.");
        var sale = FieldsGrid(3, 1);
        Field(sale, 2, 0, "credit", "سهم فروش چکی ٪", Num(original.CreditShare * 100)); Field(sale, 1, 0, "cash", "سهم فروش نقدی ٪", Num(original.CashShare * 100)); Field(sale, 0, 0, "cashdiscount", "تخفیف نقدی ٪", Num(original.CashDiscount * 100));

        outer.Children.Add(error);
        var previewTitle = new TextBlock { Text = "پیش‌نمایش محاسبه", FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(143, 32, 32)), Margin = new Thickness(0, 0, 0, 5), TextAlignment = TextAlignment.Right };
        var previewBox = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 248, 248)), BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 198)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(15), Margin = new Thickness(0, 8, 0, 0), FlowDirection = FlowDirection.RightToLeft, Child = new StackPanel { FlowDirection = FlowDirection.RightToLeft, Children = { previewTitle, preview } } }; outer.Children.Add(previewBox);
        outer.Children.Add(new TextBlock { Text = "اعداد را بدون جداکننده یا با جداکنندهٔ رایج وارد کنید؛ محاسبه هم‌زمان انجام می‌شود.", Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 11, 0, 5), TextAlignment = TextAlignment.Right });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
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
        try { var r = Rules.Calculate(Read()); error.Text = ""; preview.Text = $"بهای تمام‌شده: {Rules.Money(r.Cost)} ریال\nفروش واحد: {Rules.Money(r.UnitSale)} ریال\nفروش کل: {Rules.Money(r.Sales)} ریال   |   سود: {Rules.Money(r.Profit)} ریال\nحاشیه سود: {Rules.Percent(r.Margin)}"; }
        catch (Exception ex) { error.Text = ex.Message; preview.Text = ""; }
    }
}

public sealed class MonthDialog : Window
{
    public string Key { get; private set; } = "";
    public MonthDialog(string suggested, bool copy, bool edit = false)
    {
        Title = edit ? "ویرایش ماه" : copy ? "کپی به ماه جدید" : "ماه جدید"; Width = 440; Height = 335; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid { FlowDirection = FlowDirection.RightToLeft }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        var header = new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(24, 18, 24, 17), Child = new TextBlock { Text = Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Right } }; root.Children.Add(header);
        var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 24), FlowDirection = FlowDirection.RightToLeft }; Grid.SetRow(panel, 1); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "ماه شمسی", FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right }); var input = new TextBox { Text = suggested, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right }; panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = edit ? "تغییر ماه، همهٔ کالاها و هزینهٔ ثابت همین ماه را حفظ می‌کند." : copy ? "کالاها و درصدها به‌صورت مستقل کپی می‌شوند." : "ماه خالی با هزینهٔ ثابت ماه جاری ساخته می‌شود.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 4, 0, 7), TextAlignment = TextAlignment.Right });
        var error = new TextBlock { Foreground = Brushes.Firebrick, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }; panel.Children.Add(error);
        var button = new Button { Content = edit ? "ثبت تغییر ماه" : "ایجاد ماه", IsDefault = true, Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(button);
        button.Click += (_, _) => { var k = Rules.Digits(input.Text); if (!Rules.ValidMonth(k)) { error.Text = "قالب ماه باید مانند 1405/06 باشد."; return; } Key = k; DialogResult = true; };
    }
}

public sealed class FixedExpensesDialog : Window
{
    readonly List<(TextBox Title, TextBox Amount)> rows = [];
    readonly StackPanel list = new() { FlowDirection = FlowDirection.RightToLeft };
    public List<FixedExpense>? Value { get; private set; }
    public FixedExpensesDialog(Month month)
    {
        Title = "ریز هزینه‌های ثابت"; Width = 680; Height = 520; MinWidth = 600; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(24, 18, 24, 16), FlowDirection = FlowDirection.RightToLeft, Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { new TextBlock { Text = "ریز هزینه‌های ثابت ماه", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Right }, new TextBlock { Text = "جمع ردیف‌ها، هزینه ثابت ماه را تعیین می‌کند.", Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 7, 0, 0), TextAlignment = TextAlignment.Right } } } });
        var body = new StackPanel { Margin = new Thickness(22), FlowDirection = FlowDirection.RightToLeft }; Grid.SetRow(body, 1); root.Children.Add(body);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 350 }; body.Children.Add(scroll);
        void Add(FixedExpense? item = null)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            var title = new TextBox { Text = item?.Title ?? "", Margin = new Thickness(4), ToolTip = "عنوان هزینه", FontSize = 14 };
            var amount = new TextBox { Text = item == null ? "" : Rules.Money(item.Amount), Margin = new Thickness(4), ToolTip = "مبلغ ریال", FontSize = 14 }; MoneyInput.Attach(amount);
            var remove = new Button { Content = "حذف", Margin = new Thickness(4), Padding = new Thickness(8, 8, 8, 8) };
            Grid.SetColumn(title, 0); Grid.SetColumn(amount, 1); Grid.SetColumn(remove, 2); row.Children.Add(title); row.Children.Add(amount); row.Children.Add(remove); list.Children.Add(row); rows.Add((title, amount));
            remove.Click += (_, _) => { list.Children.Remove(row); rows.RemoveAll(x => ReferenceEquals(x.Title, title)); };
        }
        foreach (var item in month.FixedExpenses) Add(item);
        if (rows.Count == 0 && month.FixedCost > 0) Add(new FixedExpense { Title = "هزینه ثابت ماه", Amount = month.FixedCost });
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; body.Children.Add(actions);
        var add = new Button { Content = "+ افزودن ردیف" }; actions.Children.Add(add); add.Click += (_, _) => Add();
        var save = new Button { Content = "ثبت هزینه‌ها", Style = (Style)FindResource("Primary") }; actions.Children.Add(save);
        save.Click += (_, _) =>
        {
            try
            {
                var values = rows.Where(x => !string.IsNullOrWhiteSpace(x.Title.Text) || !string.IsNullOrWhiteSpace(x.Amount.Text)).Select(x => new FixedExpense { Title = Rules.Normalize(x.Title.Text), Amount = Rules.Number(x.Amount.Text) }).ToList();
                if (values.Any(x => string.IsNullOrWhiteSpace(x.Title))) throw new InvalidDataException("عنوان همه هزینه‌ها الزامی است.");
                if (values.Any(x => x.Amount < 0)) throw new InvalidDataException("مبلغ هزینه نمی‌تواند منفی باشد.");
                Value = values; DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "ثبت هزینه‌ها", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
    }
}

public sealed class ImportReviewDialog : Window
{
    public List<Product> Products { get; }
    public ImportReviewDialog(ImportReview review, IReadOnlyCollection<Product> existing)
    {
        Title = "بازبینی ورود Excel"; Width = 700; Height = 560; MinHeight = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var existingKeys = existing.Select(x => Rules.Normalize(x.Brand) + "\u001f" + Rules.Normalize(x.Name)).ToHashSet(); var seen = new HashSet<string>();
        var duplicates = new List<string>();
        Products = review.Products.Where(p => { var key = Rules.Normalize(p.Brand) + "\u001f" + Rules.Normalize(p.Name); var duplicate = !seen.Add(key) || existingKeys.Contains(key); if (duplicate) duplicates.Add(p.Brand + " / " + p.Name); return !duplicate; }).ToList();
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); Content = root;
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(22, 17, 22, 15), Child = new TextBlock { Text = "بازبینی ورود Excel", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextAlignment = TextAlignment.Right } });
        var panel = new StackPanel { Margin = new Thickness(22), FlowDirection = FlowDirection.RightToLeft }; Grid.SetRow(panel, 1); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = $"ردیف قابل ورود: {Products.Count}   |   ردیف خطادار: {review.Issues.Count}   |   تکراری: {duplicates.Count}", FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right });
        panel.Children.Add(new TextBlock { Text = "هزینه ثابت از فایل Excel وارد نمی‌شود.", Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), Margin = new Thickness(0, 6, 0, 8), TextAlignment = TextAlignment.Right });
        var details = review.Issues.Select(x => "ردیف " + x.Row + ": " + x.Message).Concat(duplicates.Select(x => "تکراری: " + x)).Take(100);
        panel.Children.Add(new TextBox { Text = string.Join(Environment.NewLine, details.DefaultIfEmpty("خطایی در بازبینی پیدا نشد.")), IsReadOnly = true, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 280, TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        var confirm = new Button { Content = "افزودن ردیف‌های معتبر", IsEnabled = Products.Count > 0, Style = (Style)FindResource("Primary") }; buttons.Children.Add(confirm); confirm.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(new Button { Content = "انصراف", IsCancel = true });
    }
}
