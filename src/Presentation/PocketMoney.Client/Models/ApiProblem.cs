namespace PocketMoney.Client.Models;

/// <summary>
/// Client-side problem model mirroring RFC 9457 ProblemDetails
/// (API Spec §1.4). The `code` extension drives UI behavior.
/// </summary>
public sealed class ApiProblem
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int Status { get; set; }
    public string Detail { get; set; } = "";
    public string Code { get; set; } = "";
    /// <summary>Timed lockouts only (SDS §7.0): when the lock lifts.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public bool IsLocked => Status == 423;
    public bool IsIpBanned => Code == "ip_banned";
    public bool IsPermanentLock => IsLocked && LockedUntil is null;

    /// <summary>Human message for inline error surfaces (UI Spec §6).</summary>
    public string Display => string.IsNullOrWhiteSpace(Detail) ? Title : Detail;
}
