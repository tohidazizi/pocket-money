using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Children;
using PocketMoney.Authentication;

namespace PocketMoney.Api.Endpoints;

/// <summary>
/// Children endpoints (API Spec §5). Parent operations ride the Firebase
/// scheme under /household; the child dashboard (§5.5) rides the Child
/// scheme and is scoped to the caller's own child_id (SDS §10 layer 3).
/// </summary>
public static class ChildEndpoints
{
    private static string? FirebaseUid(HttpContext http) =>
        http.User.FindFirstValue(FirebaseAuthDefaults.UserIdClaim);

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static IResult Unauthorized() => Results.Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized", detail: "Token carries no user id.",
        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

    private static IResult ValidationProblem(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid input", detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationError });

    private static IResult NotFoundProblem() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found",
        detail: "No such child in your household.",
        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound });

    /// <summary>Maps /household/children/{…} parent operations (Firebase scheme).</summary>
    public static void MapChildren(this RouteGroupBuilder householdGroup)
    {
        // POST /household/children (API Spec §5.1)
        householdGroup.MapPost("/children", async (
            HttpContext http, IChildService children,
            CreateChildRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Unauthorized();

            return await children.CreateAsync(uid, request.DisplayName, ClientIp(http), ct) switch
            {
                CreateChildResult.Created c => Results.Created(
                    $"/api/v1/household/children/{c.Child.Id}", c.Child),
                CreateChildResult.ValidationFailed v => ValidationProblem(v.Detail),
                CreateChildResult.ChildrenMaxReached => Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Conflict",
                    detail: $"The household already has {PocketMoney.Global.Constants.Child.ChildrenMax} children.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ChildrenMaxReached }),
                CreateChildResult.ParentUnknown => Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not found", detail: "No household for this account.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound }),
                _ => throw new InvalidOperationException("Unhandled create-child result."),
            };
        });

        // PUT /household/children/{id}/pin (API Spec §5.2)
        householdGroup.MapPut("/children/{childId:guid}/pin", async (
            HttpContext http, IChildService children,
            Guid childId, ResetChildPinRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Unauthorized();

            return await children.ResetPinAsync(uid, childId, request.NewPin, ClientIp(http), ct) switch
            {
                ChildActionResult.Ok => Results.Ok(new { }),
                ChildActionResult.ValidationFailed v => ValidationProblem(v.Detail),
                ChildActionResult.NotFound => NotFoundProblem(),
                ChildActionResult.AccountLocked => Results.Problem(
                    statusCode: StatusCodes.Status423Locked,
                    title: "Account locked",
                    detail: "The account is locked. Unlock it first; unlocking never requires a PIN change.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.AccountLocked }),
                _ => throw new InvalidOperationException("Unhandled pin-reset result."),
            };
        });

        // PUT /household/children/{id}/lock (API Spec §5.3)
        householdGroup.MapPut("/children/{childId:guid}/lock", async (
            HttpContext http, IChildService children,
            Guid childId, SetChildLockRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Unauthorized();

            if (request.Locked is null)
                return ValidationProblem("locked must be true or false.");

            return await children.SetLockAsync(uid, childId, request.Locked.Value, ClientIp(http), ct) switch
            {
                ChildActionResult.Ok => Results.Ok(new { }),
                ChildActionResult.NotFound => NotFoundProblem(),
                ChildActionResult.ValidationFailed v => ValidationProblem(v.Detail),
                ChildActionResult.AccountLocked => Results.Problem(
                    statusCode: StatusCodes.Status423Locked,
                    title: "Account locked", detail: "Unexpected lock state.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.AccountLocked }),
                _ => throw new InvalidOperationException("Unhandled lock result."),
            };
        });

        // PUT /household/children/{id}/currency (API Spec §5.4)
        householdGroup.MapPut("/children/{childId:guid}/currency", async (
            HttpContext http, IChildService children,
            Guid childId, ChangeChildCurrencyRequest request, CancellationToken ct) =>
        {
            var uid = FirebaseUid(http);
            if (uid is null)
                return Unauthorized();

            return await children.ChangeCurrencyAsync(uid, childId, request.CurrencyKey, ClientIp(http), ct) switch
            {
                ChangeCurrencyResult.Changed c => Results.Ok(c.Response),
                ChangeCurrencyResult.ValidationFailed v => ValidationProblem(v.Detail),
                ChangeCurrencyResult.NotFound => NotFoundProblem(),
                _ => throw new InvalidOperationException("Unhandled currency result."),
            };
        });
    }

    /// <summary>Maps GET /household/children/me (Child scheme, API Spec §5.5).</summary>
    public static void MapChildMe(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/household")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = ChildAuthDefaults.Scheme })
            .MapGet("/children/me", async (HttpContext http, IChildService children, CancellationToken ct) =>
            {
                var claim = http.User.FindFirstValue(ChildJwtTokenIssuer.ClaimChildId);
                if (!Guid.TryParse(claim, out var childId))
                    return Unauthorized();

                return await children.GetMeAsync(childId, ct) switch
                {
                    ChildMeResult.Ok ok => Results.Ok(ok.Response),
                    ChildMeResult.NotFound => NotFoundProblem(),
                    _ => throw new InvalidOperationException("Unhandled child-me result."),
                };
            });
    }
}
