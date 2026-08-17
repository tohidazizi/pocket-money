namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record DZD() : CurrencyType("دج", "DZ", "Algerian Dinar", "دينار جزائري", 2);
    public record AOA() : CurrencyType("Kz", "AO", "Angolan Kwanza", "Kwanza", 2);
    public record XOF() : CurrencyType("Fr", "BJ", "West African CFA Franc", "Franc CFA", 0);
    public record BWP() : CurrencyType("P", "BW", "Botswana Pula", "Pula", 2);
    public record BIF() : CurrencyType("Fr", "BI", "Burundian Franc", "Franc Burundais", 0);
    public record CVE() : CurrencyType("$", "CV", "Cape Verdean Escudo", "Escudo Cabo-Verdiano", 2);
    public record KMF() : CurrencyType("Fr", "KM", "Comorian Franc", "فرنك قمري", 0);
    public record XAF() : CurrencyType("Fr", "CM", "Central African CFA Franc", "Franc CFA", 0);
    public record DJF() : CurrencyType("Fdj", "DJ", "Djiboutian Franc", "فرنك جيبوتي", 0);
    public record EGP() : CurrencyType("ج.م", "EG", "Egyptian Pound", "جنيه مصري", 2);
    public record ERN() : CurrencyType("Nfk", "ER", "Eritrean Nakfa", "ናቅፋ", 2);
    public record SZL() : CurrencyType("L", "SZ", "Swazi Lilangeni", "Lilangeni", 2);
    public record ETB() : CurrencyType("Br", "ET", "Ethiopian Birr", "ብር", 2);
    public record GMD() : CurrencyType("D", "GM", "Gambian Dalasi", "Dalasi", 2);
    public record GHS() : CurrencyType("₵", "GH", "Ghanaian Cedi", "Cedi", 2);
    public record GNF() : CurrencyType("Fr", "GN", "Guinean Franc", "Franc Guinéen", 0);
    public record KES() : CurrencyType("Sh", "KE", "Kenyan Shilling", "Shilingi", 2);
    public record LSL() : CurrencyType("L", "LS", "Lesotho Loti", "Loti", 2);
    public record LRD() : CurrencyType("$", "LR", "Liberian Dollar", "Dollar Liberian", 2);
    public record MGA() : CurrencyType("Ar", "MG", "Malagasy Ariary", "Ariary", 2);
    public record MWK() : CurrencyType("MK", "MW", "Malawian Kwacha", "Kwacha", 2);
    public record MRU() : CurrencyType("UM", "MR", "Mauritanian Ouguiya", "أوقية", 2);
    public record MUR() : CurrencyType("₨", "MU", "Mauritian Rupee", "Roupie Mauricienne", 2);
    public record MAD() : CurrencyType("د.م.", "MA", "Moroccan Dirham", "درهم مغربي", 2);
    public record MZN() : CurrencyType("MT", "MZ", "Mozambican Metical", "Metical", 2);
    public record NAD() : CurrencyType("$", "NA", "Namibian Dollar", "Dollar Namibien", 2);
    public record NGN() : CurrencyType("₦", "NG", "Nigerian Naira", "Naira", 2);
    public record RWF() : CurrencyType("Fr", "RW", "Rwandan Franc", "Franc Rwandais", 0);
    public record STN() : CurrencyType("Db", "ST", "São Tomé and Príncipe Dobra", "Dobra", 2);
    public record SCR() : CurrencyType("₨", "SC", "Seychellois Rupee", "Roupie Seychelloise", 2);
    public record SLL() : CurrencyType("Le", "SL", "Sierra Leonean Leone", "Leone", 2);
    public record SOS() : CurrencyType("Sh", "SO", "Somali Shilling", "Shilin Soomaali", 2);
    public record ZAR() : CurrencyType("R", "ZA", "South African Rand", "Rand", 2);
    public record SSP() : CurrencyType("£", "SS", "South Sudanese Pound", "جنيه جنوب السودان", 2);
    public record SDG() : CurrencyType("ج.س.", "SD", "Sudanese Pound", "جنيه سوداني", 2);
    public record TZS() : CurrencyType("Sh", "TZ", "Tanzanian Shilling", "Shilingi", 2);
    public record UGX() : CurrencyType("Sh", "UG", "Ugandan Shilling", "Shilingi", 0);
    public record ZMW() : CurrencyType("ZK", "ZM", "Zambian Kwacha", "Kwacha", 2);
    public record ZWL() : CurrencyType("$", "ZW", "Zimbabwean Dollar", "Dollar Zimbabwe", 2);
}
