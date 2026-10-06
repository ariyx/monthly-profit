using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class BrandRateFields
{
    readonly BrandRate original;
    readonly List<(string Label, TextBox Input, TextBlock Error, bool Markup)> fields = [];
    readonly TextBlock shares = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left };
    public BrandRateFields(Panel panel, BrandRate original, bool editable = true)
    {
        this.original = original;
        void Add(string label, decimal value, bool markup = false)
        {
            panel.Children.Add(DialogUi.Label(label)); var input = DialogUi.Input(); DialogUi.Percent(input, value); input.IsReadOnly = !editable; panel.Children.Add(input);
            var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left }; panel.Children.Add(error);
            fields.Add((label, input, error, markup)); input.TextChanged += (_, _) => Check();
        }
        Add("تخفیف خرید ٪", original.PurchaseDiscount); Add("آفر ٪", original.Offer); Add("مارک‌آپ پایه ماه ٪", original.Markup, true);
        Add("سهم نقدی ٪", original.CashShare); Add("سهم چکی ٪", original.CreditShare); Add("تخفیف نقدی ٪", original.CashDiscount);
        panel.Children.Add(shares);
    }
    void Check()
    {
        foreach (var f in fields)
        {
            try { var n = DialogUi.Percent(f.Input); f.Error.Text = n < 0 || (!f.Markup && n > 1) ? (f.Markup ? "عدد منفی مجاز نیست." : "عدد باید بین صفر و ۱۰۰ باشد.") : ""; }
            catch { f.Error.Text = "عدد معتبر وارد کنید."; }
        }
        try { shares.Text = DialogUi.Percent(fields[3].Input) + DialogUi.Percent(fields[4].Input) != 1 ? "جمع سهم نقدی و چکی باید ۱۰۰٪ باشد." : ""; }
        catch { shares.Text = ""; }
    }
    public BrandRate Read()
    {
        Check();
        var invalid = fields.FirstOrDefault(f => f.Error.Text.Length > 0);
        if (invalid.Input is not null) { invalid.Input.Focus(); invalid.Input.BringIntoView(); throw new InvalidDataException("فیلدهای مشخص‌شده را اصلاح کنید."); }
        var values = fields.Select(f => DialogUi.Percent(f.Input)).ToArray();
        var result = original with { PurchaseDiscount = values[0], Offer = values[1], Markup = values[2], CashShare = values[3], CreditShare = values[4], CashDiscount = values[5] };
        BrandMonthRules.Validate(result); return result;
    }
}

public sealed class BrandPercentDialog : BrandDialogShell
{
    public BrandPercentDialog(Ledger ledger, Brand brand, string month, Action<Ledger> validate, Action<Ledger, string> commit)
        : base(ledger, "درصدهای ماه برند", $"{brand.Name} · {month}\nمارک‌آپ پایه برای روزهای خارج از بازه‌هاست؛ بقیه درصدها ماهانه‌اند.", validate, commit)
    {
        var rate = BrandMonthRules.DraftFor(ledger, month).Rates.First(r => MarkupRules.SameBrand(r.BrandName, brand.Name));
        var panel = ScrollForm(); panel.Children.Add(new TextBlock { Text = $"منبع: {rate.SourceMonthKey} · {(rate.IsConfirmed == true ? "تأیید شده" : "نیازمند تأیید")}", Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 12) });
        var inputs = new BrandRateFields(panel, rate);
        ReadChange = () => BrandMonthRules.Confirm(ledger, month, [inputs.Read()]);
        Scope = $"درصدهای «{brand.Name}» فقط در ماه {month} اصلاح می‌شوند؛ بازه‌های مارک‌آپ و استثناهای فروش حفظ می‌شوند.";
        ActionText = $"درصدهای «{brand.Name}» در {month} تأیید شد.";
    }
}

public sealed class BrandIdentityDialog : BrandDialogShell
{
    public string CurrentName { get; private set; }
    public BrandIdentityDialog(Ledger ledger, Brand brand, Action<Ledger> validate, Action<Ledger, string> commit)
        : base(ledger, "مشخصات برند", "نام، پیشوند و کدهای دقیق مستقل از ماه هستند. تغییر درصدها در بخش درصدهای ماه انجام می‌شود.", validate, commit, 720, 590)
    {
        CurrentName = brand.Name; var panel = ScrollForm(); var name = Field(panel, "نام برند", brand.Name);
        name.IsReadOnly = ledger.Items.Any(i => MarkupRules.SameBrand(i.Brand, brand.Name));
        if (name.IsReadOnly) panel.Children.Add(new TextBlock { Text = "نام برند دارای کالا ثابت است؛ پیشوند و کدهای دقیق قابل ویرایش‌اند.", Style = (Style)FindResource("MutedText"), TextWrapping = TextWrapping.Wrap });
        var prefix = Field(panel, "پیشوندها (با ، جدا کنید)", BrandPrefixRules.Display(brand.CodePrefixes));
        var exact = Field(panel, "کدهای دقیق کالا؛ اولویت با کد دقیق است", BrandPrefixRules.Display(brand.ExactProductCodes));
        ReadChange = () => { CurrentName = Rules.Normalize(name.Text); return BrandCatalogRules.Upsert(ledger, brand with { Name = CurrentName, CodePrefixes = BrandPrefixRules.Parse(prefix.Text), ExactProductCodes = BrandPrefixRules.Parse(exact.Text) }, brand.Name); };
        Scope = "ویرایش مشخصات برند؛ درصدهای ماهانه بدون تغییر باقی می‌مانند. اثر تغییر تخصیص کدها در پایین نمایش داده می‌شود.";
        ActionText = $"مشخصات برند «{brand.Name}» ویرایش شد.";
    }
}
