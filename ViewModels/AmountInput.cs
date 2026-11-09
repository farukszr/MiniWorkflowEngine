using System.Globalization;
using System.Text.RegularExpressions;

namespace MiniWorkflowEngine.ViewModels;

internal static class AmountInput
{
    public const string ErrorMessage = "Tutarı binlik ayırıcı kullanmadan, en fazla iki ondalık basamakla girin (örnek: 1000,50).";

    public static bool TryParse(string? input, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        input = input.Trim();
        if (!Regex.IsMatch(input, @"^[0-9]{1,16}([.,][0-9]{1,2})?$")) return false;
        if (!decimal.TryParse(input.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = parsed;
        return true;
    }
}
