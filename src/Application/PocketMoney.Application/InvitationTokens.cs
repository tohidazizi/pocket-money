using System.Security.Cryptography;
using System.Text;
using PocketMoney.Global;

namespace PocketMoney.Application;

/// <summary>
/// One-time invitation tokens (SDS §5). The raw token travels only inside the
/// invitation email link; the database stores its SHA-256 hash, so a leaked
/// DB dump cannot be used to accept invitations.
/// </summary>
public static class InvitationTokens
{
    /// <summary>256-bit random token, hex-encoded (URL-safe, 64 chars).</summary>
    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(Constants.Invitation.TokenBytes)).ToLowerInvariant();

    /// <summary>Deterministic SHA-256 hex hash persisted in household_invitations.token_hash.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
