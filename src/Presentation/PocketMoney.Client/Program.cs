using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using PocketMoney.Client;
using PocketMoney.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Services (SDS §1.3 client) ---
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(60),
});

var endpoints = new ApiEndpoints();
builder.Services.AddSingleton(endpoints);
builder.Services.AddScoped(sp => new PocketMoneyApiClient(
    sp.GetRequiredService<HttpClient>(), endpoints));

builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<ChildrenHistoryStore>();
builder.Services.AddScoped<FirebaseAuthService>();
builder.Services.AddScoped<AppState>();
builder.Services.AddScoped<InactivityTimerService>();
builder.Services.AddScoped<LedgerHubService>();

builder.Services.AddMudServices();

await builder.Build().RunAsync();
