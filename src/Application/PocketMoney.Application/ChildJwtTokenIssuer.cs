using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using PocketMoney.Application.Contract;
using PocketMoney.Global;
using PocketMoney.Persistence.Data;

namespace PocketMoney.Application;

/// <summary>
/// Issues the custom 365-day child JWT (SDS §3.2, FR-C2).
/// Claims: child_id, household_id, security_stamp — the API validation
/// middleware compares security_stamp against the DB record and rejects
/// stale tokens with 401 security_stamp_mismatch.
/// </summary>
public sealed class ChildJwtTokenIssuer : IChildTokenIssuer
{
    public const string ClaimChildId = "child_id";
    public const string ClaimHouseholdId = "household_id";
    public const string ClaimSecurityStamp = "security_stamp";

    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;

    public ChildJwtTokenIssuer(IConfiguration configuration)
    {
        var secret = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Missing configuration 'Jwt:Key' (SDS §1.4).");
        if (Encoding.UTF8.GetByteCount(secret) < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 256 bits (CI/CD doc §4.4).");

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = configuration["Jwt:Issuer"] ?? "pocketmoney-api";
    }

    public ChildToken Issue(Guid childId, Guid householdId, Guid securityStamp)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(Constants.Child.TokenLifetimeDays);

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: "pocketmoney-child",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, childId.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
                new Claim(ClaimChildId, childId.ToString("D")),
                new Claim(ClaimHouseholdId, householdId.ToString("D")),
                new Claim(ClaimSecurityStamp, securityStamp.ToString("D")),
            ],
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return new ChildToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    /// <summary>
    /// SDS §3.2: the child JWT's `security_stamp` claim must match the DB
    /// record — rotated on PIN reset / manual lock / unlock. A stale stamp
    /// means the session was revoked; the device must re-authenticate.
    /// Also rejects tokens whose child no longer exists (household deleted).
    /// </summary>
    public static async Task<bool> ValidateSecurityStampAsync(
        ClaimsPrincipal principal, PocketMoneyDbContext db)
    {
        if (!Guid.TryParse(principal.FindFirst(ClaimChildId)?.Value, out var childId))
            return false;
        if (!Guid.TryParse(principal.FindFirst(ClaimSecurityStamp)?.Value, out var stamp))
            return false;

        var child = await db.Children.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == childId);

        return child is not null && child.SecurityStamp == stamp;
    }
}
