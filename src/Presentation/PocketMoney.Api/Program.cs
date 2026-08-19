using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PocketMoney.Api;
using PocketMoney.Api.Endpoints;
using PocketMoney.Api.Hubs;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
using PocketMoney.Authentication;
using PocketMoney.Persistence;
using PocketMoney.Persistence.Data;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// --- Logging (SDS §1.5: Serilog) ---
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

// --- Persistence (SDS §1.3 Infrastructure) ---
builder.Services.AddPocketMoneyPersistence(builder.Configuration);

// --- Application services ---
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IChildTokenIssuer, ChildJwtTokenIssuer>();
builder.Services.AddScoped<IChildAuthService, ChildAuthService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IHouseholdService, HouseholdService>();
builder.Services.AddScoped<IInvitationService, InvitationService>();
builder.Services.AddScoped<IInvitationEmailDispatcher, LoggingInvitationEmailDispatcher>();
builder.Services.AddScoped<IChildService, ChildService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();

// --- Real-time ledger push (SDS §7.2) ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<ILedgerPushService, SignalRLedgerPushService>();

// --- Authentication: two bearer schemes (API Spec §1.2) ---
// Firebase scheme = parent JWTs (public JWKS verification, projectId only).
// Child scheme  = custom 365-day symmetric JWTs (SDS §3.2).
// Firebase is the DEFAULT scheme: parent routes authorize without naming it.
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Missing configuration 'Jwt:Key' (SDS §1.4).");
builder.Services
    .AddAuthentication(FirebaseAuthDefaults.Scheme)
    .AddFirebaseAuthentication(builder.Configuration)
    .AddJwtBearer(ChildAuthDefaults.Scheme, options =>
    {
        options.MapInboundClaims = false;
        options.Events = JwtBearerChallenge.ProblemDetailsEvents();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "pocketmoney-api",
            ValidateAudience = true,
            ValidAudience = "pocketmoney-child",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero, // 365-day tokens: no slack needed
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuerSigningKey = true,
        };

        // SDS §3.2 layer 3: 365-day tokens are revocable ONLY because the
        // stamp is checked against the DB on every request. Mismatch (PIN
        // reset / manual lock-unlock / missing child) → 401 with code
        // security_stamp_mismatch (rendered by the shared challenge handler),
        // forcing the child device to re-login.
        options.Events.OnTokenValidated = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices
                .GetRequiredService<PocketMoneyDbContext>();
            if (!await ChildJwtTokenIssuer.ValidateSecurityStampAsync(ctx.Principal!, db))
            {
                // Pin the code for the shared challenge handler (SDS §3.2).
                ctx.HttpContext.Items[PocketMoney.Authentication.AuthContextKeys.ErrorCode] =
                    PocketMoney.Authentication.AuthErrorCodes.SecurityStampMismatch;
                ctx.Fail("security_stamp_mismatch");
            }
        };
    });
builder.Services.AddAuthorization();

// --- ProblemDetails (SDS §7.0, RFC 9457) ---
builder.Services.AddProblemDetails();

// --- OpenAPI / Scalar (SDS §1.5, §7.0) ---
builder.Services.AddOpenApi();

// --- Forwarded headers: Railway sits behind a proxy; the IP-ban ladder
// must see real client IPs (CI/CD doc §4.4, SDS §10.2) ---
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// --- CORS for the Blazor WASM client (separate origin in dev & prod) ---
// Allowed origins from config; dev defaults cover the client dev server.
var clientOrigins = builder.Configuration.GetSection("Client:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5117", "https://localhost:7230"];
builder.Services.AddCors(options => options.AddPolicy("client", policy =>
    policy.WithOrigins(clientOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithHeaders("Authorization")));

var app = builder.Build();

// --- D-2 (approved): migrations on startup, forward-only ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PocketMoneyDbContext>();
    db.Database.Migrate();
}

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

// CORS must run before authentication so the SignalR negotiate preflight
// and API calls from the Blazor client origin are allowed.
app.UseCors("client");

if (!app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/healthz", async (PocketMoneyDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect
        ? Results.Ok(new { status = "healthy" })
        : Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Database unreachable");
});

var api = app.MapGroup("/api/v1");
api.MapChildLogin();
api.MapHousehold();
api.MapChildMe();
api.MapTransactions();

// --- Real-time ledger hub (SDS §7.2) ---
// Accepts BOTH bearer schemes: parents (Firebase) and children (own JWTs).
app.MapHub<LedgerHub>("/hubs/ledger")
    .RequireAuthorization(new AuthorizeAttribute
    {
        AuthenticationSchemes = $"{FirebaseAuthDefaults.Scheme},{ChildAuthDefaults.Scheme}",
    });

app.Run();

/// <summary>Test hook for WebApplicationFactory (SDS §13.2).</summary>
public partial class Program;
