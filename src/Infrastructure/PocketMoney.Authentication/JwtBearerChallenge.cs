using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PocketMoney.Authentication;

/// <summary>Auth error codes (API Spec §1.4).</summary>
public static class AuthErrorCodes
{
    public const string TokenInvalid = "token_invalid";
    public const string TokenExpired = "token_expired";
    public const string SecurityStampMismatch = "security_stamp_mismatch";
}

/// <summary>
/// HttpContext item a token-validation event can set to pin the exact error
/// code the challenge response should carry. Deterministic — does not depend
/// on how the handler propagates Fail() messages into ErrorDescription.
/// </summary>
public static class AuthContextKeys
{
    public const string ErrorCode = "PocketMoney.AuthErrorCode";
}

/// <summary>
/// RFC 9457 ProblemDetails on bearer challenge (SDS §7.0): missing or
/// rejected tokens produce a JSON problem body instead of the default empty
/// 401. Shared by the Firebase (parent) and Child schemes.
/// </summary>
public static class JwtBearerChallenge
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JwtBearerEvents ProblemDetailsEvents() => new()
    {
        OnChallenge = context =>
        {
            context.HandleResponse();

            // Dual-scheme routes (GET /transactions, /hubs/ledger) challenge
            // BOTH schemes; the first write wins — the second must not
            // attempt to write to an already-started response.
            if (context.Response.HasStarted)
                return Task.CompletedTask;

            // Validation events pin the exact code via HttpContext items
            // (deterministic); fall back to inspecting the failure message.
            var pinnedCode = context.HttpContext.Items[AuthContextKeys.ErrorCode] as string;
            var stampMismatch = pinnedCode == AuthErrorCodes.SecurityStampMismatch
                || context.ErrorDescription?.Contains("security_stamp_mismatch", StringComparison.OrdinalIgnoreCase) is true;
            var expired = !stampMismatch && context.ErrorDescription?.Contains("expired", StringComparison.OrdinalIgnoreCase) is true;

            var problem = new ProblemDetails
            {
                Type = stampMismatch
                    ? "https://pocketmoney.app/errors/security-stamp-mismatch"
                    : "https://pocketmoney.app/errors/token-invalid",
                Status = StatusCodes.Status401Unauthorized,
                Title = stampMismatch ? "Session expired" : expired ? "Token expired" : "Unauthorized",
                Detail = stampMismatch
                    ? "This session is no longer valid. Please sign in again."
                    : expired
                        ? "The bearer token has expired."
                        : "The bearer token is missing or invalid.",
            };
            problem.Extensions["code"] = stampMismatch
                ? "security_stamp_mismatch"
                : expired ? AuthErrorCodes.TokenExpired : AuthErrorCodes.TokenInvalid;

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            return context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        },
    };
}
