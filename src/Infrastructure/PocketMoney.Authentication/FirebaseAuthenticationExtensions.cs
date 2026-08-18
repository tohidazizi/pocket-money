using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PocketMoney.Authentication;

/// <summary>
/// Registers the Firebase ID-token bearer scheme (API Spec §1.2 "Parent").
/// Issuer/audience are derived from the Firebase projectId; signing keys are
/// resolved from Google's public JWKS with an auto-refreshing configuration
/// manager — no secrets involved.
/// </summary>
public static class FirebaseAuthenticationExtensions
{
    public const string ProjectIdConfigurationKey = "Firebase:ProjectId";

    public static AuthenticationBuilder AddFirebaseAuthentication(
        this AuthenticationBuilder builder, IConfiguration configuration)
    {
        var projectId = configuration[ProjectIdConfigurationKey]
            ?? throw new InvalidOperationException(
                $"Missing configuration '{ProjectIdConfigurationKey}' (SDS §1.4).");

        // Auto-refreshing OIDC metadata → JWKS cache. The JwtBearer handler
        // re-fetches on unknown signing keys (token replay after key rotation).
        var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            FirebaseAuthDefaults.MetadataAddress(projectId),
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });

        builder.AddJwtBearer(FirebaseAuthDefaults.Scheme, options =>
        {
            // Keep raw claim names (`user_id`, `email`) — no inbound mapping.
            options.MapInboundClaims = false;
            options.ConfigurationManager = configurationManager;
            options.Events = JwtBearerChallenge.ProblemDetailsEvents();

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = FirebaseAuthDefaults.Issuer(projectId),
                ValidateAudience = true,
                ValidAudience = projectId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
            };
        });

        return builder;
    }
}
