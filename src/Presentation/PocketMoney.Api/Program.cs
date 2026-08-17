using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PocketMoney.Api;
using PocketMoney.Api.Endpoints;
using PocketMoney.Application;
using PocketMoney.Application.Contract;
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

// --- Child JWT authentication (SDS §3.2) ---
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Missing configuration 'Jwt:Key' (SDS §1.4).");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
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

var app = builder.Build();

// --- D-2 (approved): migrations on startup, forward-only ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PocketMoneyDbContext>();
    db.Database.Migrate();
}

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

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

app.Run();

/// <summary>Test hook for WebApplicationFactory (SDS §13.2).</summary>
public partial class Program;
