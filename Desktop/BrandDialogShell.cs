using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Profit.Core;

namespace Profit.Desktop;

// Preview and editing share one window; only the final button invokes persistence.
public abstract class BrandDialogShell : Window
{
    protected readonly Ledger Original;
    protected readonly Grid Form = new();
    protected readonly TextBlock Error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left };
    protected readonly Button PreviewButton;
    protected readonly Button FinalButton;
    protected Func<Ledger>? ReadChange;
    protected string Scope = "";
    protected string ActionText = "تغییرات برند ثبت شد.";
    public Ledger? Value { get; private set; }
    readonly Action<Ledger> validate;
    readonly Action<Ledger, string> commit;
    readonly ScrollViewer review;
    readonly TextBlock summary;
    readonly Button back;
    Ledger? staged;
    bool dirty;
    bool tracking;
    bool committed;

    protected BrandDialogShell(Ledger original, string title, string note, Action<Ledger> validate, Action<Ledger, string> commit, double width = 800, double height = 720)
    {
        Original = original; this.validate = validate; this.commit = commit;
        Title = title; var area = SystemParameters.WorkArea;
        Width = Math.Min(width, area.Width - 32); Height = Math.Min(height, area.Height - 32);
        MinWidth = Math.Min(520, Width); MinHeight = Math.Min(420, Height);
        MaxWidth = area.Width; MaxHeight = area.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FlowDirection = FlowDirection.LeftToRight; FontFamily = (FontFamily)Application.Current.FindResource("Vazir");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Content = root;
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        root.Children.Add(DialogUi.Header(title, note));
        var body = new Grid { Margin = new Thickness(22, 16, 22, 10) }; Grid.SetRow(body, 1); root.Children.Add(body); body.Children.Add(Form);
        summary = new TextBlock { TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Left };
        review = new ScrollViewer { Content = summary, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed }; body.Children.Add(review);
        var footer = new StackPanel { Margin = new Thickness(22, 0, 22, 16) }; Grid.SetRow(footer, 2); root.Children.Add(footer); footer.Children.Add(Error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
        back = new Button { Content = "برگشت به ویرایش", Visibility = Visibility.Collapsed }; buttons.Children.Add(back);
        PreviewButton = new Button { Content = "بررسی تغییرات", IsDefault = true, Style = (Style)FindResource("Primary") }; buttons.Children.Add(PreviewButton);
        FinalButton = new Button { Content = "تأیید و اعمال تغییرات", IsDefault = false, Style = (Style)FindResource("Primary"), Visibility = Visibility.Collapsed }; buttons.Children.Add(FinalButton);
        buttons.Children.Add(new Button { Content = "انصراف", IsCancel = true });
        Form.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => { if (tracking) dirty = true; }));
        Form.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => { if (tracking) dirty = true; }));
        Form.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => { if (tracking) dirty = true; }));
        Loaded += (_, _) => { tracking = true; Form.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); };
        Closing += (_, e) => { if (!committed && dirty && MessageBox.Show(this, "تغییرات ذخیره نشده‌اند. بدون ثبت ببندید؟", "تغییر ذخیره‌نشده", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true; };
        PreviewButton.Click += (_, _) =>
        {
            try
            {
                var next = ReadChange?.Invoke() ?? throw new InvalidOperationException("فرم آماده نیست.");
                next = BrandMonthRules.ApplyExactProductCodeAssignments(MarkupRules.UpgradeLegacy(next));
                LedgerCalculator.Calculate(next); validate(next);
                if (JsonSerializer.Serialize(next, Rules.Json) == JsonSerializer.Serialize(Original, Rules.Json)) { dirty = false; Error.Text = "تغییری برای ثبت وجود ندارد."; return; }
                summary.Text = BrandChangePreview.Describe(Original, next, Scope); staged = next;
                Form.Visibility = PreviewButton.Visibility = Visibility.Collapsed; review.Visibility = back.Visibility = FinalButton.Visibility = Visibility.Visible;
                PreviewButton.IsDefault = false; FinalButton.IsDefault = true; Error.Text = ""; FinalButton.Focus();
            }
            catch (Exception ex) { Error.Text = ex.Message; }
        };
        back.Click += (_, _) => { staged = null; review.Visibility = back.Visibility = FinalButton.Visibility = Visibility.Collapsed; Form.Visibility = PreviewButton.Visibility = Visibility.Visible; FinalButton.IsDefault = false; PreviewButton.IsDefault = true; Error.Text = ""; Form.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); };
        FinalButton.Click += (_, _) =>
        {
            try { if (staged is null) return; validate(staged); commit(staged, ActionText); Value = staged; committed = true; DialogResult = true; }
            catch (Exception ex) { Error.Text = ex.Message; }
        };
    }

    protected StackPanel ScrollForm()
    {
        var fields = new StackPanel(); Form.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); return fields;
    }
    protected static TextBox Field(Panel fields, string label, string value = "") { fields.Children.Add(DialogUi.Label(label)); var input = DialogUi.Input(value); fields.Children.Add(input); return input; }
}
