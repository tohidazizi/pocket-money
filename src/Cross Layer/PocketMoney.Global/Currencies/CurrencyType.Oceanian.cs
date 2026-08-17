namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record AUD() : CurrencyType("$", "AU", "Australian Dollar", "Dollar", 2);
    public record NZD() : CurrencyType("$", "NZ", "New Zealand Dollar", "Dollar", 2);
    public record FJD() : CurrencyType("$", "FJ", "Fijian Dollar", "Dollar", 2);
    public record PGK() : CurrencyType("K", "PG", "Papua New Guinean Kina", "Kina", 2);
    public record SBD() : CurrencyType("$", "SB", "Solomon Islands Dollar", "Dollar", 2);
    public record VUV() : CurrencyType("Vt", "VU", "Vanuatu Vatu", "Vatu", 0);

}
