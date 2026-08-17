namespace PocketMoney.Domain.Entities;

/// <summary>Global Login Attempt — append-only (SDS §2.3, entity 5).</summary>
public sealed class LoginAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string HttpRequestInfo { get; set; } = string.Empty;
    public bool IsSuccessful { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
