namespace PocketMoney.Client.Services;

/// <summary>
/// Runtime configuration: API base URL. In dev the client (5117) and API
/// (5199) are separate origins — CORS on the API permits the client.
/// Override via <c>pmApiBase</c> in localStorage for custom deployments.
/// </summary>
public sealed class ApiEndpoints
{
    public const string DefaultBase = "http://localhost:5199";

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
}
