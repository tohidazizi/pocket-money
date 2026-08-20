using System.Text.Json;

namespace PocketMoney.Client.Services;

/// <summary>
/// Runtime configuration: API base URL. In dev the client (5117) and API
/// (5199) are separate origins — CORS on the API permits the client.
/// Production resolution: <c>pm-config.json</c> is served next to the client
/// bundle and can be rewritten per environment without rebuilding the WASM
/// binary. On any failure we fall back to <see cref="DefaultBase"/> so a
/// missing config never breaks local development.
/// </summary>
public sealed class ApiEndpoints
{
    public const string DefaultBase = "http://localhost:5199";
    private const string ConfigPath = "pm-config.json";

    public string BaseUrl { get; }

    public ApiEndpoints()
    {
        BaseUrl = DefaultBase;
    }

    public ApiEndpoints(string baseUrl) => BaseUrl = baseUrl.TrimEnd('/');

    public string V1 => $"{BaseUrl}/api/v1";
    public string ChildLogin => $"{V1}/auth/child/login";
    public string Household => $"{V1}/household";
    public string HouseholdChildren => $"{V1}/household/children";
    public string HouseholdTransactions => $"{V1}/household/transactions";
    public string LedgerHub => $"{BaseUrl}/hubs/ledger";

    /// <summary>
    /// Resolves the API base at boot. Never throws — any failure falls back
    /// to <see cref="DefaultBase"/> so local development keeps working.
    /// </summary>
    public static async Task<ApiEndpoints> LoadAsync(HttpClient http)
    {
        try
        {
            using var response = await http.GetAsync(ConfigPath, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var config = JsonSerializer.Deserialize<PmConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (!string.IsNullOrWhiteSpace(config?.ApiBase))
                {
                    return new ApiEndpoints(config.ApiBase);
                }
            }
        }
        catch
        {
            // Missing/unparseable config — fall through to default.
        }

        return new ApiEndpoints(DefaultBase);
    }

    private sealed record PmConfig(string? ApiBase);
}
