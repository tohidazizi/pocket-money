namespace PocketMoney.Authentication;

/// <summary>
/// Firebase ID-token authentication for parents (SDS §7.1.2, API Spec §1.2).
/// Verification uses Google's PUBLIC JWKS — no service account or private
/// key is required; the only configuration is the Firebase projectId.
/// </summary>
public static class FirebaseAuthDefaults
{
    public const string Scheme = "Firebase";

    /// <summary>Firebase ID tokens carry the user UID in `user_id`.</summary>
    public const string UserIdClaim = "user_id";

    /// <summary>Firebase ID tokens carry the verified email in `email`.</summary>
    public const string EmailClaim = "email";

    public static string Issuer(string projectId) =>
        $"https://securetoken.google.com/{projectId}";

    /// <summary>
    /// OIDC metadata document publishing the JWKS URI. Consumed by
    /// Microsoft.IdentityModel's ConfigurationManager (auto-refreshing cache).
    /// </summary>
    public static string MetadataAddress(string projectId) =>
        $"https://securetoken.google.com/{projectId}/.well-known/openid-configuration";
}

/// <summary>Custom 365-day child JWT scheme (SDS §3.2).</summary>
public static class ChildAuthDefaults
{
    public const string Scheme = "Child";
}
