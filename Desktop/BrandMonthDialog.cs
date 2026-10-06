using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

public sealed class BrandMonthConfirmationDialog : BrandDialogShell
{
    public sealed class RateRow : INotifyPropertyChanged
    {
        string purchase = "0", offer = "0", markup = "0", cash = "30", credit = "70", discount = "5";
        bool selected, ready;
        public event PropertyChangedEventHandler? PropertyChanged;
        public string BrandName { get; private set; } = "";
        public string Source { get; private set; } = "";
        public string Status { get; private set; } = "";
        public string Error { get; private set; } = "";
        public bool HasError => Error.Length > 0;
        public bool Selected { get => selected; set { selected = value; Changed(); } }
        public string PurchaseDiscount { get => purchase; set => Set(ref purchase, value); }
        public string Offer { get => offer; set => Set(ref offer, value); }
        public string Markup { get => markup; set => Set(ref markup, value); }
        public string CashShare { get => cash; set => Set(ref cash, value); }
        public string CreditShare { get => credit; set => Set(ref credit, value); }
        public string CashDiscount { get => discount; set => Set(ref discount, value); }
        void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        void Set(ref string field, string value, [CallerMemberName] string? name = null)
        {
            field = value; if (ready) { Selected = true; Status = "ویرایش ثبت‌نشده"; Changed(nameof(Status)); }
            try { ToValue(); Error = ""; } catch (Exception ex) { Error = ex.Message; }
            Changed(name); Changed(nameof(Error)); Changed(nameof(HasError));
        }
        public static RateRow From(BrandRate r) => new()
        {
            BrandName = r.BrandName, Source = r.SourceMonthKey, Status = r.IsConfirmed == true ? "تأیید شده" : "نیازمند تأیید",
            purchase = P(r.PurchaseDiscount), offer = P(r.Offer), markup = P(r.Markup), cash = P(r.CashShare), credit = P(r.CreditShare), discount = P(r.CashDiscount),
            selected = r.IsConfirmed != true, ready = true
        };
        static string P(decimal value) => (value * 100m).ToString("0.##", CultureInfo.InvariantCulture);
        decimal Read(string text, string label, bool isMarkup = false)
        {
            try { var value = Rules.Number(text) / 100m; if (value < 0 || (!isMarkup && value > 1)) throw new FormatException(); return value; }
            catch { throw new FormatException(label + ": " + (isMarkup ? "عدد نامنفی وارد کنید." : "عدد بین صفر و ۱۰۰ وارد کنید.")); }
        }
        public BrandRate ToValue()
        {
            var rate = new BrandRate { BrandName = BrandName, PurchaseDiscount = Read(purchase, "تخفیف خرید"), Offer = Read(offer, "آفر"), Markup = Read(markup, "مارک‌آپ", true), CashShare = Read(cash, "سهم نقدی"), CreditShare = Read(credit, "سهم چکی"), CashDiscount = Read(discount, "تخفیف نقدی") };
            BrandMonthRules.Validate(rate); return rate;
        }
    }

    public BrandMonthConfirmationDialog(Ledger ledger, string month, Action<Ledger> validate, Action<Ledger, string> commit)
        : base(ledger, $"درصدهای برندها · {month}", "فقط ردیف‌های تیک‌خورده ثبت می‌شوند. ویرایش یک ردیف آن را انتخاب می‌کند؛ تأیید برندهای دیگر حفظ می‌شود.", validate, commit, 1140, 700)
    {
        var active = ledger.Brands.Where(b => b.IsActive).Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = BrandMonthRules.DraftFor(ledger, month).Rates.Where(r => active.Contains(r.BrandName)).OrderBy(r => r.BrandName).Select(RateRow.From).ToList();
        var grid = new DataGrid { ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = false, CanUserAddRows = false, CanUserDeleteRows = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        Form.Children.Add(grid);
        grid.Columns.Add(new DataGridCheckBoxColumn { Header = "ثبت", Binding = new Binding(nameof(RateRow.Selected)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 60 });
        void Column(string title, string path, bool readOnly = false, double width = 115) => grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, IsReadOnly = readOnly, Width = width });
        Column("برند", nameof(RateRow.BrandName), true, 150); Column("وضعیت", nameof(RateRow.Status), true); Column("منبع", nameof(RateRow.Source), true);
        Column("تخفیف خرید ٪", nameof(RateRow.PurchaseDiscount)); Column("آفر ٪", nameof(RateRow.Offer)); Column("مارک‌آپ پایه ٪", nameof(RateRow.Markup)); Column("سهم نقدی ٪", nameof(RateRow.CashShare)); Column("سهم چکی ٪", nameof(RateRow.CreditShare)); Column("تخفیف نقدی ٪", nameof(RateRow.CashDiscount)); Column("خطای ردیف", nameof(RateRow.Error), true, 260);
        var rowStyle = new Style(typeof(DataGridRow), Application.Current.TryFindResource(typeof(DataGridRow)) as Style);
        var errorTrigger = new DataTrigger { Binding = new Binding(nameof(RateRow.HasError)), Value = true }; errorTrigger.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.MistyRose)); rowStyle.Triggers.Add(errorTrigger); grid.RowStyle = rowStyle;
        ReadChange = () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            var values = new List<BrandRate>();
            foreach (var row in rows.Where(r => r.Selected))
            {
                try { values.Add(row.ToValue()); }
                catch (Exception ex) { grid.SelectedItem = row; grid.ScrollIntoView(row); throw new InvalidOperationException(row.BrandName + ": " + ex.Message); }
            }
            return BrandMonthRules.Confirm(ledger, month, values);
        };
        Scope = $"فقط برندهای انتخاب‌شده در ماه {month} تأیید یا اصلاح می‌شوند؛ مارک‌آپ اختصاصی فروش و بازه‌های ثبت‌شده حفظ می‌شوند.";
        ActionText = $"درصدهای برندهای انتخاب‌شده در {month} ثبت شد.";
    }
}
