namespace PocketMoney.Application.Model.Children;

/// <summary>
/// POST /api/v1/auth/child/login request body (API Spec §2.1).
/// </summary>
public sealed record ChildLoginRequest(string? AccountId, string? Pin);

/// <summary>Login success payload (API Spec §2.1).</summary>
public sealed record ChildLoginResponse(string Token, DateTimeOffset ExpiresAt, ChildSummaryDto Child);

/// <summary>Public child summary (API Spec §2.1).</summary>
public sealed record ChildSummaryDto(Guid Id, string AccountId, string DisplayName);
