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

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();
}
