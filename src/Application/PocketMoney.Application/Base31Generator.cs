using System.Security.Cryptography;
using PocketMoney.Global;

namespace PocketMoney.Application;

/// <summary>
/// Generates 5-character Base-31 child Account IDs (SDS §3.1, FR-P3).
/// Excludes O, I, S, U, Q to prevent visual confusion. Generation is
/// server-side only, during child profile creation; the client merely
/// displays the ID. Uniqueness collisions are retried by the caller — the
/// unique index on children.account_id (§2.4) is the final guarantee.
/// </summary>
public static class Base31Generator
{
    private static readonly byte AccountIdLength = Constants.Child.AccountIdLength;
    private static readonly string Alphabet = Constants.Base31Alphabet;

    public static string GenerateAccountId()
    {
        Span<byte> randomBytes = stackalloc byte[AccountIdLength];
        RandomNumberGenerator.Fill(randomBytes);

        Span<char> accountId = stackalloc char[AccountIdLength];
        for (int i = 0; i < AccountIdLength; i++)
        {
            accountId[i] = Alphabet[randomBytes[i] % Alphabet.Length];
        }

        return new string(accountId);
    }

    /// <summary>True when every character belongs to the Base-31 alphabet (SDS §9.2).</summary>
    public static bool IsValid(string? accountId)
    {
        if (accountId is null || accountId.Length != AccountIdLength)
            return false;

        foreach (var c in accountId)
        {
            if (Alphabet.IndexOf(c) < 0)
                return false;
        }
        return true;
    }
}
