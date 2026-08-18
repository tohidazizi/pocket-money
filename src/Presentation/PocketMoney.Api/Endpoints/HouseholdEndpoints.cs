using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Households;
using PocketMoney.Authentication;

namespace PocketMoney.Api.Endpoints;

/// <summary>
/// Household endpoints (API Spec §3–4, SDS §7.1). All routes require the
/// Firebase scheme ("Parent"); /invitations/accept is Firebase-only.
/// </summary>
public static class HouseholdEndpoints
{
    private static IResult ValidationProblem(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid input",
        detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationError });

    /// <summary>Extracts the verified Firebase UID (`user_id`) from the caller.</summary>
    private static string? FirebaseUid(HttpContext http) =>
        http.User.FindFirstValue(FirebaseAuthDefaults.UserIdClaim);

    /// <summary>Verified email (`email`) — present when the account is email/password.</summary>
    private static string? FirebaseEmail(HttpContext http) =>
        http.User.FindFirstValue(FirebaseAuthDefaults.EmailClaim);

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static void MapHousehold(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/household")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = FirebaseAuthDefaults.Scheme });

        // GET /household — doubles as Auto-Registration (SDS §7.1.2)
        group.MapGet("/", async (HttpContext http, IHouseholdService households, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return Results.Ok(await households.GetOrCreateAsync(uid, FirebaseEmail(http), ClientIp(http), ct));
        });

        // PUT /household/settings
        group.MapPut("/settings", async (
            HttpContext http, IHouseholdService households,
            UpdateHouseholdSettingsRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return await households.UpdateSettingsAsync(uid, request.DisplayName, request.DefaultCurrencyKey, ClientIp(http), ct) switch
            {
                UpdateSettingsResult.Ok ok => Results.Ok(ok.Household),
                UpdateSettingsResult.ValidationFailed v => ValidationProblem(v.Detail),
                UpdateSettingsResult.ParentUnknown => Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Call GET /household first.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid }),
                _ => throw new InvalidOperationException("Unhandled settings result."),
            };
        });

        // DELETE /household — owner only (FR-P1)
        group.MapDelete("/", async (HttpContext http, IHouseholdService households, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return await households.DeleteAsync(uid, ClientIp(http), ct) switch
            {
                DeleteHouseholdResult.Deleted => Results.NoContent(),
                DeleteHouseholdResult.OwnerOnly => Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Owner only",
                    detail: "Only the household owner can delete the household.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.OwnerOnly }),
                DeleteHouseholdResult.ParentUnknown => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not found", detail: "No household for this account.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound }),
                _ => throw new InvalidOperationException("Unhandled delete result."),
            };
        });

        MapInvitations(group);
        MapParentPin(group);
    }

    private static void MapParentPin(RouteGroupBuilder group)
    {
        // PUT /household/parents/me/pin (API Spec §4.1)
        group.MapPut("/parents/me/pin", async (
            HttpContext http, IHouseholdService households,
            SetParentPinRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return await households.SetMyPinAsync(uid, request.CurrentPin, request.NewPin, ClientIp(http), ct) switch
            {
                SetParentPinResult.Ok => Results.Ok(new { }),
                SetParentPinResult.ValidationFailed v => ValidationProblem(v.Detail),
                SetParentPinResult.InvalidCredentials => Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid credentials",
                    detail: "The current PIN is incorrect.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.InvalidCredentials }),
                SetParentPinResult.ParentUnknown => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not found", detail: "No parent record for this account.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound }),
                _ => throw new InvalidOperationException("Unhandled PIN result."),
            };
        });
    }

    private static void MapInvitations(RouteGroupBuilder group)
    {
        // POST /household/invitations (API Spec §3.5)
        group.MapPost("/invitations", async (
            HttpContext http, IInvitationService invitations,
            CreateInvitationRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return await invitations.CreateAsync(uid, request.Email, ClientIp(http), ct) switch
            {
                CreateInvitationResult.Created c => Results.Ok(c.Invitation),
                CreateInvitationResult.ValidationFailed v => ValidationProblem(v.Detail),
                CreateInvitationResult.ParentCapReached => Conflict("parent_cap_reached",
                    "The household already has 2 parents."),
                CreateInvitationResult.InvitationPending => Conflict("invitation_pending",
                    "An invitation is already pending."),
                CreateInvitationResult.ParentUnknown => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not found", detail: "No household for this account.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound }),
                _ => throw new InvalidOperationException("Unhandled invitation result."),
            };
        });

        // POST /household/invitations/accept — Firebase JWT only (API Spec §3.6)
        group.MapPost("/invitations/accept", async (
            HttpContext http, IInvitationService invitations,
            AcceptInvitationRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return await invitations.AcceptAsync(uid, FirebaseEmail(http), request.Token, ClientIp(http), ct) switch
            {
                AcceptInvitationResult.Accepted a => Results.Ok(a.Household),
                AcceptInvitationResult.ValidationFailed v => ValidationProblem(v.Detail),
                AcceptInvitationResult.InvitationInvalid => Conflict("invitation_invalid",
                    "The invitation token is invalid."),
                AcceptInvitationResult.InvitationExpired => Conflict("invitation_expired",
                    "The invitation has expired."),
                AcceptInvitationResult.ParentCapReached => Conflict("parent_cap_reached",
                    "The household is already full."),
                AcceptInvitationResult.AlreadyInHousehold => Conflict("already_in_household",
                    "This account already belongs to a household."),
                _ => throw new InvalidOperationException("Unhandled accept result."),
            };
        });

        // DELETE /household/invitations/{id} — sender only (API Spec §3.7)
        group.MapDelete("/invitations/{invitationId:guid}", async (
            HttpContext http, IInvitationService invitations,
            Guid invitationId, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "Token carries no user id.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

            return await invitations.CancelAsync(uid, invitationId, ClientIp(http), ct) switch
            {
                CancelInvitationResult.Cancelled => Results.NoContent(),
                CancelInvitationResult.NotFound => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not found",
                    detail: "No such pending invitation in your household.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound }),
                CancelInvitationResult.SenderOnly => Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Sender only",
                    detail: "Only the parent who sent the invitation can cancel it.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.InvitationSenderOnly }),
                CancelInvitationResult.ParentUnknown => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not found", detail: "No household for this account.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound }),
                _ => throw new InvalidOperationException("Unhandled cancel result."),
            };
        });
    }

    private static IResult Conflict(string code, string detail) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Conflict",
        detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>PUT /household/parents/me/pin body (API Spec §4.1).</summary>
public sealed record SetParentPinRequest(string? CurrentPin, string? NewPin);
