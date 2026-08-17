namespace PocketMoney.Global;

/// <summary>
/// Currency is a closed, hardcoded set — not free text (SDS §2.1.1).
/// </summary>
public abstract partial record CurrencyType
{
    /// <summary>
    /// The special "Point" currency is used for in-app rewards and is not a real-world currency.
    /// </summary>
    public const string PointKey = nameof(Point);

    /// <summary>
    /// The maximum length of the currency key (discriminator) used for persistence and identification.
    /// </summary>
    public const int KeyMaxLength = 16;

    /// <summary>
    /// The symbol of the currency (e.g., "$", "€", "£").
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// The country code associated with the currency (e.g., "US", "FR", "IR").
    /// </summary>
    public string Country { get; }

    /// <summary>
    /// The display title of the currency (e.g., "US Dollar", "Euro", "Iranian Rial").
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// The native title of the currency, which may differ from the display title (e.g., "ریال" for Iranian Rial).
    /// </summary>
    public string NativeTitle { get; }

    /// <summary>
    /// The number of decimal digits used for the currency (e.g., 2 for USD, 3 for OMR).
    /// </summary>
    public byte DecimalDigits { get; }
    
    /// <summary>
    /// Persisted discriminator: the concrete record's type name (e.g. "IRR", "Point")
    /// </summary>
    public string Key => GetType().Name;

    /// <summary>
    /// Prevents derivation outside this scope
    /// </summary>
    protected CurrencyType(string symbol, string country, string title, string? nativeTitle = null, byte decimalDigits = 2)
    {
        if(Key.Length > KeyMaxLength)
            throw new InvalidOperationException($"Currency key '{Key}' exceeds {KeyMaxLength} characters.");

        if(decimalDigits < 0 || decimalDigits > 3)
            throw new InvalidOperationException($"Invalid number of decimal digits '{decimalDigits}' for currency '{Key}'. Number of decimal digits must be between 0 and 3.");

        Symbol = symbol;
        Country = country;
        Title = title;
        NativeTitle = string.IsNullOrWhiteSpace(nativeTitle) ? title : nativeTitle;
        DecimalDigits = decimalDigits;
    }

    /// <summary>
    /// The special "Point" currency is used for in-app rewards and is not a real-world currency.
    /// </summary>
    public record Point() : CurrencyType("🪙", "World", PointKey);

    /// <summary>
    /// Auto-register all concrete CurrencyType records (including those in partial files)
    /// </summary>
    private static readonly IReadOnlyDictionary<string, CurrencyType> _all =
        typeof(CurrencyType).Assembly
            .GetTypes()
            .Where(t => t.IsSubclassOf(typeof(CurrencyType)) && !t.IsAbstract)
            .Select(t => (CurrencyType)Activator.CreateInstance(t)!)
            .ToDictionary(c => c.Key, c => c);

    /// <summary>
    /// Returns all supported currencies as a read-only collection. See: <see href="https://en.wikipedia.org/wiki/ISO_4217"/>
    /// </summary>
    public static IReadOnlyCollection<CurrencyType> Supported => _all.Values as IReadOnlyCollection<CurrencyType> ?? [];

    /// <summary>
    /// null when the key is unknown — callers reject with 400
    /// </summary>
    /// <param name="key">The currency key to parse.</param>
    /// <returns>The corresponding CurrencyType instance, or null if the key is unknown.</returns>
    public static CurrencyType? Parse(string key) => _all.TryGetValue(key, out var c) ? c : null;

    /// <summary>
    /// Attempts to parse a currency key into a CurrencyType instance.
    /// </summary>
    /// <param name="key">The currency key to parse.</param>
    /// <param name="currencyType">The resulting CurrencyType instance, or null if the key is unknown.</param>
    /// <returns>true if the key was successfully parsed; otherwise, false.</returns>
    public static bool TryParse(string key, out CurrencyType? currencyType)
    {
        currencyType = Parse(key);
        return currencyType is not null;
    }
}
