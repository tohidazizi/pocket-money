using System.Text;

namespace PocketMoney.Application;

/// <summary>
/// Opaque keyset cursor for the transaction timeline (SDS §12.2).
/// Encodes the <c>(created_at, id)</c> pair of the last returned row as
/// URL-safe Base64. Clients treat it as a black box; the server rejects
/// malformed cursors with 400.
/// </summary>
public static class TimelineCursor
{
    /// <summary>Encodes the keyset of the last row returned.</summary>
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        var raw = $"{createdAt:O}|{id:D}";
        return ToBase64Url(Encoding.UTF8.GetBytes(raw));
    }

    /// <summary>Decodes a client-supplied cursor; null on any malformation.</summary>
    public static (DateTimeOffset CreatedAt, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var raw = Encoding.UTF8.GetString(FromBase64Url(cursor));
            var sep = raw.IndexOf('|');
            if (sep <= 0 || sep >= raw.Length - 1)
                return null;

            if (!DateTimeOffset.TryParse(raw[..sep],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var createdAt))
                return null;
            if (!Guid.TryParse(raw[(sep + 1)..], out var id))
                return null;

            return (createdAt, id);
        }
        catch
        {
            return null;
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
