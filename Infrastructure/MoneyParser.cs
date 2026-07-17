using System.Globalization;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Infrastructure;

internal static class MoneyParser
{
    public static decimal Parse(string text)
    {
        var match = Regex.Match(
            text,
            @"(?<!\d)([\d.,]+)(?!\d)",
            RegexOptions.RightToLeft);

        if (!match.Success)
        {
            throw new FormatException($"Không đọc được số tiền: {text}");
        }

        return decimal.Parse(
            match.Groups[1].Value
                .Replace(".", "")
                .Replace(",", ""),
            CultureInfo.InvariantCulture);
    }
}
