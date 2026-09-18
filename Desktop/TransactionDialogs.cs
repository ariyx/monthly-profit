using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

static class DialogUi
{
    public static TextBox Input(string value = "") => new() { Text = value, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 3, 0, 8) };
    public static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft };
    public static string Today()
    {
        var c = new PersianCalendar(); var d = DateTime.Today; return $"{c.GetYear(d):0000}{c.GetMonth(d):00}{c.GetDayOfMonth(d):00}";
    }
    public static decimal Percent(TextBox input) => Rules.Number(input.Text) / 100m;
    public static void Percent(TextBox input, decimal value) => input.Text = (value * 100m).ToString("0.##", CultureInfo.InvariantCulture);
    public static Border Header(string title, string note) => new()
    {
        Background = new SolidColorBrush(Color.FromRgb(21, 26, 37)), Padding = new Thickness(24, 18, 24, 16),
        Child = new StackPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch, Children =
        {
            new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft },
            new TextBlock { Text = note, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 6, 0, 0), TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch, FlowDirection = FlowDirection.RightToLeft, TextWrapping = TextWrapping.Wrap }
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
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir"); brands = source.OrderBy(x => x.Name).ToList();
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
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid { FlowDirection = FlowDirection.LeftToRight };
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bodyScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var panel = new StackPanel { Margin = new Thickness(26, 20, 26, 12), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch };
        bodyScroll.Content = panel; Grid.SetRow(bodyScroll, 0); root.Children.Add(bodyScroll);
        var footer = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(228, 232, 239)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(26, 10, 26, 12) };
        Grid.SetRow(footer, 1); root.Children.Add(footer); Content = root;
        panel.Children.Add(DialogUi.Label("نام برند")); var name = DialogUi.Input(brand?.Name ?? ""); panel.Children.Add(name);
        panel.Children.Add(DialogUi.Label("پیشوند کد کالا")); var prefixes = DialogUi.Input(BrandPrefixRules.Display(brand?.CodePrefixes ?? [])); prefixes.ToolTip = "مثال: 106 یا 115، 145"; panel.Children.Add(prefixes);
        panel.Children.Add(new TextBlock { Text = "هر پیشوند را فقط با رقم وارد کنید؛ چند پیشوند را با «،» جدا کنید. مثلاً 106 همهٔ کدهای شروع‌شونده با 106 را به این برند متصل می‌کند.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, -3, 0, 7) });
        var inputs = new Dictionary<string, TextBox>();
        void Rate(string key, string label, decimal value) { panel.Children.Add(DialogUi.Label(label)); var t = DialogUi.Input(); DialogUi.Percent(t, value); inputs[key] = t; panel.Children.Add(t); }
        Rate("discount", "تخفیف خرید ٪", brand?.PurchaseDiscount ?? 0); Rate("offer", "آفر خرید ٪", brand?.Offer ?? 0); Rate("markup", "سود / مارک‌آپ ٪", brand?.Markup ?? .04m); Rate("cash", "سهم فروش نقدی ٪", brand?.CashShare ?? .30m); Rate("credit", "سهم فروش چکی ٪", brand?.CreditShare ?? .70m); Rate("cashdiscount", "تخفیف نقدی ٪", brand?.CashDiscount ?? .05m);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(error);
        var actions = new WrapPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "ثبت برند", Style = (Style)FindResource("Primary") }; actions.Children.Add(save); footer.Child = actions;
        save.Click += (_, _) => { try { Value = new Brand { Name = Rules.Normalize(name.Text), CodePrefixes = BrandPrefixRules.Parse(prefixes.Text), PurchaseDiscount = DialogUi.Percent(inputs["discount"]), Offer = DialogUi.Percent(inputs["offer"]), Markup = DialogUi.Percent(inputs["markup"]), CashShare = DialogUi.Percent(inputs["cash"]), CreditShare = DialogUi.Percent(inputs["credit"]), CashDiscount = DialogUi.Percent(inputs["cashdiscount"]) }; Rules.Validate(Value); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
}

public sealed record PendingBrandResolution(string BrandName, string Prefix, bool ApplyToPrefix);

// صف کدهای ناشناخته فقط نقطهٔ ورود است؛ هر تصمیم در این پنجره و برای یک کالا گرفته می‌شود.
public sealed class PendingBrandDialog : Window
{
    readonly PendingBrandGridRow selected;
    readonly List<PendingBrandTransaction> pending;
    public PendingBrandResolution? Value { get; private set; }

    public PendingBrandDialog(IEnumerable<Brand> brands, PendingBrandGridRow selected, IEnumerable<PendingBrandTransaction> pending)
    {
        this.selected = selected; this.pending = pending.ToList();
        Title = "تعیین برند کالا"; Width = 700; Height = 590; MinWidth = 600; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("تعیین برند کد ناشناخته", "ابتدا برند را انتخاب کنید. فقط در صورتی که از الگوی کد مطمئن هستید، قانون پیشوند را هم ثبت کنید."));

        var panel = new StackPanel { Margin = new Thickness(26, 20, 26, 12), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(panel, 1); root.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = $"کد کالا: {selected.Code}", FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right });
        panel.Children.Add(new TextBlock { Text = selected.Name, FontSize = 15, Margin = new Thickness(0, 5, 0, 3), TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"در صف: {selected.PurchaseRows} ردیف خرید و {selected.SaleRows} ردیف فروش · اولین تاریخ: {selected.FirstDate}", Foreground = new SolidColorBrush(Color.FromRgb(84, 98, 124)), TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap });

        panel.Children.Add(new TextBlock { Text = "برند ثبت‌شده", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 22, 0, 2), TextAlignment = TextAlignment.Right });
        // این کنترل فقط باید یک برندِ ثبت‌شده را انتخاب کند. Editable بودن آن باعث
        // می‌شد بخش TextBoxِ قالب سفارشی، مقدار SelectedItem را در بعضی محیط‌ها
        // نمایش ندهد. حالت انتخابیِ معمولی مقدار انتخاب‌شده را همیشه نشان می‌دهد
        // و همچنان با تایپِ ابتدای نام، انتخاب سریع در فهرست ممکن است.
        var brand = new ComboBox { IsEditable = false, IsTextSearchEnabled = true, ItemsSource = brands.OrderBy(x => x.Name).Select(x => x.Name).ToList(), HorizontalContentAlignment = HorizontalAlignment.Right, Height = 34, ToolTip = "یک برند ثبت‌شده را انتخاب کنید" };
        panel.Children.Add(brand);
        panel.Children.Add(new TextBlock { Text = "اگر برند موردنظر وجود ندارد، ابتدا آن را از دکمه «+ افزودن برند» با درصدهایش ثبت کنید.", Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });

        var defaultPrefix = Rules.Digits(selected.Code); defaultPrefix = defaultPrefix[..Math.Min(3, defaultPrefix.Length)];
        var applyPrefix = new CheckBox { Content = "همه کدهای ناشناخته با این پیشوند را هم پردازش کن و قانون برند را ذخیره کن", Margin = new Thickness(0, 6, 0, 5), HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(applyPrefix);
        var prefix = DialogUi.Input(defaultPrefix); prefix.ToolTip = "مثال: 106"; prefix.IsEnabled = false; panel.Children.Add(prefix);
        var impact = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(54, 74, 104)), TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }; panel.Children.Add(impact);
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(error);

        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(26, 0, 26, 18) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var cancel = new Button { Content = "انصراف" }; var apply = new Button { Content = "ثبت و پردازش", Style = (Style)FindResource("Primary") }; footer.Children.Add(apply); footer.Children.Add(cancel);

        void UpdateImpact()
        {
            prefix.IsEnabled = applyPrefix.IsChecked == true;
            if (applyPrefix.IsChecked != true)
            {
                impact.Text = $"فقط همین کد و {selected.Values.Count} ردیف مرتبط آن پردازش می‌شود؛ هیچ قانون سراسری ثبت نخواهد شد.";
                return;
            }
            var values = BrandPrefixRules.Parse(prefix.Text);
            if (values.Count != 1) { impact.Text = "برای قانون سراسری، یک پیشوند عددی وارد کنید."; return; }
            var targets = pending.Where(x => Rules.Digits(x.Transaction.Code).StartsWith(values[0], StringComparison.Ordinal)).ToList();
            var codes = targets.Select(x => x.Transaction.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            impact.Text = $"با پیشوند «{values[0]}»، {codes} کد و {targets.Count} ردیفِ در صف با این برند پردازش می‌شود و ورودهای بعدی همین پیشوند خودکار تشخیص داده خواهند شد.";
        }
        applyPrefix.Checked += (_, _) => UpdateImpact(); applyPrefix.Unchecked += (_, _) => UpdateImpact(); prefix.TextChanged += (_, _) => UpdateImpact();
        apply.Click += (_, _) =>
        {
            var name = Rules.Normalize(brand.Text);
            if (string.IsNullOrWhiteSpace(name)) { error.Text = "یک برند ثبت‌شده را انتخاب کنید."; return; }
            if (!brands.Any(x => Rules.Normalize(x.Name).Equals(name, StringComparison.OrdinalIgnoreCase))) { error.Text = "این برند ثبت نشده است. ابتدا آن را از بخش برندها اضافه کنید."; return; }
            var values = BrandPrefixRules.Parse(prefix.Text);
            if (applyPrefix.IsChecked == true && values.Count != 1) { error.Text = "برای ثبت قانون، یک پیشوند عددی وارد کنید."; return; }
            Value = new PendingBrandResolution(name, values.FirstOrDefault() ?? "", applyPrefix.IsChecked == true); DialogResult = true;
        };
        cancel.Click += (_, _) => DialogResult = false;
        UpdateImpact();
    }
}

// هر مغایرت فقط از روی جدول باز می‌شود؛ این پنجره هم ثبت تعدیل جدید و هم ویرایش
// تعدیل‌های قبلی همان کالا را در یک مسیر نگه می‌دارد.
public sealed class ReconciliationDialog : Window
{
    sealed class AdjustmentRow
    {
        public required Purchase Value { get; init; }
        public string Date => Value.Date;
        public string Quantity => Rules.Money(Value.Quantity);
        public string UnitPrice => Rules.Money(Value.UnitPrice);
        public string Note => Value.Note;
    }

    readonly Ledger baseline;
    readonly ReconciliationCandidate initial;
    readonly Func<string, bool> canEditMonth;
    readonly List<Purchase> purchases;
    readonly DataGrid adjustments = new() { AutoGenerateColumns = false, CanUserAddRows = false, Height = 210 };
    readonly TextBlock current = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, Foreground = new SolidColorBrush(Color.FromRgb(54, 74, 104)) };
    readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
    readonly TextBox date = DialogUi.Input();
    readonly TextBox quantity = DialogUi.Input();
    readonly TextBox unitPrice = DialogUi.Input();
    readonly TextBox note = DialogUi.Input();
    readonly Button saveLine;
    string? selectedAdjustmentId;
    bool changed;
    ReconciliationCandidate? remaining;
    public List<Purchase>? Purchases { get; private set; }

    public ReconciliationDialog(Ledger ledger, ReconciliationCandidate candidate, Func<string, bool> canEditMonth)
    {
        baseline = ledger; initial = candidate; this.canEditMonth = canEditMonth; purchases = ledger.Purchases.ToList();
        Title = "رسیدگی به مغایرت موجودی"; Width = 850; Height = 760; MinWidth = 700; MinHeight = 590; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("رسیدگی به مغایرت موجودی", "تعدیل در تاریخ اولین کسری ثبت می‌شود و در همان روز، پیش از فروش محاسبه خواهد شد."));

        var content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(content, 1); root.Children.Add(content);
        var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 12), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; content.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"{candidate.Brand}  |  {candidate.Name}  |  کد کالا: {candidate.Code}", FontSize = 16, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right });
        panel.Children.Add(current);
        panel.Children.Add(new TextBlock { Text = "ثبت یا ویرایش تعدیل", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 18, 0, 7), TextAlignment = TextAlignment.Right });
        var inputs = new Grid { FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Stretch }; for (var i = 0; i < 4; i++) inputs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); panel.Children.Add(inputs);
        void Input(int column, string label, TextBox box, string hint)
        {
            var stack = new StackPanel { Margin = new Thickness(5, 0, 5, 0), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; stack.Children.Add(DialogUi.Label(label)); box.ToolTip = hint; stack.Children.Add(box); Grid.SetColumn(stack, column); inputs.Children.Add(stack);
        }
        Input(3, "تاریخ تعدیل", date, "مانند 14050612"); Input(2, "تعداد تعدیل", quantity, "حداکثر کسری باقی‌مانده"); Input(1, "قیمت واحد — ریال", unitPrice, "پیشنهاد از آخرین خرید"); Input(0, "یادداشت", note, "اختیاری");
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 8) }; panel.Children.Add(actions);
        saveLine = new Button { Content = "ثبت تعدیل", Style = (Style)FindResource("Primary") };
        var newLine = new Button { Content = "تعدیل جدید" }; var delete = new Button { Content = "حذف تعدیل انتخاب‌شده" };
        actions.Children.Add(saveLine); actions.Children.Add(newLine); actions.Children.Add(delete);
        panel.Children.Add(new TextBlock { Text = "تعدیلات قبلی همین کالا", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 7), TextAlignment = TextAlignment.Right });
        panel.Children.Add(new TextBlock { Text = "یک ردیف را انتخاب کنید تا همان‌جا قابل ویرایش یا حذف باشد.", Foreground = new SolidColorBrush(Color.FromRgb(102, 112, 133)), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 0, 6) });
        adjustments.Columns.Add(new DataGridTextColumn { Header = "تاریخ", Binding = new System.Windows.Data.Binding("Date"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        adjustments.Columns.Add(new DataGridTextColumn { Header = "تعداد", Binding = new System.Windows.Data.Binding("Quantity"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        adjustments.Columns.Add(new DataGridTextColumn { Header = "قیمت واحد", Binding = new System.Windows.Data.Binding("UnitPrice"), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
        adjustments.Columns.Add(new DataGridTextColumn { Header = "یادداشت", Binding = new System.Windows.Data.Binding("Note"), Width = new DataGridLength(2.2, DataGridLengthUnitType.Star) });
        panel.Children.Add(adjustments); panel.Children.Add(error);

        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 24, 18) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var close = new Button { Content = "انصراف" }; var apply = new Button { Content = "ثبت تغییرات و بستن", Style = (Style)FindResource("Primary") }; footer.Children.Add(close); footer.Children.Add(apply);

        ReconciliationCandidate? CurrentCandidate() => ReconciliationPlanner.Existing(baseline with { Purchases = purchases }).FirstOrDefault(x => x.Code.Equals(initial.Code, StringComparison.OrdinalIgnoreCase));
        void ResetEditor()
        {
            selectedAdjustmentId = null; adjustments.SelectedItem = null; saveLine.Content = "ثبت تعدیل"; error.Text = "";
            var candidateNow = remaining ?? initial; date.Text = candidateNow.FirstDate; quantity.Text = Rules.Money(candidateNow.Quantity); unitPrice.Text = candidateNow.SuggestedUnitCost > 0 ? Rules.Money(candidateNow.SuggestedUnitCost) : ""; note.Text = "";
        }
        void Refresh()
        {
            remaining = CurrentCandidate();
            current.Text = remaining == null
                ? "کسری فعلی: ندارد. تعدیل‌های قبلی را در صورت نیاز انتخاب و ویرایش کنید."
                : $"اولین کسری: {remaining.FirstDate} · کسری باقی‌مانده: {Rules.Money(remaining.Quantity)} · قیمت پیشنهادی: {(remaining.SuggestedUnitCost > 0 ? Rules.Money(remaining.SuggestedUnitCost) + " ریال" : "نیازمند ورود دستی")} · ردیف فروش درگیر: {remaining.SaleRows}";
            adjustments.ItemsSource = purchases.Where(x => x.IsAdjustment && x.Code.Equals(initial.Code, StringComparison.OrdinalIgnoreCase)).OrderByDescending(x => x.Date, StringComparer.Ordinal).ThenByDescending(x => x.Id, StringComparer.Ordinal).Select(x => new AdjustmentRow { Value = x }).ToList();
        }
        adjustments.SelectionChanged += (_, _) =>
        {
            if (adjustments.SelectedItem is not AdjustmentRow row) return;
            selectedAdjustmentId = row.Value.Id; date.Text = row.Value.Date; quantity.Text = Rules.Money(row.Value.Quantity); unitPrice.Text = Rules.Money(row.Value.UnitPrice); note.Text = row.Value.Note; saveLine.Content = "ذخیره ویرایش"; error.Text = "";
        };
        newLine.Click += (_, _) => ResetEditor();
        saveLine.Click += (_, _) =>
        {
            try
            {
                var dateValue = Rules.Digits(date.Text); var amount = Rules.Number(quantity.Text); var price = Rules.Number(unitPrice.Text);
                if (!Rules.ValidDate(dateValue)) throw new InvalidDataException("تاریخ تعدیل نامعتبر است.");
                if (string.CompareOrdinal(dateValue, initial.FirstDate) > 0) throw new InvalidDataException("تاریخ تعدیل نمی‌تواند بعد از اولین فروشِ دچار کسری باشد.");
                if (!canEditMonth(Rules.MonthOf(dateValue))) throw new InvalidOperationException($"ماه {Rules.MonthOf(dateValue)} بسته است.");
                if (amount <= 0 || price <= 0) throw new InvalidDataException("تعداد و قیمت واحد باید بیشتر از صفر باشند.");
                if (selectedAdjustmentId == null)
                {
                    if (remaining == null) throw new InvalidDataException("کسری باقی‌مانده‌ای برای ثبت تعدیل جدید وجود ندارد.");
                    if (amount > remaining.Quantity) throw new InvalidDataException("تعداد تعدیل نمی‌تواند از کسری باقی‌مانده بیشتر باشد.");
                    var created = new Purchase { Date = dateValue, Code = initial.Code, Supplier = "تعدیل دستی", Quantity = amount, UnitPrice = price, Total = amount * price, IsAdjustment = true, Note = Rules.Normalize(note.Text) };
                    Rules.Validate(created); purchases.Add(created);
                }
                else
                {
                    var index = purchases.FindIndex(x => x.Id == selectedAdjustmentId);
                    if (index < 0) throw new InvalidOperationException("تعدیل انتخاب‌شده یافت نشد.");
                    if (!canEditMonth(Rules.MonthOf(purchases[index].Date))) throw new InvalidOperationException($"ماه {Rules.MonthOf(purchases[index].Date)} بسته است.");
                    var withoutSelected = purchases.Where(x => x.Id != selectedAdjustmentId).ToList();
                    var shortageWithoutSelected = ReconciliationPlanner.Existing(baseline with { Purchases = withoutSelected }).FirstOrDefault(x => x.Code.Equals(initial.Code, StringComparison.OrdinalIgnoreCase))?.Quantity ?? 0;
                    if (amount > shortageWithoutSelected) throw new InvalidDataException("تعداد ویرایش‌شده نمی‌تواند از کسری واقعی این کالا بیشتر باشد.");
                    var updated = purchases[index] with { Date = dateValue, Quantity = amount, UnitPrice = price, Total = amount * price, Note = Rules.Normalize(note.Text) };
                    Rules.Validate(updated); purchases[index] = updated;
                }
                changed = true; Refresh(); ResetEditor();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        delete.Click += (_, _) =>
        {
            try
            {
                if (selectedAdjustmentId == null) throw new InvalidOperationException("ابتدا یک تعدیل قبلی را انتخاب کنید.");
                var index = purchases.FindIndex(x => x.Id == selectedAdjustmentId); if (index < 0) throw new InvalidOperationException("تعدیل انتخاب‌شده یافت نشد.");
                if (!canEditMonth(Rules.MonthOf(purchases[index].Date))) throw new InvalidOperationException($"ماه {Rules.MonthOf(purchases[index].Date)} بسته است.");
                if (MessageBox.Show(this, "تعدیل انتخاب‌شده حذف شود؟", "حذف تعدیل", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                purchases.RemoveAt(index); changed = true; Refresh(); ResetEditor();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        apply.Click += (_, _) => { Purchases = changed ? purchases.ToList() : null; DialogResult = true; };
        close.Click += (_, _) => DialogResult = false;
        Refresh(); ResetEditor();
    }
}

public sealed class PurchaseDialog : Window
{
    public CatalogItem? Item { get; private set; }
    public Purchase? Value { get; private set; }
    public PurchaseDialog(Ledger ledger, bool adjustment = false, string? code = null, decimal quantity = 0, decimal suggestedUnitPrice = 0, string? dateValue = null)
    {
        Title = adjustment ? "تعدیل موجودی" : "ثبت خرید"; Width = 640; Height = 690; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var panel = new StackPanel { Margin = new Thickness(26), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; Content = panel;
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
        save.Click += (_, _) => { try { var normalizedCode = Rules.Normalize(codeBox.Text); var selectedBrand = ledger.Brands.FirstOrDefault(x => Rules.Normalize(x.Name) == Rules.Normalize(brand.Text)) ?? throw new InvalidDataException("ابتدا برند کالا را در بخش برندها ثبت کنید."); var q = Rules.Number(qty.Text); var price = Rules.Number(unit.Text); var total = q * price; Item = new CatalogItem { Code = normalizedCode, Name = Rules.Normalize(name.Text), Brand = selectedBrand.Name }; Rules.Validate(Item); Value = new Purchase { Date = Rules.Digits(date.Text), Code = Item.Code, Supplier = Rules.Normalize(supplier.Text), Quantity = q, UnitPrice = price, Total = total, Deductions = Rules.Number(deductions.Text), BrandDiscount = adjustment ? 0 : selectedBrand.PurchaseDiscount, Offer = adjustment ? 0 : selectedBrand.Offer, IsAdjustment = adjustment, Note = adjustment ? "تعدیل موجودی ناشی از مغایرت" : "ثبت دستی" }; Rules.Validate(Value); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
    }
}

public sealed class SaleDialog : Window
{
    public Sale? Value { get; private set; }
    public SaleDialog(Ledger ledger)
    {
        Title = "ثبت فروش"; Width = 640; Height = 610; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var panel = new StackPanel { Margin = new Thickness(26), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; Content = panel;
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

// ویرایش فروش باید اثر واقعی آن بر FIFO را پیش از ثبت نشان دهد؛ بنابراین هر تغییر،
// کل دفتر را با جایگزینی همین ردیف دوباره محاسبه می‌کند.
public sealed class SaleEditorDialog : Window
{
    readonly Ledger ledger;
    readonly Sale original;
    readonly TextBox date;
    readonly TextBox customer;
    readonly TextBox quantity;
    readonly TextBox unitPrice;
    readonly TextBox deductions;
    readonly TextBlock preview = new() { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right, LineHeight = 27 };
    readonly TextBlock error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right };
    public Sale? Value { get; private set; }

    public SaleEditorDialog(Ledger ledger, Sale original)
    {
        this.ledger = ledger; this.original = original;
        var item = ledger.Items.First(x => x.Code.Equals(original.Code, StringComparison.OrdinalIgnoreCase));
        Title = "ویرایش فروش"; Width = 760; Height = 700; MinWidth = 640; MinHeight = 590; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        root.Children.Add(DialogUi.Header("ویرایش فروش", "با هر تغییر، بهای FIFO و سود این فروش بر اساس کل گردش کالا دوباره محاسبه می‌شود."));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var panel = new StackPanel { Margin = new Thickness(26, 18, 26, 12), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; scroll.Content = panel;
        panel.Children.Add(new TextBlock { Text = $"{item.Brand}  |  {item.Name}  |  کد کالا: {item.Code}", FontSize = 16, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap });

        var fields = new Grid { Margin = new Thickness(0, 16, 0, 8), FlowDirection = FlowDirection.LeftToRight }; fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); panel.Children.Add(fields);
        date = DialogUi.Input(original.Date); customer = DialogUi.Input(original.Customer); quantity = DialogUi.Input(Rules.Money(original.Quantity)); unitPrice = DialogUi.Input(Rules.Money(original.UnitPrice)); deductions = DialogUi.Input(Rules.Money(original.Deductions));
        void Field(int column, int row, string label, TextBox box)
        {
            while (fields.RowDefinitions.Count <= row) fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var stack = new StackPanel { Margin = new Thickness(6, 0, 6, 0), FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch }; stack.Children.Add(DialogUi.Label(label)); stack.Children.Add(box); Grid.SetColumn(stack, column); Grid.SetRow(stack, row); fields.Children.Add(stack);
        }
        Field(1, 0, "تاریخ", date); Field(0, 0, "مشتری", customer); Field(1, 1, "تعداد", quantity); Field(0, 1, "قیمت فروش واحد — ریال", unitPrice); Field(1, 2, "کسورات — ریال", deductions);
        MoneyInput.Attach(quantity); MoneyInput.Attach(unitPrice); MoneyInput.Attach(deductions);

        var previewBox = new Border { Style = (Style)FindResource("Panel"), Margin = new Thickness(0, 12, 0, 0), Child = new StackPanel { FlowDirection = FlowDirection.RightToLeft, HorizontalAlignment = HorizontalAlignment.Stretch } };
        var previewPanel = (StackPanel)previewBox.Child; previewPanel.Children.Add(new TextBlock { Text = "پیش‌نمایش محاسبه", FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Right }); previewPanel.Children.Add(preview); panel.Children.Add(previewBox); panel.Children.Add(error);

        var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(26, 0, 26, 18) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        var cancel = new Button { Content = "انصراف" }; var save = new Button { Content = "ثبت تغییرات", Style = (Style)FindResource("Primary") }; footer.Children.Add(save); footer.Children.Add(cancel);

        Sale ReadValue()
        {
            var q = Rules.Number(quantity.Text); var price = Rules.Number(unitPrice.Text);
            var value = original with { Date = Rules.Digits(date.Text), Customer = Rules.Normalize(customer.Text), Quantity = q, UnitPrice = price, Total = q * price, Deductions = Rules.Number(deductions.Text) };
            Rules.Validate(value); return value;
        }
        void UpdatePreview()
        {
            try
            {
                var value = ReadValue();
                var sales = ledger.Sales.Select(x => x.Id == original.Id ? value : x).ToList();
                var settled = LedgerCalculator.Calculate(ledger with { Sales = sales }).Sales[value.Id];
                preview.Text = $"فروش پس از کسورات: {Rules.Money(Rules.NetSaleBase(value))} ریال\nتخفیف نقدی: {Rules.Money(Rules.CashDiscountAmount(value))} ریال\nدریافتی نقدی: {Rules.Money(settled.Cash)} ریال\nفروش چکی: {Rules.Money(settled.Credit)} ریال\nبهای FIFO: {Rules.Money(settled.Cost)} ریال\nسود ناخالص: {Rules.Money(settled.Profit)} ریال" + (settled.Shortage > 0 ? $"\nکسری تأییدنشده: {Rules.Money(settled.Shortage)} عدد" : "");
                error.Text = "";
            }
            catch (Exception ex) { preview.Text = "برای نمایش پیش‌نمایش، همهٔ مقادیر را به‌درستی وارد کنید."; error.Text = ex.Message; }
        }
        date.TextChanged += (_, _) => UpdatePreview(); customer.TextChanged += (_, _) => UpdatePreview(); quantity.TextChanged += (_, _) => UpdatePreview(); unitPrice.TextChanged += (_, _) => UpdatePreview(); deductions.TextChanged += (_, _) => UpdatePreview();
        save.Click += (_, _) => { try { Value = ReadValue(); DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; } };
        cancel.Click += (_, _) => DialogResult = false;
        UpdatePreview();
    }
}
