namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record USD() : CurrencyType("$", "US", "US Dollar", "Dollar", 2);
    public record CAD() : CurrencyType("$", "CA", "Canadian Dollar", "Dollar Canadien", 2);
    public record MXN() : CurrencyType("$", "MX", "Mexican Peso", "Peso", 2);
    public record BBD() : CurrencyType("$", "BB", "Barbadian Dollar", "Dollar", 2);
    public record BSD() : CurrencyType("$", "BS", "Bahamian Dollar", "Dollar", 2);
    public record BZD() : CurrencyType("$", "BZ", "Belize Dollar", "Dollar", 2);
    public record CRC() : CurrencyType("₡", "CR", "Costa Rican Colón", "Colón", 2);
    public record CUP() : CurrencyType("$", "CU", "Cuban Peso", "Peso", 2);
    public record DOP() : CurrencyType("$", "DO", "Dominican Peso", "Peso", 2);
    public record GTQ() : CurrencyType("Q", "GT", "Guatemalan Quetzal", "Quetzal", 2);
    public record HNL() : CurrencyType("L", "HN", "Honduran Lempira", "Lempira", 2);
    public record HTG() : CurrencyType("G", "HT", "Haitian Gourde", "Gourde", 2);
    public record NIO() : CurrencyType("C$", "NI", "Nicaraguan Córdoba", "Córdoba", 2);
    public record PAB() : CurrencyType("B/.", "PA", "Panamanian Balboa", "Balboa", 2);
    public record TTD() : CurrencyType("$", "TT", "Trinidad Dollar", "Dollar", 2);
    public record JMD() : CurrencyType("$", "JM", "Jamaican Dollar", "Dollar", 2);
}
