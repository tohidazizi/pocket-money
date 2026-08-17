using System.Security.Cryptography;

namespace PocketMoney.Application;

/// <summary>
/// PIN hashing for child and parent PINs.
/// Implementation decision (SDS specifies the PinHash field but not the
/// algorithm): PBKDF2-SHA256 with 310,000 iterations (OWASP 2023 minimum),
/// 16-byte random salt. Stored format: "PBKDF2-SHA256$iterations$saltB64$hashB64".
/// A 4-digit PIN is extremely low-entropy, so the iteration count matters.
/// </summary>
public static class PinHasher
{
    private const string Scheme = "PBKDF2-SHA256";
    private const int Iterations = 310_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string pin, string storedHash)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme)
            return false;

        if (!int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        byte[] salt, expectedKey;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedKey = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualKey = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expectedKey.Length);
        return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
    }
}
