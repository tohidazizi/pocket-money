namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record ALL() : CurrencyType("L", "AL", "Albanian Lek", "Lek", 2);

    #region Euro

    public record EUR() : CurrencyType("€", "EU", "Euro", "Euro", 2);
    public record EUR_AT() : CurrencyType("€", "AT", "Euro", "Euro", 2);
    public record EUR_BE() : CurrencyType("€", "BE", "Euro", "Euro", 2);
    public record EUR_CY() : CurrencyType("€", "CY", "Euro", "Ευρώ", 2);
    public record EUR_EE() : CurrencyType("€", "EE", "Euro", "Euro", 2);
    public record EUR_FI() : CurrencyType("€", "FI", "Euro", "Euro", 2);
    public record EUR_FR() : CurrencyType("€", "FR", "Euro", "Euro", 2);
    public record EUR_DE() : CurrencyType("€", "DE", "Euro", "Euro", 2);
    public record EUR_GR() : CurrencyType("€", "GR", "Euro", "Ευρώ", 2);
    public record EUR_IE() : CurrencyType("€", "IE", "Euro", "Euro", 2);
    public record EUR_IT() : CurrencyType("€", "IT", "Euro", "Euro", 2);
    public record EUR_LU() : CurrencyType("€", "LU", "Euro", "Euro", 2);
    public record EUR_LV() : CurrencyType("€", "LV", "Euro", "Eiro", 2);
    public record EUR_LT() : CurrencyType("€", "LT", "Euro", "Euras", 2);
    public record EUR_MT() : CurrencyType("€", "MT", "Euro", "Ewro", 2);
    public record EUR_NL() : CurrencyType("€", "NL", "Euro", "Euro", 2);
    public record EUR_PT() : CurrencyType("€", "PT", "Euro", "Euro", 2);
    public record EUR_SK() : CurrencyType("€", "SK", "Euro", "Euro", 2);
    public record EUR_SI() : CurrencyType("€", "SI", "Euro", "Evro", 2);
    public record EUR_ES() : CurrencyType("€", "ES", "Euro", "Euro", 2);

    #endregion

    public record BYN() : CurrencyType("Br", "BY", "Belarusian Ruble", "Рубель", 2);
    public record BAM() : CurrencyType("KM", "BA", "Bosnia Convertible Mark", "Konvertibilna Marka", 2);
    public record BGN() : CurrencyType("лв", "BG", "Bulgarian Lev", "Лев", 2);
    public record HRK() : CurrencyType("€", "HR", "Euro", "Euro", 2);
    public record CZK() : CurrencyType("Kč", "CZ", "Czech Koruna", "Koruna", 2);
    public record DKK() : CurrencyType("kr", "DK", "Danish Krone", "Krone", 2);
    public record GBP() : CurrencyType("£", "GB", "British Pound", "Pound Sterling", 2);
    public record HUF() : CurrencyType("Ft", "HU", "Hungarian Forint", "Forint", 2);
    public record ISK() : CurrencyType("kr", "IS", "Icelandic Króna", "Króna", 0);
    public record CHF() : CurrencyType("Fr", "CH", "Swiss Franc", "Franken", 2);
    public record NOK() : CurrencyType("kr", "NO", "Norwegian Krone", "Krone", 2);
    public record PLN() : CurrencyType("zł", "PL", "Polish Złoty", "Złoty", 2);
    public record RON() : CurrencyType("lei", "RO", "Romanian Leu", "Leu", 2);
    public record RUB() : CurrencyType("₽", "RU", "Russian Ruble", "Рубль", 2);
    public record RSD() : CurrencyType("дин", "RS", "Serbian Dinar", "Динар", 2);
    public record SEK() : CurrencyType("kr", "SE", "Swedish Krona", "Krona", 2);
    public record UAH() : CurrencyType("₴", "UA", "Ukrainian Hryvnia", "Гривня", 2);
}
