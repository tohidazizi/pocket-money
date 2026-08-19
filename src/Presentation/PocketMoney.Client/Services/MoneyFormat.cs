namespace PocketMoney.Client.Services;

/// <summary>
/// Money formatting per SDS §9.5: exactly the currency's DecimalDigits,
/// prefixed with the currency symbol ($5.00 for USD, 🪙5 for Point).
/// </summary>
public static class MoneyFormat
{
    public static string Format(decimal amount, PocketMoney.Application.Model.Common.CurrencyDto currency)
    {
        var digits = currency.DecimalDigits;
        var body = amount.ToString($"F{digits}", System.Globalization.CultureInfo.InvariantCulture);
        return digits == 0 ? $"{currency.Symbol}{body}" : $"{currency.Symbol}{body}";
    }
}
