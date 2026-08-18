using PocketMoney.Application.Model.Households;

namespace PocketMoney.Application.Contract;

/// <summary>Parent invitation flow (API Spec §3.5–3.7, SDS §5).</summary>
public interface IInvitationService
{
    Task<CreateInvitationResult> CreateAsync(string firebaseUid, string? email, string ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Accepts an invitation. The cap and one-household rules are re-checked
    /// inside a serializable transaction (SDS §5 step 5).
    /// </summary>
    Task<AcceptInvitationResult> AcceptAsync(string firebaseUid, string? email, string? token, string ipAddress, CancellationToken ct = default);

    Task<CancelInvitationResult> CancelAsync(string firebaseUid, Guid invitationId, string ipAddress, CancellationToken ct = default);
}

/// <summary>POST /household/invitations outcomes (API Spec §3.5).</summary>
public abstract record CreateInvitationResult
{
    public sealed record Created(InvitationResponse Invitation) : CreateInvitationResult;
    public sealed record ValidationFailed(string Detail) : CreateInvitationResult;
    public sealed record ParentCapReached : CreateInvitationResult;
    public sealed record InvitationPending : CreateInvitationResult;
    public sealed record ParentUnknown : CreateInvitationResult;
}

/// <summary>POST /household/invitations/accept outcomes (API Spec §3.6).</summary>
public abstract record AcceptInvitationResult
{
    public sealed record Accepted(AcceptInvitationResponse Household) : AcceptInvitationResult;
    public sealed record ValidationFailed(string Detail) : AcceptInvitationResult;
    public sealed record InvitationInvalid : AcceptInvitationResult;
    public sealed record InvitationExpired : AcceptInvitationResult;
    public sealed record ParentCapReached : AcceptInvitationResult;
    public sealed record AlreadyInHousehold : AcceptInvitationResult;
}

/// <summary>DELETE /household/invitations/{id} outcomes (API Spec §3.7).</summary>
public abstract record CancelInvitationResult
{
    public sealed record Cancelled : CancelInvitationResult;
    /// <summary>404 — not in caller's household, or already accepted/expired.</summary>
    public sealed record NotFound : CancelInvitationResult;
    public sealed record SenderOnly : CancelInvitationResult;
    public sealed record ParentUnknown : CancelInvitationResult;
}

/// <summary>
/// Dispatches the invitation email (SDS §5 step 2). Pluggable: V1 ships a
/// logging dispatcher; SendGrid plugs in here when a key is provisioned.
/// </summary>
public interface IInvitationEmailDispatcher
{
    /// <param name="invitedEmail">Invited parent's email address.</param>
    /// <param name="token">The raw one-time invitation token (never persisted).</param>
    Task DispatchAsync(string invitedEmail, string token, CancellationToken ct = default);
}
