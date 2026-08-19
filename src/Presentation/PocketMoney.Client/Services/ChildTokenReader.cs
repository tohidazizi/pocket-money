using System.Text.Json;

namespace PocketMoney.Client.Services;

/// <summary>
/// Reads claims from a JWT owned by this device (child JWT or the Firebase
/// ID token) via base64url payload decode. The token belongs to this device,
/// so decoding it locally is not a security boundary — it saves a round
/// trip (e.g. recovering child_id for the SignalR group join, SDS §7.2, or
/// the Firebase uid for ownership checks).
/// </summary>
public static class ChildTokenReader
{
    public static Guid? ReadChildId(string jwt)
    {
        if (ReadClaim(jwt, "child_id") is { } raw && Guid.TryParse(raw, out var id))
            return id;
        return null;
    }

    /// <summary>Decodes one string claim from the payload (Firebase `sub` = uid).</summary>
    public static string? ReadClaim(string jwt, string claim)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length != 3) return null;
            var payload = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
