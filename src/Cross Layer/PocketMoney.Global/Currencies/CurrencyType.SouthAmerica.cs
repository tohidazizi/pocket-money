namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record ARS() : CurrencyType("$", "AR", "Argentine Peso", "Peso", 2);
    public record BOB() : CurrencyType("Bs.", "BO", "Bolivian Boliviano", "Boliviano", 2);
    public record BRL() : CurrencyType("R$", "BR", "Brazilian Real", "Real", 2);
    public record CLP() : CurrencyType("$", "CL", "Chilean Peso", "Peso", 0);
    public record COP() : CurrencyType("$", "CO", "Colombian Peso", "Peso", 2);
    public record GYD() : CurrencyType("$", "GY", "Guyanese Dollar", "Dollar", 2);
    public record PYG() : CurrencyType("₲", "PY", "Paraguayan Guaraní", "Guaraní", 0);
    public record PEN() : CurrencyType("S/", "PE", "Peruvian Sol", "Sol", 2);
    public record SRD() : CurrencyType("$", "SR", "Surinamese Dollar", "Dollar", 2);
    public record UYU() : CurrencyType("$", "UY", "Uruguayan Peso", "Peso", 2);
    public record VES() : CurrencyType("Bs.", "VE", "Venezuelan Bolívar", "Bolívar", 2);
}
