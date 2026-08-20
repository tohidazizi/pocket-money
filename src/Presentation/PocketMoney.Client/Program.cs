using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using PocketMoney.Client;
using PocketMoney.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Services (SDS §1.3 client) ---
var http = new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(60),
};
builder.Services.AddScoped(_ => http);

// Runtime API base resolution (Railway deployment): pm-config.json is
// served next to the client bundle and can be rewritten per environment
// without rebuilding. On any failure we fall back to the dev default
// (localhost:5199) so a missing config never breaks local development.
var endpoints = await ApiEndpoints.LoadAsync(http);
builder.Services.AddSingleton(endpoints);
builder.Services.AddScoped(sp => new PocketMoneyApiClient(http, endpoints));

builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<ChildrenHistoryStore>();
builder.Services.AddScoped<FirebaseAuthService>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<InactivityTimerService>();
builder.Services.AddScoped<LedgerHubService>();

builder.Services.AddMudServices();

await builder.Build().RunAsync();
