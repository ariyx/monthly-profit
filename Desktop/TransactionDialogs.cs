using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

static class DialogUi
{
    public static TextBox Input(string value = "") => new() { Text = value, FlowDirection = FlowDirection.LeftToRight, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 3, 0, 8) };
    public static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static string Today()
    {
        var c = new PersianCalendar(); var d = DateTime.Today; return $"{c.GetYear(d):0000}{c.GetMonth(d):00}{c.GetDayOfMonth(d):00}";
    }
    public static decimal Percent(TextBox input) => Rules.Number(input.Text) / 100m;
    public static void Percent(TextBox input, decimal value) => input.Text = (value * 100m).ToString("0.##", CultureInfo.InvariantCulture);
    public static Border Header(string title, string note) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(24, 18, 24, 16),
        Child = new StackPanel { FlowDirection = FlowDirection.LeftToRight, Children =
        {
            new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch },
            new TextBlock { Text = note, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch, TextWrapping = TextWrapping.Wrap }
        }}
    };
}

public sealed class BrandManagerDialog : Window
{
    sealed class BrandGridRow
    {
        public required Brand Value { get; init; }
        public string Name => Value.Name;
        public string Prefixes => BrandPrefixRules.Display(Value.CodePrefixes);
        public string PurchaseDiscount => Rules.Percent(Value.PurchaseDiscount);
        public string Offer => Rules.Percent(Value.Offer);
        public string Markup => Rules.Percent(Value.Markup);
        public string CashShare => Rules.Percent(Value.CashShare);
        public string CreditShare => Rules.Percent(Value.CreditShare);
        public string CashDiscount => Rules.Percent(Value.CashDiscount);
    }
    readonly List<Brand> brands;
    readonly DataGrid grid = new();
    public List<Brand>? Value { get; private set; }
    public BrandManagerDialog(IEnumerable<Brand> source)
    {
        Title = "مدیریت برندها"; Width = 1020; Height = 600; MinWidth = 820; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir"); brands = source.OrderBy(x => x.Name).ToList();
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("مدیریت برندها", "پیشوند کد، برند کالاهای ورودی را تعیین می‌کند؛ درصدها برای ثبت‌های بعدی همان برند استفاده می‌شوند."));
        grid.AutoGenerateColumns = false; grid.Margin = new Thickness(22); grid.Columns.Add(new DataGridTextColumn { Header = "برند", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1.35, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "پیشوند کد", Binding = new System.Windows.Data.Binding("Prefixes"), Width = new DataGridLength(1.1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "تخفیف خرید٪", Binding = new System.Windows.Data.Binding("PurchaseDiscount"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "آفر٪", Binding = new System.Windows.Data.Binding("Offer"), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "سود٪", Binding = new System.Windows.Data.Binding("Markup"), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "نقدی٪", Binding = new System.Windows.Data.Binding("CashShare"), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "چکی٪", Binding = new System.Windows.Data.Binding("CreditShare"), Width = new DataGridLength(.7, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "تخفیف نقدی٪", Binding = new System.Windows.Data.Binding("CashDiscount"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        Grid.SetRow(grid, 1); root.Children.Add(grid);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 0, 22, 18) }; Grid.SetRow(actions, 2); root.Children.Add(actions);
        var add = new Button { Content = "+ افزودن برند", Style = (Style)FindResource("Primary") }; var edit = new Button { Content = "ویرایش" }; var remove = new Button { Content = "حذف" }; var save = new Button { Content = "ثبت تغییرات" }; actions.Children.Add(add); actions.Children.Add(edit); actions.Children.Add(remove); actions.Children.Add(save);
        void Draw() => grid.ItemsSource = brands.Select(x => new BrandGridRow { Value = x }).ToList();
        Brand? Selected() => (grid.SelectedItem as BrandGridRow)?.Value;
        add.Click += (_, _) => { var d = new BrandEditorDialog(null) { Owner = this }; if (d.ShowDialog() == true && d.Value != null) { if (brands.Any(x => Rules.Normalize(x.Name) == Rules.Normalize(d.Value.Name))) { MessageBox.Show(this, "این برند قبلاً ثبت شده است."); return; } brands.Add(d.Value); Draw(); } };
        edit.Click += (_, _) => { var old = Selected(); if (old == null) return; var d = new BrandEditorDialog(old) { Owner = this }; if (d.ShowDialog() == true && d.Value != null) { brands[brands.IndexOf(old)] = d.Value; Draw(); } };
        remove.Click += (_, _) => { var item = Selected(); if (item != null && MessageBox.Show(this, "برند انتخاب‌شده حذف شود؟", "حذف برند", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { brands.Remove(item); Draw(); } };
        save.Click += (_, _) => { try { foreach (var brand in brands) Rules.Validate(brand); Value = brands; DialogResult = true; } catch (Exception ex) { MessageBox.Show(this, ex.Message); } };
        Draw();
    }
}

public sealed class BrandEditorDialog : Window
{
    public Brand? Value { get; private set; }
    public BrandEditorDialog(Brand? brand)
    {
        Title = brand == null ? "افزودن برند" : "ویرایش برند"; Width = 520; Height = 710; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var panel = new StackPanel { Margin = new Thickness(26) }; Content = panel;
        panel.Children.Add(DialogUi.Label("نام برند")); var name = DialogUi.Input(brand?.Name ?? ""); panel.Children.Add(name);
        panel.Children.Add(DialogUi.Label("پیشوند کد کالا")); var prefixes = DialogUi.Input(BrandPrefixRules.Display(brand?.CodePrefixes ?? [])); prefixes.ToolTip = "مثال: 106 یا 115، 145"; panel.Children.Add(prefixes);
        panel.Children.Add(new TextBlock { Text = "هر پیشوند را فقط با رقم وارد کنید؛ چند پیشوند را با «،» جدا کنید. مثلاً 106 همهٔ کدهای شروع‌شونده با 106 را به این برند متصل می‌کند.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, -3, 0, 7) });
        var inputs = new Dictionary<string, TextBox>();
        void Rate(string key, string label, decimal value) { panel.Children.Add(DialogUi.Label(label)); var t = DialogUi.Input(); DialogUi.Percent(t, value); inputs[key] = t; panel.Children.Add(t); }
        Rate("discount", "تخفیف خرید ٪", brand?.PurchaseDiscount ?? 0); Rate("offer", "آفر خرید ٪", brand?.Offer ?? 0); Rate("markup", "سود / مارک‌آپ ٪", brand?.Markup ?? .04m); Rate("cash", "سهم فروش نقدی ٪", brand?.CashShare ?? .30m); Rate("credit", "سهم فروش چکی ٪", brand?.CreditShare ?? .70m); Rate("cashdiscount", "تخفیف نقدی ٪", brand?.CashDiscount ?? .05m);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }; panel.Children.Add(error); var save = new Button { Content = "ثبت برند", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(save);
        save.Click += (_, _) => { try { Value = new Brand { Name = Rules.Normalize(name.Text), CodePrefixes = BrandPrefixRules.Parse(prefixes.Text), PurchaseDiscount = DialogUi.Percent(inputs["discount"]), Offer = DialogUi.Percent(inputs["offer"]), Markup = DialogUi.Percent(inputs["markup"]), CashShare = DialogUi.Percent(inputs["cash"]), CreditShare = DialogUi.Percent(inputs["credit"]), CashDiscount = DialogUi.Percent(inputs["cashdiscount"]) }; Rules.Validate(Value); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
}

public sealed class PurchaseDialog : Window
{
    public CatalogItem? Item { get; private set; }
    public Purchase? Value { get; private set; }
    public PurchaseDialog(Ledger ledger, bool adjustment = false, string? code = null, decimal quantity = 0, decimal suggestedUnitPrice = 0, string? dateValue = null)
    {
        Title = adjustment ? "تعدیل موجودی" : "ثبت خرید"; Width = 640; Height = 690; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var panel = new StackPanel { Margin = new Thickness(26) }; Content = panel;
        panel.Children.Add(DialogUi.Label("تاریخ (14050421)")); var date = DialogUi.Input(dateValue ?? DialogUi.Today()); panel.Children.Add(date);
        panel.Children.Add(DialogUi.Label("کد کالا")); var codeBox = DialogUi.Input(code ?? ""); panel.Children.Add(codeBox);
        panel.Children.Add(DialogUi.Label("نام کالا")); var name = DialogUi.Input(); panel.Children.Add(name);
        panel.Children.Add(DialogUi.Label("برند")); var brand = DialogUi.Input(); panel.Children.Add(brand);
        panel.Children.Add(DialogUi.Label("تأمین‌کننده")); var supplier = DialogUi.Input(adjustment ? "تعدیل دستی" : ""); panel.Children.Add(supplier);
        panel.Children.Add(DialogUi.Label("تعداد")); var qty = DialogUi.Input(quantity == 0 ? "" : Rules.Money(quantity)); panel.Children.Add(qty);
        panel.Children.Add(DialogUi.Label("قیمت خرید واحد — ریال")); var unit = DialogUi.Input(suggestedUnitPrice == 0 ? "" : Rules.Money(suggestedUnitPrice)); panel.Children.Add(unit);
        panel.Children.Add(DialogUi.Label("کسورات — ریال")); var deductions = DialogUi.Input("0"); panel.Children.Add(deductions);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }; panel.Children.Add(error); var save = new Button { Content = adjustment ? "ثبت تعدیل" : "ثبت خرید", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(save);
        void Fill()
        {
            var normalizedCode = Rules.Normalize(codeBox.Text);
            var existing = ledger.Items.FirstOrDefault(x => x.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { name.Text = existing.Name; brand.Text = existing.Brand; return; }
            var detected = BrandPrefixRules.Detect(ledger.Brands, normalizedCode);
            if (detected != null) brand.Text = detected.Name;
        }
        codeBox.LostFocus += (_, _) => Fill(); Fill();
        save.Click += (_, _) => { try { var normalizedCode = Rules.Normalize(codeBox.Text); var selectedBrand = ledger.Brands.FirstOrDefault(x => Rules.Normalize(x.Name) == Rules.Normalize(brand.Text)) ?? throw new InvalidDataException("ابتدا برند کالا را در بخش مدیریت برند ثبت کنید."); var q = Rules.Number(qty.Text); var price = Rules.Number(unit.Text); var total = q * price; Item = new CatalogItem { Code = normalizedCode, Name = Rules.Normalize(name.Text), Brand = selectedBrand.Name }; Rules.Validate(Item); Value = new Purchase { Date = Rules.Digits(date.Text), Code = Item.Code, Supplier = Rules.Normalize(supplier.Text), Quantity = q, UnitPrice = price, Total = total, Deductions = Rules.Number(deductions.Text), BrandDiscount = adjustment ? 0 : selectedBrand.PurchaseDiscount, Offer = adjustment ? 0 : selectedBrand.Offer, IsAdjustment = adjustment, Note = adjustment ? "تعدیل موجودی ناشی از مغایرت" : "ثبت دستی" }; Rules.Validate(Value); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
}

public sealed class SaleDialog : Window
{
    public Sale? Value { get; private set; }
    public SaleDialog(Ledger ledger)
    {
        Title = "ثبت فروش"; Width = 640; Height = 610; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.RightToLeft; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var panel = new StackPanel { Margin = new Thickness(26) }; Content = panel;
        panel.Children.Add(DialogUi.Label("تاریخ (14050421)")); var date = DialogUi.Input(DialogUi.Today()); panel.Children.Add(date);
        panel.Children.Add(DialogUi.Label("کد کالا")); var code = DialogUi.Input(); panel.Children.Add(code);
        panel.Children.Add(DialogUi.Label("مشتری")); var customer = DialogUi.Input(); panel.Children.Add(customer);
        panel.Children.Add(DialogUi.Label("تعداد")); var qty = DialogUi.Input(); panel.Children.Add(qty);
        panel.Children.Add(DialogUi.Label("قیمت فروش واحد — ریال")); var unit = DialogUi.Input(); panel.Children.Add(unit);
        panel.Children.Add(DialogUi.Label("کسورات — ریال")); var deductions = DialogUi.Input("0"); panel.Children.Add(deductions);
        var hint = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }; panel.Children.Add(hint);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }; panel.Children.Add(error); var save = new Button { Content = "ثبت فروش", Style = (Style)FindResource("Primary"), HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(save);
        Brand? CurrentBrand()
        {
            var item = ledger.Items.FirstOrDefault(x => x.Code.Equals(Rules.Normalize(code.Text), StringComparison.OrdinalIgnoreCase)); return item == null ? null : ledger.Brands.FirstOrDefault(x => Rules.Normalize(x.Name) == Rules.Normalize(item.Brand));
        }
        void Suggest()
        {
            var item = ledger.Items.FirstOrDefault(x => x.Code.Equals(Rules.Normalize(code.Text), StringComparison.OrdinalIgnoreCase)); var profile = CurrentBrand(); if (item == null || profile == null) { hint.Text = "برای این کد کالا، ابتدا خرید یا تعدیل موجودی ثبت کنید."; return; }
            var latest = ledger.Purchases.Where(x => x.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Date).ThenByDescending(x => x.Id).FirstOrDefault();
            var cost = latest == null ? ledger.OpeningLots.Where(x => x.Code.Equals(item.Code, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.SourceDate).ThenByDescending(x => x.Id).Select(x => x.UnitCost).FirstOrDefault() : Rules.NetPurchase(latest) / latest.Quantity;
            if (cost == 0) return;
            unit.Text = Rules.Money(cost * (1 + profile.Markup)); hint.Text = $"برند: {item.Brand} · قیمت پیشنهادی بر پایه آخرین قیمت خرید و سود برند.";
        }
        code.LostFocus += (_, _) => Suggest();
        save.Click += (_, _) => { try { var item = ledger.Items.FirstOrDefault(x => x.Code.Equals(Rules.Normalize(code.Text), StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("کد کالا ناشناخته است. ابتدا تعدیل موجودی آن را ثبت کنید."); var profile = ledger.Brands.FirstOrDefault(x => Rules.Normalize(x.Name) == Rules.Normalize(item.Brand)) ?? throw new InvalidDataException("برند کالا یافت نشد."); var q = Rules.Number(qty.Text); var price = Rules.Number(unit.Text); Value = new Sale { Date = Rules.Digits(date.Text), Code = item.Code, Customer = Rules.Normalize(customer.Text), Quantity = q, UnitPrice = price, Total = q * price, Deductions = Rules.Number(deductions.Text), CashShare = profile.CashShare, CreditShare = profile.CreditShare, CashDiscount = profile.CashDiscount }; Rules.Validate(Value); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
}
