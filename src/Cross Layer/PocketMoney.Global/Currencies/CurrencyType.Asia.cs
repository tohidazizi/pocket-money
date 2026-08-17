namespace PocketMoney.Global;

public abstract partial record CurrencyType
{
    public record AFN() : CurrencyType("؋", "AF", "Afghan Afghani", "افغانی", 2);
    public record AMD() : CurrencyType("֏", "AM", "Armenian Dram", "Դրամ", 2);
    public record AZN() : CurrencyType("₼", "AZ", "Azerbaijani Manat", "Manat", 2);
    public record BHD() : CurrencyType(".د.ب", "BH", "Bahraini Dinar", "دينار بحريني", 3);
    public record BDT() : CurrencyType("৳", "BD", "Bangladeshi Taka", "টাকা", 2);
    public record BTN() : CurrencyType("Nu.", "BT", "Bhutanese Ngultrum", "དངུལ་ཀྲམ", 2);
    public record BND() : CurrencyType("$", "BN", "Brunei Dollar", "Ringgit Brunei", 2);
    public record MMK() : CurrencyType("Ks", "MM", "Myanmar Kyat", "ကျပ်", 2);
    public record CNY() : CurrencyType("¥", "CN", "Chinese Yuan", "人民币", 2);
    public record HKD() : CurrencyType("$", "HK", "Hong Kong Dollar", "港元", 2);
    public record IDR() : CurrencyType("Rp", "ID", "Indonesian Rupiah", "Rupiah", 2);
    public record INR() : CurrencyType("₹", "IN", "Indian Rupee", "रुपया", 2);
    public record IQD() : CurrencyType("ع.د", "IQ", "Iraqi Dinar", "دينار عراقي", 3);
    public record IRR() : CurrencyType("﷼", "IR", "Iranian Rial", "ریال ایرانی", 0);
    public record IRR2() : CurrencyType("تومان", "IR", "Iranian Toman", "تومان", 0);
    public record ILS() : CurrencyType("₪", "IL", "Israeli Shekel", "שקל", 2);
    public record JPY() : CurrencyType("¥", "JP", "Japanese Yen", "円", 0);
    public record JOD() : CurrencyType("د.أ", "JO", "Jordanian Dinar", "دينار أردني", 3);
    public record KZT() : CurrencyType("₸", "KZ", "Kazakhstani Tenge", "Теңге", 2);
    public record KWD() : CurrencyType("د.ك", "KW", "Kuwaiti Dinar", "دينار كويتي", 3);
    public record KGS() : CurrencyType("⃀", "KG", "Kyrgyzstani Som", "Сом", 2);
    public record LAK() : CurrencyType("₭", "LA", "Lao Kip", "ກີບ", 2);
    public record LBP() : CurrencyType("ل.ل", "LB", "Lebanese Pound", "ليرة لبنانية", 2);
    public record MVR() : CurrencyType("Rf", "MV", "Maldivian Rufiyaa", "ރުފިޔާ", 2);
    public record MNT() : CurrencyType("₮", "MN", "Mongolian Tögrög", "Төгрөг", 2);
    public record NPR() : CurrencyType("₨", "NP", "Nepalese Rupee", "रुपैयाँ", 2);
    public record OMR() : CurrencyType("ر.ع.", "OM", "Omani Rial", "ريال عماني", 3);
    public record PKR() : CurrencyType("₨", "PK", "Pakistani Rupee", "روپیہ", 2);
    public record PHP() : CurrencyType("₱", "PH", "Philippine Peso", "Piso", 2);
    public record QAR() : CurrencyType("ر.ق", "QA", "Qatari Riyal", "ريال قطري", 2);
    public record SAR() : CurrencyType("ر.س", "SA", "Saudi Riyal", "ريال سعودي", 2);
    public record SGD() : CurrencyType("$", "SG", "Singapore Dollar", "Dollar Singapura", 2);
    public record KRW() : CurrencyType("₩", "KR", "South Korean Won", "원", 0);
    public record LKR() : CurrencyType("Rs", "LK", "Sri Lankan Rupee", "රුපියල්", 2);
    public record SYP() : CurrencyType("ل.س", "SY", "Syrian Pound", "ليرة سورية", 2);
    public record TWD() : CurrencyType("NT$", "TW", "New Taiwan Dollar", "新台幣", 2);
    public record TJS() : CurrencyType("ЅМ", "TJ", "Tajikistani Somoni", "Сомонӣ", 2);
    public record THB() : CurrencyType("฿", "TH", "Thai Baht", "บาท", 2);
    public record TRY() : CurrencyType("₺", "TR", "Turkish Lira", "Türk Lirası", 2);
    public record TMT() : CurrencyType("m", "TM", "Turkmenistani Manat", "Manat", 2);
    public record AED() : CurrencyType("د.إ", "AE", "UAE Dirham", "درهم إماراتي", 2);
    public record UZS() : CurrencyType("so'm", "UZ", "Uzbekistani Som", "Сўм", 2);
    public record VND() : CurrencyType("₫", "VN", "Vietnamese Dong", "Đồng", 0);
    public record YER() : CurrencyType("ر.ي", "YE", "Yemeni Rial", "ريال يمني", 2);

}
