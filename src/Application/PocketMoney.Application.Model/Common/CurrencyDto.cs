using PocketMoney.Global;

namespace PocketMoney.Application.Model.Common;

/// <summary>
/// Resolved currency record exchanged by key string (API Spec §1.3, SDS §2.1.1).
/// `nativeTitle` is null when it equals the display title (API Spec §3.2 shape).
/// </summary>
public sealed record CurrencyDto(
    string Key,
    string Symbol,
    string Country,
    string Title,
    string? NativeTitle,
    byte DecimalDigits)
{
    public static CurrencyDto From(CurrencyType currency) => new(
        currency.Key,
        currency.Symbol,
        currency.Country,
        currency.Title,
        currency.NativeTitle == currency.Title ? null : currency.NativeTitle,
        currency.DecimalDigits);
}
