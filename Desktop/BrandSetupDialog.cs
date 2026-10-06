using System.IO;
using System.Windows;
using System.Windows.Controls;
using Profit.Core;

namespace Profit.Desktop;

public sealed class BrandSetupDialog : BrandDialogShell
{
    public BrandSetupDialog(Ledger ledger, string month, bool monthClosed, Action<Ledger> validate, Action<Ledger, string> commit, CatalogItem? unknown = null)
        : base(ledger, unknown is null ? "افزودن برند" : "تعیین برند کالا", unknown is null ? "ابتدا مشخصات برند، سپس درصدهای ماه؛ سایر برندها و تأییدهای قبلی حفظ می‌شوند." : $"کد {unknown.Code} · {unknown.Name}\nپیشوند خودکار است؛ کدهای ناشناخته مرتبط نیز تخصیص می‌گیرند.", validate, commit, 760, 780)
    {
        var panel = ScrollForm(); var name = Field(panel, "نام برند");
        var automatic = unknown is null ? "" : Rules.Digits(unknown.Code)[..Math.Min(3, Rules.Digits(unknown.Code).Length)];
        var prefixes = Field(panel, "پیشوند کد کالا", automatic); prefixes.IsReadOnly = unknown is not null;
        var exact = unknown is null ? Field(panel, "کدهای دقیق کالا (اختیاری)") : new TextBox();
        var next = new Button { Content = monthClosed ? "بررسی مشخصات برند" : "ادامه به درصدهای ماه", HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(next);
        var ratesPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 14, 0, 0) }; panel.Children.Add(ratesPanel);
        BrandRateFields? rateFields = null; PreviewButton.IsEnabled = false;
        Brand ReadBrand()
        {
            var clean = Rules.Normalize(name.Text); var existing = ledger.Brands.FirstOrDefault(b => MarkupRules.SameBrand(b.Name, clean));
            if (existing is not null && (unknown is null || !existing.IsActive)) throw new InvalidDataException(existing.IsActive ? "این نام قبلاً ثبت شده است؛ از مدیریت برند استفاده کنید." : "برند غیرفعال است؛ ابتدا آن را فعال کنید.");
            var parsed = BrandPrefixRules.Parse(prefixes.Text);
            if (unknown is not null && existing is not null) parsed = existing.CodePrefixes.Concat(parsed).Distinct(StringComparer.Ordinal).ToList();
            return (existing ?? new Brand()) with { Name = clean, CodePrefixes = parsed, ExactProductCodes = unknown is not null && existing is not null ? existing.ExactProductCodes : BrandPrefixRules.Parse(exact.Text) };
        }
        void Invalidate() { ratesPanel.Visibility = Visibility.Collapsed; PreviewButton.IsEnabled = false; rateFields = null; }
        name.TextChanged += (_, _) => Invalidate(); prefixes.TextChanged += (_, _) => Invalidate(); exact.TextChanged += (_, _) => Invalidate();
        next.Click += (_, _) =>
        {
            try
            {
                var brand = ReadBrand(); var existing = ledger.Brands.FirstOrDefault(b => MarkupRules.SameBrand(b.Name, brand.Name));
                var staged = BrandCatalogRules.Upsert(ledger, brand, existing?.Name);
                ratesPanel.Children.Clear();
                if (monthClosed) ratesPanel.Children.Add(new TextBlock { Text = $"ماه {month} بسته است؛ فقط مشخصات ثبت می‌شوند. برای تأیید درصدها ماه را باز کنید.", Style = (Style)FindResource("MutedText"), TextWrapping = TextWrapping.Wrap });
                else
                {
                    var source = BrandMonthRules.DraftFor(staged, month).Rates.First(r => MarkupRules.SameBrand(r.BrandName, brand.Name));
                    ratesPanel.Children.Add(new TextBlock { Text = $"درصدهای ماه {month} · منبع: {source.SourceMonthKey}", Style = (Style)FindResource("MutedText") });
                    rateFields = new BrandRateFields(ratesPanel, source);
                }
                ratesPanel.Visibility = Visibility.Visible; PreviewButton.IsEnabled = true; Error.Text = "";
            }
            catch (Exception ex) { Error.Text = ex.Message; }
        };
        ReadChange = () =>
        {
            var brand = ReadBrand(); var existing = ledger.Brands.FirstOrDefault(b => MarkupRules.SameBrand(b.Name, brand.Name));
            var staged = BrandCatalogRules.Upsert(ledger, brand, existing?.Name);
            if (!monthClosed) staged = BrandMonthRules.Confirm(staged, month, [rateFields?.Read() ?? throw new InvalidDataException("ابتدا مرحله درصدها را باز کنید.")]);
            return staged;
        };
        Scope = $"مشخصات برند در همه ماه‌ها در دسترس است؛ فقط درصدهای ماه {month} تأیید می‌شوند. ماه‌های دیگرِ این برند تا تأیید جداگانه معلق می‌مانند.";
        ActionText = "برند و تخصیص کدهای مرتبط ثبت شد؛ تأیید سایر برندها حفظ شد.";
    }
}
