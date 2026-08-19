using System.Text.RegularExpressions;
using PocketMoney.Global;

namespace PocketMoney.Application;

/// <summary>
/// Input validation rules, SDS §9 (normative for V1). All strings arrive
/// already trimmed by the caller (SDS §9.1).
/// </summary>
public static partial class InputValidation
{
    /// <summary>PINs (child &amp; parent): exactly 4 digits (SDS §9.2).</summary>
    public static bool IsValidPin(string? pin) =>
        pin is not null && pin.Length == 4 && pin.All(char.IsDigit);

    /// <summary>
    /// Display names (SDS §9.2): Unicode letters, digits, space, '-', ''', '.'.
    /// Caller enforces the length cap (household 60 / person 100).
    /// </summary>
    public static bool IsValidDisplayName(string value) =>
        value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '\'' or '.');

    /// <summary>
    /// Invitation/parent email (SDS §9.2): ≤ 320, RFC-5322 shape.
    /// Deliberately a shape check — Firebase re-verifies the address at
    /// acceptance; an SMTP-level validation is out of scope for V1.
    /// </summary>
    public static bool IsValidEmail(string? email) =>
        email is not null
        && email.Length > 0
        && email.Length <= Constants.Parent.EmailMaxLength
        && EmailShape().IsMatch(email);

    /// <summary>
    /// Transaction amount (SDS §9.4): strictly positive, ≤ 9,999,999,999.999,
    /// fractional scale ≤ the child's currency DecimalDigits — rejected, never
    /// rounded.
    /// </summary>
    public const decimal MaxTransactionAmount = 9_999_999_999.999m;

    public static bool IsValidTransactionAmount(decimal amount, byte currencyDecimalDigits)
    {
        if (amount <= 0 || amount > MaxTransactionAmount)
            return false;

        // Scale check: amount * 10^digits must be integral.
        var scaled = decimal.Round(amount, currencyDecimalDigits);
        return scaled == amount;
    }

    /// <summary>
    /// Transaction reason sanitization (SDS §9.2): control characters
    /// (U+0000–U+001F, U+007F) stripped; emoji restricted to
    /// <see cref="Constants.Transaction.ReasonEmojiWhitelist"/> — non-whitelisted
    /// emoji are stripped at the API boundary.
    /// </summary>
    public static string SanitizeReason(string raw)
    {
        var sb = new System.Text.StringBuilder(raw.Length);
        var i = 0;

        while (i < raw.Length)
        {
            var cp = char.ConvertToUtf32(raw, i);
            var len = i + 1 < raw.Length && char.IsSurrogatePair(raw[i], raw[i + 1]) ? 2 : 1;

            // A trailing variation selector belongs to this grapheme —
            // consume it with the base and normalize it away on output.
            var seqLen = len;
            if (i + len < raw.Length && raw[i + len] == '\uFE0F')
                seqLen += 1;

            // Control characters: strip (SDS §9.2).
            if (cp <= 0x1F || cp == 0x7F || cp is 0x200D or 0xFE0F)
            {
                i += seqLen;
                continue;
            }

            if (IsEmojiCodepoint(cp))
            {
                if (WhitelistedReasonEmojis.Contains(cp))
                    sb.Append(raw, i, len); // keep base, drop variation selector
                // Non-whitelisted emoji: strip.
                i += seqLen;
                continue;
            }

            sb.Append(raw, i, len);
            i += len;
        }

        return sb.ToString().Trim();
    }

    /// <summary>Emoji-ish codepoint ranges relevant to V1 input.</summary>
    private static bool IsEmojiCodepoint(int cp) =>
        cp is >= 0x1F000 and <= 0x1FAFF   // pictographs, emoticons, symbols
        or >= 0x2600 and <= 0x27BF        // misc symbols & dingbats (☀ ☺ ⚽ ✏ ❤ …)
        or >= 0x2300 and <= 0x23FF        // misc technical (⏰ …)
        or >= 0x2B00 and <= 0x2BFF        // stars/arrows used as emoji (⭐ …)
        or >= 0x1F3FB and <= 0x1F3FF;     // skin-tone modifiers

    /// <summary>Base codepoints of the whitelist, variation selectors excluded.</summary>
    private static readonly HashSet<int> WhitelistedReasonEmojis = BuildWhitelist();

    private static HashSet<int> BuildWhitelist()
    {
        var set = new HashSet<int>();
        var s = Constants.Transaction.ReasonEmojiWhitelist;
        for (var i = 0; i < s.Length;)
        {
            var cp = char.ConvertToUtf32(s, i);
            if (cp is not (0xFE0F or 0x200D))
                set.Add(cp);
            i += char.IsSurrogatePair(s, i) ? 2 : 1;
        }
        return set;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();
}
