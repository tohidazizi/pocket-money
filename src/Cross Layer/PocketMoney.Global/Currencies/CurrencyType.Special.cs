namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record XCD() : CurrencyType("$", "AG", "East Caribbean Dollar", "Dollar", 2);
    public record XPF() : CurrencyType("Fr", "PF", "CFP Franc", "Franc", 0);
    public record XDR() : CurrencyType("SDR", "IM", "IMF Special Drawing Rights", "SDR", 0);
}
