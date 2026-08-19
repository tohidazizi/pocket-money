using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using PocketMoney.Application.Model.Children;
using PocketMoney.Application.Model.Common;
using PocketMoney.Application.Model.Households;
using PocketMoney.Application.Model.Transactions;
using PocketMoney.Client.Models;

namespace PocketMoney.Client.Services;

/// <summary>
/// Typed API client over the Pocket-Money REST surface (API Spec).
/// Reuses the shared <c>Application.Model</c> records so request/response
/// shapes never drift from the server. Every non-2xx response is parsed
/// into an RFC 9457 <see cref="ApiProblem"/> and surfaced as
/// <see cref="ApiException"/> (UI Spec §6).
/// </summary>
public sealed class PocketMoneyApiClient
{
    private readonly HttpClient _http;
    private readonly ApiEndpoints _endpoints;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public PocketMoneyApiClient(HttpClient http, ApiEndpoints endpoints)
    {
        _http = http;
        _endpoints = endpoints;
    }

    // ------------------------------------------------------------------
    // Auth — child (public)
    // ------------------------------------------------------------------
    public Task<ChildLoginResponse> ChildLoginAsync(string accountId, string pin, CancellationToken ct = default) =>
        PostAsync<ChildLoginRequest, ChildLoginResponse>(
            _endpoints.ChildLogin, new ChildLoginRequest(accountId, pin), bearer: null, ct);

    // ------------------------------------------------------------------
    // Household (parent JWT)
    // ------------------------------------------------------------------
    public Task<HouseholdResponse> GetHouseholdAsync(string bearer, CancellationToken ct = default) =>
        GetAsync<HouseholdResponse>(_endpoints.Household, bearer, ct);

    public Task UpdateHouseholdSettingsAsync(string bearer, UpdateHouseholdSettingsRequest request, CancellationToken ct = default) =>
        PutAsync<UpdateHouseholdSettingsRequest>(_endpoints.Household + "/settings", request, bearer, ct);

    public Task DeleteHouseholdAsync(string bearer, CancellationToken ct = default) =>
        DeleteAsync(_endpoints.Household, bearer, ct);

    // ------------------------------------------------------------------
    // Invitations (parent JWT)
    // ------------------------------------------------------------------
    public Task<InvitationResponse> CreateInvitationAsync(string bearer, CreateInvitationRequest request, CancellationToken ct = default) =>
        PostAsync<CreateInvitationRequest, InvitationResponse>(
            _endpoints.Household + "/invitations", request, bearer, ct);

    public Task<AcceptInvitationResponse> AcceptInvitationAsync(string bearer, AcceptInvitationRequest request, CancellationToken ct = default) =>
        PostAsync<AcceptInvitationRequest, AcceptInvitationResponse>(
            _endpoints.Household + "/invitations/accept", request, bearer, ct);

    public Task CancelInvitationAsync(string bearer, Guid invitationId, CancellationToken ct = default) =>
        DeleteAsync($"{_endpoints.Household}/invitations/{invitationId}", bearer, ct);

    // ------------------------------------------------------------------
    // Children (parent JWT)
    // ------------------------------------------------------------------
    public Task<CreateChildResponse> CreateChildAsync(string bearer, CreateChildRequest request, CancellationToken ct = default) =>
        PostAsync<CreateChildRequest, CreateChildResponse>(
            _endpoints.HouseholdChildren, request, bearer, ct);

    public Task ResetChildPinAsync(string bearer, Guid childId, ResetChildPinRequest request, CancellationToken ct = default) =>
        PutAsync<ResetChildPinRequest>($"{_endpoints.HouseholdChildren}/{childId}/pin", request, bearer, ct);

    public Task SetChildLockAsync(string bearer, Guid childId, SetChildLockRequest request, CancellationToken ct = default) =>
        PutAsync<SetChildLockRequest>($"{_endpoints.HouseholdChildren}/{childId}/lock", request, bearer, ct);

    public Task<ChildCurrencyResponse> ChangeChildCurrencyAsync(string bearer, Guid childId, ChangeChildCurrencyRequest request, CancellationToken ct = default) =>
        PutAsync<ChangeChildCurrencyRequest, ChildCurrencyResponse>(
            $"{_endpoints.HouseholdChildren}/{childId}/currency", request, bearer, ct);

    // ------------------------------------------------------------------
    // Parent PIN (parent JWT)
    // ------------------------------------------------------------------
    public Task SetParentPinAsync(string bearer, SetParentPinRequest request, CancellationToken ct = default) =>
        PutAsync<SetParentPinRequest>(_endpoints.Household + "/parents/me/pin", request, bearer, ct);

    /// <summary>
    /// Verifies the Parent Lock PIN for idle-unlock by setting the PIN to
    /// itself. There is no dedicated verify endpoint in V1 (API Spec §4.1);
    /// a 401 invalid_credentials means the PIN is wrong.
    /// </summary>
    public async Task<bool> VerifyParentPinAsync(string bearer, string pin, CancellationToken ct = default)
    {
        try
        {
            await SetParentPinAsync(bearer, new SetParentPinRequest(pin, pin), ct);
            return true;
        }
        catch (ApiException ex) when (ex.Code == "invalid_credentials")
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Child self (child JWT)
    // ------------------------------------------------------------------
    public Task<ChildMeResponse> GetChildMeAsync(string bearer, CancellationToken ct = default) =>
        GetAsync<ChildMeResponse>(_endpoints.HouseholdChildren + "/me", bearer, ct);

    // ------------------------------------------------------------------
    // Transactions
    // ------------------------------------------------------------------
    public Task<TransactionDto> CreateTransactionAsync(string bearer, CreateTransactionRequest request, CancellationToken ct = default) =>
        PostAsync<CreateTransactionRequest, TransactionDto>(
            _endpoints.HouseholdTransactions, request, bearer, ct);

    /// <summary>
    /// Keyset-paginated timeline (API Spec §6.2). Child callers pass their own
    /// JWT and see only their rows (the childId filter is ignored server-side).
    /// </summary>
    public async Task<TimelinePage> GetTransactionsAsync(
        string bearer,
        Guid? childId = null,
        string? type = null,
        DateOnly? from = null,
        DateOnly? to = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        string? search = null,
        string? cursor = null,
        byte? pageSize = null,
        CancellationToken ct = default)
    {
        var qs = new List<string>();
        if (childId is not null) qs.Add($"childId={childId}");
        if (!string.IsNullOrWhiteSpace(type)) qs.Add($"type={HttpUtility.UrlEncode(type)}");
        if (from is not null) qs.Add($"from={from:yyyy-MM-dd}");
        if (to is not null) qs.Add($"to={to:yyyy-MM-dd}");
        if (minAmount is not null) qs.Add($"minAmount={minAmount}");
        if (maxAmount is not null) qs.Add($"maxAmount={maxAmount}");
        if (!string.IsNullOrWhiteSpace(search)) qs.Add($"q={HttpUtility.UrlEncode(search)}");
        if (!string.IsNullOrWhiteSpace(cursor)) qs.Add($"cursor={HttpUtility.UrlEncode(cursor)}");
        if (pageSize is not null) qs.Add($"pageSize={pageSize}");

        var url = _endpoints.HouseholdTransactions + (qs.Count > 0 ? "?" + string.Join("&", qs) : "");
        return await GetAsync<TimelinePage>(url, bearer, ct);
    }

    // ------------------------------------------------------------------
    // Plumbing
    // ------------------------------------------------------------------
    private async Task<TResponse> GetAsync<TResponse>(string url, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuth(req, bearer);
        using var resp = await _http.SendAsync(req, ct);
        return await ReadAsync<TResponse>(resp, ct);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string url, TRequest body, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body, options: Json) };
        AddAuth(req, bearer);
        using var resp = await _http.SendAsync(req, ct);
        return await ReadAsync<TResponse>(resp, ct);
    }

    private async Task PutAsync<TRequest>(string url, TRequest body, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: Json) };
        AddAuth(req, bearer);
        using var resp = await _http.SendAsync(req, ct);
        await ReadAsync<JsonElement>(resp, ct); // discard body; throws on error
    }

    private async Task<TResponse> PutAsync<TRequest, TResponse>(string url, TRequest body, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body, options: Json) };
        AddAuth(req, bearer);
        using var resp = await _http.SendAsync(req, ct);
        return await ReadAsync<TResponse>(resp, ct);
    }

    private async Task DeleteAsync(string url, string? bearer, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        AddAuth(req, bearer);
        using var resp = await _http.SendAsync(req, ct);
        await ReadAsync<JsonElement>(resp, ct);
    }

    private static void AddAuth(HttpRequestMessage req, string? bearer)
    {
        if (!string.IsNullOrWhiteSpace(bearer))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new ApiException(ParseProblem(resp, body));
        }

        if (typeof(T) == typeof(JsonElement))
            return (T)(object)default(JsonElement);

        var stream = await resp.Content.ReadAsStreamAsync(ct);
        if (stream.CanSeek && stream.Length == 0)
            return default!;

        var result = await JsonSerializer.DeserializeAsync<T>(stream, Json, ct);
        return result!;
    }

    /// <summary>Parses an RFC 9457 body, tolerating missing extension fields.</summary>
    private static ApiProblem ParseProblem(HttpResponseMessage resp, string body)
    {
        var problem = new ApiProblem { Status = (int)resp.StatusCode, Title = resp.ReasonPhrase ?? "Error" };
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("type", out var t)) problem.Type = t.GetString() ?? "";
                if (root.TryGetProperty("title", out var ti)) problem.Title = ti.GetString() ?? problem.Title;
                if (root.TryGetProperty("status", out var s) && s.TryGetInt32(out var sv)) problem.Status = sv;
                if (root.TryGetProperty("detail", out var d)) problem.Detail = d.GetString() ?? "";
                if (root.TryGetProperty("code", out var c)) problem.Code = c.GetString() ?? "";
                if (root.TryGetProperty("lockedUntil", out var lu) && lu.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(lu.GetString(), out var dt))
                    problem.LockedUntil = dt;
            }
        }
        catch (JsonException)
        {
            problem.Detail = body; // non-ProblemDetails body
        }
        return problem;
    }
}
