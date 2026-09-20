using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

static class DialogUi
{
    public static TextBox Input(string value = "") => new() { Text = value, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 3, 0, 9) };
    public static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static decimal Percent(TextBox input) => Rules.Number(input.Text) / 100m;
    public static void Percent(TextBox input, decimal value) => input.Text = (value * 100m).ToString("0.##", CultureInfo.InvariantCulture);
    public static Border Header(string title, string note) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(24, 18, 24, 16),
        Child = new StackPanel { Children =
        {
            new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left },
            new TextBlock { Text = note, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 6, 0, 0), FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap }
        }}
    };
    public static Window Dialog(string title, double width, double height) => new() { Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 600), MinHeight = Math.Min(height, 380), WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.LeftToRight, FontFamily = (FontFamily)Application.Current.FindResource("Vazir") };
}

public sealed class BrandEditorDialog : Window
{
    public Brand? Value { get; private set; }
    public BrandEditorDialog(Brand? value)
    {
        Title = value == null ? "افزودن برند" : "ویرایش برند";
        Width = 560; Height = 340; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header(Title, "درصدهای مالی در تأیید ماهانه ثبت می‌شوند؛ اینجا فقط نام و پیشوند کالا را تعیین کنید."));
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) }; Grid.SetRow(body, 1); root.Children.Add(body);
        body.Children.Add(DialogUi.Label("نام برند")); var name = DialogUi.Input(value?.Name ?? ""); body.Children.Add(name);
        body.Children.Add(DialogUi.Label("پیشوند کد کالا (با «،» جدا کنید)")); var prefixes = DialogUi.Input(value == null ? "" : BrandPrefixRules.Display(value.CodePrefixes)); body.Children.Add(prefixes);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left }; body.Children.Add(error);
        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var save = new Button { Content = "ثبت برند", Style = (Style)FindResource("Primary"), IsDefault = true }; footer.Children.Add(save); footer.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        save.Click += (_, _) =>
        {
            try
            {
                Value = (value ?? new Brand { Markup = .04m, CashShare = .30m, CreditShare = .70m, CashDiscount = .05m }) with { Name = Rules.Normalize(name.Text), CodePrefixes = BrandPrefixRules.Parse(prefixes.Text) };
                Rules.Validate(Value); DialogResult = true;
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
    }
}

public sealed class SaleDialog : Window
{
    readonly Ledger ledger;
    readonly TextBox date; readonly TextBox code; readonly TextBox name; readonly TextBox customer; readonly TextBox quantity; readonly TextBox price; readonly TextBox deductions;
    readonly TextBlock preview;
    public Sale? Value { get; private set; }
    public CatalogItem? Item { get; private set; }
    public SaleDialog(Ledger ledger, string dateValue)
    {
        this.ledger = ledger; Title = "ثبت فروش"; Width = 780; Height = 690; MinHeight = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("ثبت فروش", "هزینه خرید این فروش خودکار از قیمت فروش و درصدهای برند همان ماه استخراج می‌شود."));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(24, 18, 24, 8) }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var body = new StackPanel(); scroll.Content = body;
        (TextBox, TextBox) Field(string label, string value = "") { body.Children.Add(DialogUi.Label(label)); var input = DialogUi.Input(value); body.Children.Add(input); return (input, input); }
        date = Field("تاریخ (مانند 14050627)", dateValue).Item1; code = Field("کد کالا").Item1; name = Field("نام کالا").Item1; customer = Field("مشتری").Item1; quantity = Field("تعداد").Item1; price = Field("قیمت فروش واحد — ریال").Item1; deductions = Field("کسورات فاکتور — ریال", "0").Item1;
        MoneyInput.Attach(quantity); MoneyInput.Attach(price); MoneyInput.Attach(deductions);
        preview = new TextBlock { Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), Padding = new Thickness(14), TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, Margin = new Thickness(0, 8, 0, 8) }; body.Children.Add(preview);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left }; body.Children.Add(error);
        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var save = new Button { Content = "ثبت فروش", Style = (Style)FindResource("Primary"), IsDefault = true }; footer.Children.Add(save); footer.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        void Refresh() { try { preview.Text = Preview(Read(out _)); error.Text = ""; } catch (Exception ex) { preview.Text = ""; error.Text = ex.Message; } }
        foreach (var input in new[] { date, code, name, customer, quantity, price, deductions }) input.TextChanged += (_, _) => Refresh();
        save.Click += (_, _) => { try { Value = Read(out var item); Item = item; DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
    Sale Read(out CatalogItem item)
    {
        var cleanDate = Rules.Digits(date.Text); var cleanCode = Rules.Normalize(code.Text); var cleanName = Rules.Normalize(name.Text);
        if (!Rules.ValidDate(cleanDate) || string.IsNullOrWhiteSpace(cleanCode) || string.IsNullOrWhiteSpace(cleanName)) throw new InvalidDataException("تاریخ، کد و نام کالا الزامی هستند.");
        var found = ledger.Items.FirstOrDefault(x => x.Code.Equals(cleanCode, StringComparison.OrdinalIgnoreCase));
        var brand = found?.Brand ?? BrandPrefixRules.Detect(ledger.Brands, cleanCode)?.Name ?? throw new InvalidOperationException("برای این کد، برند تعیین نشده است. ابتدا برند یا پیشوند آن را مشخص کنید.");
        item = found ?? new CatalogItem { Code = cleanCode, Name = cleanName, Brand = brand };
        if (!Rules.Normalize(item.Brand).Equals(Rules.Normalize(brand), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("کد کالا با برند دیگری ثبت شده است.");
        var rate = BrandMonthRules.RequireConfirmed(ledger, brand, Rules.MonthOf(cleanDate));
        var qty = Rules.Number(quantity.Text); var unit = Rules.Number(price.Text); var total = qty * unit;
        var sale = BrandMonthRules.Apply(new Sale { Date = cleanDate, Code = cleanCode, Customer = Rules.Normalize(customer.Text), Quantity = qty, UnitPrice = unit, Total = total, Deductions = Rules.Number(deductions.Text) }, rate, Rules.MonthOf(cleanDate));
        Rules.Validate(sale); return sale;
    }
    static string Preview(Sale sale)
    {
        var value = LedgerCalculator.PreviewSale(sale);
        return $"قیمت خرید اولیهٔ تخمینی واحد: {Rules.ReportMoney(value.GrossPurchaseUnit)} ریال\nهزینه خالص تخمینی واحد: {Rules.ReportMoney(value.NetCostUnit)} ریال\nدریافتی واقعی: {Rules.ReportMoney(value.Sales)} ریال\nهزینه کل تخمینی: {Rules.ReportMoney(value.Cost)} ریال\nسود خالص این فروش: {Rules.ReportMoney(value.Profit)} ریال";
    }
}

public sealed class SaleEditorDialog : Window
{
    readonly Ledger ledger; readonly Sale original; readonly TextBox date; readonly TextBox customer; readonly TextBox quantity; readonly TextBox price; readonly TextBox deductions; readonly CheckBox manual; readonly TextBox manualCost; readonly TextBox manualReason; readonly TextBlock preview;
    public Sale? Value { get; private set; }
    public SaleEditorDialog(Ledger ledger, Sale sale)
    {
        this.ledger = ledger; original = sale; Title = "ویرایش فروش"; Width = 780; Height = 730; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("ویرایش فروش", $"کد کالا: {sale.Code} · هزینه این فروش از قیمت خودش محاسبه می‌شود."));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(24, 18, 24, 8) }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var body = new StackPanel(); scroll.Content = body;
        TextBox Field(string label, string value) { body.Children.Add(DialogUi.Label(label)); var input = DialogUi.Input(value); body.Children.Add(input); return input; }
        date = Field("تاریخ", sale.Date); customer = Field("مشتری", sale.Customer); quantity = Field("تعداد", Rules.Money(sale.Quantity)); price = Field("قیمت فروش واحد — ریال", Rules.Money(sale.UnitPrice)); deductions = Field("کسورات فاکتور — ریال", Rules.Money(sale.Deductions));
        MoneyInput.Attach(quantity); MoneyInput.Attach(price); MoneyInput.Attach(deductions);
        manual = new CheckBox { Content = "هزینه تخمینی این فروش را دستی ثبت می‌کنم.", IsChecked = sale.EstimatedCostOverride.HasValue, FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 0, 8) }; body.Children.Add(manual);
        manualCost = Field("هزینه کل دستی — ریال", sale.EstimatedCostOverride.HasValue ? Rules.Money(sale.EstimatedCostOverride.Value) : ""); manualReason = Field("دلیل هزینه دستی", sale.EstimatedCostOverrideNote); MoneyInput.Attach(manualCost);
        preview = new TextBlock { Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)), Padding = new Thickness(14), TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left, Margin = new Thickness(0, 8, 0, 8) }; body.Children.Add(preview);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left }; body.Children.Add(error);
        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var save = new Button { Content = "ثبت تغییرات", Style = (Style)FindResource("Primary"), IsDefault = true }; footer.Children.Add(save); footer.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        void Refresh() { manualCost.IsEnabled = manualReason.IsEnabled = manual.IsChecked == true; try { preview.Text = Preview(Read()); error.Text = ""; } catch (Exception ex) { preview.Text = ""; error.Text = ex.Message; } }
        foreach (var input in new[] { date, customer, quantity, price, deductions, manualCost, manualReason }) input.TextChanged += (_, _) => Refresh(); manual.Checked += (_, _) => Refresh(); manual.Unchecked += (_, _) => Refresh();
        save.Click += (_, _) => { try { Value = Read(); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } }; Refresh();
    }
    Sale Read()
    {
        var cleanDate = Rules.Digits(date.Text); var item = ledger.Items.FirstOrDefault(x => x.Code.Equals(original.Code, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("کالای فروش یافت نشد.");
        var month = Rules.MonthOf(cleanDate); var rate = BrandMonthRules.RequireConfirmed(ledger, item.Brand, month);
        var qty = Rules.Number(quantity.Text); var unit = Rules.Number(price.Text);
        decimal? overrideCost = manual.IsChecked == true ? Rules.Number(manualCost.Text) : null;
        var next = BrandMonthRules.Apply(original with { Date = cleanDate, Customer = Rules.Normalize(customer.Text), Quantity = qty, UnitPrice = unit, Total = qty * unit, Deductions = Rules.Number(deductions.Text), EstimatedCostOverride = overrideCost, EstimatedCostOverrideNote = manual.IsChecked == true ? Rules.Normalize(manualReason.Text) : "" }, rate, month);
        Rules.Validate(next); return next;
    }
    static string Preview(Sale sale) { var value = LedgerCalculator.PreviewSale(sale); return $"قیمت خرید اولیهٔ تخمینی واحد: {Rules.ReportMoney(value.GrossPurchaseUnit)} ریال\nهزینه خالص تخمینی واحد: {Rules.ReportMoney(value.NetCostUnit)} ریال\nدریافتی واقعی: {Rules.ReportMoney(value.Sales)} ریال\nهزینه کل: {Rules.ReportMoney(value.Cost)} ریال ({(value.HasManualCost ? "دستی" : "خودکار")})\nسود خالص این فروش: {Rules.ReportMoney(value.Profit)} ریال"; }
}

public sealed class FixedExpensesDialog : Window
{
    readonly List<(TextBox Title, TextBox Amount)> rows = [];
    readonly StackPanel list = new();
    public List<FixedExpense>? Value { get; private set; }
    public FixedExpensesDialog(Month month)
    {
        Title = "ریز هزینه‌های ثابت"; Width = 780; Height = 520; MinHeight = 430; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("ریز هزینه‌های ثابت ماه", "جمع ردیف‌ها، هزینه ثابت ماه را تعیین می‌کند."));
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(24, 16, 24, 8), Content = list }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        void Add(FixedExpense? item = null)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var remove = new Button { Content = "حذف", Margin = new Thickness(4) }; var amount = DialogUi.Input(item == null ? "" : Rules.Money(item.Amount)); var title = DialogUi.Input(item?.Title ?? ""); MoneyInput.Attach(amount);
            Grid.SetColumn(remove, 0); Grid.SetColumn(amount, 1); Grid.SetColumn(title, 2); row.Children.Add(remove); row.Children.Add(amount); row.Children.Add(title); list.Children.Add(row); rows.Add((title, amount));
            remove.Click += (_, _) => { list.Children.Remove(row); rows.RemoveAll(x => ReferenceEquals(x.Title, title)); };
        }
        foreach (var item in month.FixedExpenses) Add(item); if (rows.Count == 0 && month.FixedCost > 0) Add(new FixedExpense { Title = "هزینه ثابت ماه", Amount = month.FixedCost });
        var footer = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 0, 20, 16) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var save = new Button { Content = "ثبت هزینه‌ها", Style = (Style)FindResource("Primary"), IsDefault = true }; footer.Children.Add(save); var add = new Button { Content = "+ افزودن ردیف" }; footer.Children.Add(add); footer.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        add.Click += (_, _) => Add(); save.Click += (_, _) => { try { Value = rows.Where(x => !string.IsNullOrWhiteSpace(x.Title.Text) || !string.IsNullOrWhiteSpace(x.Amount.Text)).Select(x => new FixedExpense { Title = Rules.Normalize(x.Title.Text), Amount = Rules.Number(x.Amount.Text) }).ToList(); if (Value.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Amount < 0)) throw new InvalidDataException("عنوان و مبلغ هزینه نامعتبر است."); DialogResult = true; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "هزینه ثابت", MessageBoxButton.OK, MessageBoxImage.Warning); } };
    }
}
