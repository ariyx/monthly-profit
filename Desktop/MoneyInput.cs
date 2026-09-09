using System.Globalization;
using System.Windows.Controls;
using Profit.Core;

namespace Profit.Desktop;

public static class MoneyInput
{
    public static void Attach(TextBox box)
    {
        var changing = false;
        box.TextChanged += (_, _) =>
        {
            if (changing) return;
            var source = box.Text;
            var normalized = Rules.Digits(source);
            if (string.IsNullOrWhiteSpace(normalized) || normalized is "-" or "." or "-.") return;
            if (!decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return;
            var formatted = Rules.Money(value);
            if (formatted == source) return;
            var rightDigits = source.Skip(Math.Min(box.CaretIndex, source.Length)).Count(char.IsDigit);
            changing = true;
            try
            {
                box.Text = formatted;
                var position = formatted.Length;
                while (position > 0 && rightDigits > 0)
                {
                    position--; if (char.IsDigit(formatted[position])) rightDigits--;
                }
                box.CaretIndex = position;
            }
            finally { changing = false; }
        };
    }
}
