namespace PocketMoney.Global;

/// <summary>
/// Currency is a closed, hardcoded set — not free text (SDS §2.1.1).
/// Each entry carries its symbol, country, display titles, and its own decimal precision.
/// </summary>
public abstract record CurrencyType
{
    public const string PointKey = nameof(Point);
    public const int KeyMaxLength = 16;

    public string Symbol { get; }
    public string Country { get; }
    public string Title { get; }
    public string NativeTitle { get; }
    public byte DecimalDigits { get; }

    /// <summary>Persisted discriminator: the concrete record's type name (e.g. "IRR", "Point").</summary>
    public string Key => GetType().Name;

    // Prevents derivation outside this scope.
    private CurrencyType(string symbol, string country, string title, string? nativeTitle = null, byte decimalDigits = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(country);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (Key.Length > KeyMaxLength)
            throw new InvalidOperationException($"Currency key '{Key}' exceeds {KeyMaxLength} characters.");
        if (decimalDigits > 3)
            throw new ArgumentOutOfRangeException(nameof(decimalDigits), "Must be between 0 and 3.");

        Symbol = symbol;
        Country = country;
        Title = title;
        NativeTitle = string.IsNullOrWhiteSpace(nativeTitle) ? title : nativeTitle;
        DecimalDigits = decimalDigits;
    }

    public record Point() : CurrencyType("🪙", "World", PointKey);
    public record IRR() : CurrencyType("ريال", "IR", "Iranian Rial", "ریال");
    public record IRRT() : CurrencyType("ت", "IR", "Iranian Toman", "تومان");
    public record IRRHT() : CurrencyType("ه‍.ت", "IR", "Iranian Thousand of Tomans", "هزار تومان", 3);
    public record USD() : CurrencyType("$", "US", "US Dollar", decimalDigits: 2);
    public record CAD() : CurrencyType("$", "CA", "Canadian Dollar", decimalDigits: 2);
    public record EuroFr() : CurrencyType("€", "FR", "Euro", decimalDigits: 2);
    public record EuroDe() : CurrencyType("€", "DE", "Euro", decimalDigits: 2);
    public record Pound() : CurrencyType("£", "GB", "GBP", decimalDigits: 2);
    public record OMR() : CurrencyType("ر.ع.", "OM", "Omani Rial", "ريال عماني", 3);
    // Remaining currencies/countries are added during implementation (SDS §2.1.1).

    private static readonly Dictionary<string, CurrencyType> _all =
        new Dictionary<string, CurrencyType>
        {
            [nameof(Point)] = new Point(),
            [nameof(IRR)] = new IRR(),
            [nameof(IRRT)] = new IRRT(),
            [nameof(IRRHT)] = new IRRHT(),
            [nameof(USD)] = new USD(),
            [nameof(CAD)] = new CAD(),
            [nameof(EuroFr)] = new EuroFr(),
            [nameof(EuroDe)] = new EuroDe(),
            [nameof(Pound)] = new Pound(),
            [nameof(OMR)] = new OMR(),
        };

    public static IReadOnlyCollection<CurrencyType> Supported => _all.Values;

    /// <summary>Null when the key is unknown — callers reject with 400.</summary>
    public static CurrencyType? Parse(string key) => _all.TryGetValue(key, out var c) ? c : null;

    public static bool TryParse(string key, out CurrencyType? currencyType)
    {
        currencyType = Parse(key);
        return currencyType is not null;
    }
}
