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

            var expired = context.ErrorDescription?.Contains("expired", StringComparison.OrdinalIgnoreCase) is true
                || context.ErrorDescription?.Contains("lifetime", StringComparison.OrdinalIgnoreCase) is true;

            var problem = new ProblemDetails
            {
                Type = "https://pocketmoney.app/errors/token-invalid",
                Status = StatusCodes.Status401Unauthorized,
                Title = expired ? "Token expired" : "Unauthorized",
                Detail = expired
                    ? "The bearer token has expired."
                    : "The bearer token is missing or invalid.",
            };
            problem.Extensions["code"] = expired ? AuthErrorCodes.TokenExpired : AuthErrorCodes.TokenInvalid;

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            return context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
        },
    };
}
