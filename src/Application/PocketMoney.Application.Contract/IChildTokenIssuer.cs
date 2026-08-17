namespace PocketMoney.Application.Contract;

/// <summary>Issues child authentication tokens (SDS §3.2, FR-C2).</summary>
public interface IChildTokenIssuer
{
    ChildToken Issue(Guid childId, Guid householdId, Guid securityStamp);
}

/// <summary>Result of issuing a child token.</summary>
public sealed record ChildToken(string Token, DateTimeOffset ExpiresAt);
