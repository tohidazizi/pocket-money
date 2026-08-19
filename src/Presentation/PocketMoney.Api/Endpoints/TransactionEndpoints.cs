using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Transactions;
using PocketMoney.Authentication;
using PocketMoney.Global;

namespace PocketMoney.Api.Endpoints;

/// <summary>
/// Transaction endpoints (API Spec §6, SDS §4 + §12).
/// POST rides the Firebase scheme (parents only); GET serves BOTH schemes —
/// child callers see strictly their own rows.
/// </summary>
public static class TransactionEndpoints
{
    private static string? FirebaseUid(HttpContext http) =>
        http.User.FindFirstValue(FirebaseAuthDefaults.UserIdClaim);

    private static string ClientIp(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static IResult ValidationProblem(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid input", detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationError });

    private static IResult NotFoundProblem() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found",
        detail: "No such child in your household.",
        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NotFound });

    public static void MapTransactions(this IEndpointRouteBuilder app)
    {
        // POST /household/transactions — parent JWT (API Spec §6.1)
        app.MapGroup("/household")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = FirebaseAuthDefaults.Scheme })
            .MapPost("/transactions", async (
                HttpContext http, ITransactionService transactions,
                CreateTransactionRequest request, CancellationToken ct) =>
            {
                var uid = FirebaseUid(http);
                if (uid is null)
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Unauthorized", detail: "Token carries no user id.",
                        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

                return await transactions.CreateAsync(uid, request, ClientIp(http), ct) switch
                {
                    CreateTransactionResult.Created c => Results.Ok(c.Transaction),
                    CreateTransactionResult.ValidationFailed v => ValidationProblem(v.Detail),
                    CreateTransactionResult.NotFound => NotFoundProblem(),
                    CreateTransactionResult.NegativeBalance => Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "Insufficient balance",
                        detail: "Debit rejected: the resulting balance would be negative.",
                        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.NegativeBalance }),
                    _ => throw new InvalidOperationException("Unhandled transaction result."),
                };
            });

        // GET /household/transactions — parent JWT / child JWT (API Spec §6.2)
        app.MapGroup("/household")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = $"{FirebaseAuthDefaults.Scheme},{ChildAuthDefaults.Scheme}",
            })
            .MapGet("/transactions", async (
                HttpContext http, ITransactionService transactions,
                Guid? childId, string? type, DateOnly? from, DateOnly? to,
                decimal? minAmount, decimal? maxAmount, string? q,
                string? cursor, int? pageSize, CancellationToken ct) =>
            {
                // --- type filter (API Spec §6.2: CREDIT | DEBIT) ---
                TransactionType? typeFilter = null;
                if (!string.IsNullOrWhiteSpace(type))
                {
                    if (!Enum.TryParse<TransactionType>(type.Trim(), ignoreCase: true, out var parsed))
                        return ValidationProblem("type must be CREDIT or DEBIT.");
                    typeFilter = parsed;
                }

                // --- pageSize (SDS §12.2: invalid → 400) ---
                var size = pageSize ?? Constants.Timeline.DefaultPageSize;
                if (size < 1 || size > Constants.Timeline.MaxPageSize)
                    return ValidationProblem(
                        $"pageSize must be between 1 and {Constants.Timeline.MaxPageSize}.");

                // --- opaque cursor (SDS §12.2) ---
                (DateTimeOffset CreatedAt, Guid Id)? keyset = null;
                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    keyset = TimelineCursor.Decode(cursor);
                    if (keyset is null)
                        return ValidationProblem("cursor is malformed.");
                }

                var query = new TimelineQuery(
                    childId, typeFilter, from, to, minAmount, maxAmount, q, keyset, size);

                // Child JWT callers see strictly their own rows — the childId
                // filter is ignored (API Spec §6.2, SDS §7.1 footnote).
                var callerChildId = http.User.FindFirstValue(ChildJwtTokenIssuer.ClaimChildId);
                if (callerChildId is not null && Guid.TryParse(callerChildId, out var ownChildId))
                {
                    var page = await transactions.GetChildTimelineAsync(ownChildId, query, ct);
                    return Results.Ok(page);
                }

                var uid = FirebaseUid(http);
                if (uid is null)
                    return Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Unauthorized", detail: "Token carries no user id.",
                        extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.TokenInvalid });

                return await transactions.GetHouseholdTimelineAsync(uid, query, ct) switch
                {
                    TimelineResult.Ok ok => Results.Ok(ok.Page),
                    TimelineResult.NotFound => NotFoundProblem(),
                    _ => throw new InvalidOperationException("Unhandled timeline result."),
                };
            });
    }
}
