using Microsoft.AspNetCore.Mvc;
using PocketMoney.Application.Contract;
using PocketMoney.Application.Model.Children;

namespace PocketMoney.Api.Endpoints;

/// <summary>
/// POST /api/v1/auth/child/login — public (API Spec §2.1, SDS §7.1).
/// Every attempt is audited by the service layer (SDS §3.3).
/// </summary>
public static class ChildLoginEndpoint
{
    public static void MapChildLogin(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/child/login", async (
            [FromBody] ChildLoginRequest request,
            HttpContext http,
            IChildAuthService authService,
            CancellationToken ct) =>
        {
            var clientInfo = new ClientInfo(
                IpAddress: http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                HttpRequestInfo: $"{http.Request.Method} {http.Request.Path} | UA: {http.Request.Headers.UserAgent}");

            var result = await authService.LoginAsync(request.AccountId, request.Pin, clientInfo, ct);

            return result switch
            {
                ChildLoginResult.Success s => Results.Ok(new ChildLoginResponse(
                    s.Token, s.ExpiresAt, new ChildSummaryDto(s.ChildId, s.AccountId, s.DisplayName))),

                ChildLoginResult.ValidationFailed v => Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid input",
                    detail: v.Detail,
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationError }),

                ChildLoginResult.InvalidCredentials => Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Invalid credentials",
                    detail: "The account ID or PIN is incorrect.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.InvalidCredentials }),

                ChildLoginResult.Locked l => Results.Json(new LockedErrorDetails
                {
                    Type = "https://pocketmoney.app/errors/account-locked",
                    Title = "Account locked",
                    Status = StatusCodes.Status423Locked,
                    Detail = "Too many failed attempts. The account is temporarily locked.",
                    LockedUntil = l.LockedUntil,
                    Extensions = { ["code"] = ErrorCodes.AccountLocked },
                }, statusCode: StatusCodes.Status423Locked),

                ChildLoginResult.PermanentlyLocked => Results.Problem(
                    statusCode: StatusCodes.Status423Locked,
                    title: "Account permanently locked",
                    detail: "The account is locked. Ask a parent to unlock it.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.AccountPermanentlyLocked }),

                ChildLoginResult.IpBanned b => Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Access temporarily banned",
                    detail: $"Too many failed attempts from this network. Banned until {b.BannedUntil:u}.",
                    extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.IpBanned }),

                _ => throw new InvalidOperationException("Unhandled login result."),
            };
        });
    }
}
