using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Modules.Organization.UseCases;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;

/// <summary>
/// The connector's HTTP connection to the central service (a singleton of the module's container, disposed on unload,
/// so no socket or timer outlives the module): <see cref="OrganizationOptions.CentralUrl"/> as the base address, and
/// a client-credentials token from <see cref="OrganizationOptions.TokenEndpoint"/> on every call.
/// </summary>
public sealed class CentralHttp : IDisposable
{
    private readonly SocketsHttpHandler _sockets = new() { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    public CentralHttp(IOptions<OrganizationOptions> options, TimeProvider clock)
    {
        var o = options.Value;
        Tokens = new ServiceTokens(new HttpClient(_sockets, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(30) }, o, clock);
        var baseUrl = o.ConnectorEnabled ? new Uri(o.CentralUrl.EndsWith('/') ? o.CentralUrl : o.CentralUrl + "/") : null;
        Client = new HttpClient(new BearerHandler(Tokens) { InnerHandler = _sockets }, disposeHandler: false)
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public HttpClient Client { get; }

    public ServiceTokens Tokens { get; }

    public void Dispose()
    {
        Client.Dispose();
        Tokens.Dispose();
        _sockets.Dispose();
    }

    private sealed class BearerHandler(ServiceTokens tokens) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (await tokens.GetAsync(ct).ConfigureAwait(false) is { } token)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await base.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Unauthorized) tokens.Forget();
            return response;
        }
    }
}

/// <summary>Client-credentials access tokens for the connector, cached until shortly before they expire.</summary>
public sealed class ServiceTokens(HttpClient http, OrganizationOptions options, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _expires;

    /// <summary>A valid token, or null when no token endpoint is configured.</summary>
    public async Task<string?> GetAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.TokenEndpoint)) return null;
        if (_token is { } cached && clock.GetUtcNow() < _expires) return cached;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_token is { } again && clock.GetUtcNow() < _expires) return again;
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.ClientId,
                ["client_secret"] = options.ClientSecret
            };
            if (!string.IsNullOrWhiteSpace(options.ClientScope)) form["scope"] = options.ClientScope;
            using var response = await http.PostAsync(options.TokenEndpoint, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"The token endpoint refused the connector's client ({(int)response.StatusCode}).", null, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct).ConfigureAwait(false)
                       ?? throw new HttpRequestException("The token endpoint returned no token.");
            _token = body.AccessToken;
            _expires = clock.GetUtcNow() + TimeSpan.FromSeconds(Math.Max(30, body.ExpiresIn - 60));
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The central service refused the token: fetch a new one next time.</summary>
    public void Forget() => _token = null;

    public void Dispose()
    {
        http.Dispose();
        _gate.Dispose();
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

/// <summary>A call to the central service failed with a status (403, 404, 409, ...); <see cref="Detail"/> is its problem detail.</summary>
public sealed class OrganizationCallException(HttpStatusCode status, string? detail)
    : HttpRequestException($"The central Organization service answered {(int)status}{(string.IsNullOrEmpty(detail) ? "" : $": {detail}")}", null, status)
{
    public string? Detail { get; } = detail;
}

/// <summary>
/// <see cref="IOrganizationClient"/> over HTTP: the central management API (<c>/api/organization</c> of the central
/// deployment), called as the connector's service client. The connector uses the seeds and feed operations; the
/// others are there for tools and services that act as a client with more rights.
/// </summary>
public sealed class CentralOrganizationClient(CentralHttp central) : IOrganizationClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Http => central.Client.BaseAddress is null
        ? throw new InvalidOperationException("Organization:CentralUrl is not set: the connector has no central service to call.")
        : central.Client;

    private static string Org(Guid orgId) => $"orgs/{orgId}";
    private static string E(string value) => Uri.EscapeDataString(value);

    public Task<IReadOnlyList<OrgDto>> ListOrgsAsync(CancellationToken ct = default) => Get<IReadOnlyList<OrgDto>>("orgs", ct);
    public Task<OrgDto> GetOrgAsync(Guid orgId, CancellationToken ct = default) => Get<OrgDto>(Org(orgId), ct);
    public Task<OrgDto> CreateOrgAsync(CreateOrgRequest request, CancellationToken ct = default) => Send<OrgDto>(HttpMethod.Post, "orgs", request, ct);
    public Task<OrgDto> UpdateOrgAsync(Guid orgId, UpdateOrgRequest request, CancellationToken ct = default) => Send<OrgDto>(HttpMethod.Put, Org(orgId), request, ct);
    public Task<OrgDto> SetOrgAdminsAsync(Guid orgId, SetOrgAdminsRequest request, CancellationToken ct = default) => Send<OrgDto>(HttpMethod.Put, $"{Org(orgId)}/admins", request, ct);

    public Task<IReadOnlyList<UnitTypeDto>> ListUnitTypesAsync(Guid orgId, CancellationToken ct = default) => Get<IReadOnlyList<UnitTypeDto>>($"{Org(orgId)}/unit-types", ct);
    public Task<UnitTypeDto> CreateUnitTypeAsync(Guid orgId, CreateUnitTypeRequest request, CancellationToken ct = default) => Send<UnitTypeDto>(HttpMethod.Post, $"{Org(orgId)}/unit-types", request, ct);

    public Task<IReadOnlyList<OrgUnitDto>> ListUnitsAsync(Guid orgId, CancellationToken ct = default) => Get<IReadOnlyList<OrgUnitDto>>($"{Org(orgId)}/units", ct);
    public Task<OrgUnitDto> GetUnitAsync(Guid orgId, Guid unitId, CancellationToken ct = default) => Get<OrgUnitDto>($"{Org(orgId)}/units/{unitId}", ct);
    public Task<UnitTenantDto> GetUnitTenantAsync(Guid orgId, Guid unitId, CancellationToken ct = default) => Get<UnitTenantDto>($"{Org(orgId)}/units/{unitId}/tenant", ct);
    public Task<OrgUnitDto> CreateUnitAsync(Guid orgId, CreateUnitRequest request, CancellationToken ct = default) => Send<OrgUnitDto>(HttpMethod.Post, $"{Org(orgId)}/units", request, ct);
    public Task<OrgUnitDto> UpdateUnitAsync(Guid orgId, Guid unitId, UpdateUnitRequest request, CancellationToken ct = default) => Send<OrgUnitDto>(HttpMethod.Put, $"{Org(orgId)}/units/{unitId}", request, ct);
    public Task<OrgUnitDto> MoveUnitAsync(Guid orgId, Guid unitId, MoveUnitRequest request, CancellationToken ct = default) => Send<OrgUnitDto>(HttpMethod.Post, $"{Org(orgId)}/units/{unitId}/move", request, ct);
    public Task RemoveUnitAsync(Guid orgId, Guid unitId, CancellationToken ct = default) => Send(HttpMethod.Delete, $"{Org(orgId)}/units/{unitId}", null, ct);

    public Task<IReadOnlyList<PersonDto>> ListPersonsAsync(Guid orgId, CancellationToken ct = default) => Get<IReadOnlyList<PersonDto>>($"{Org(orgId)}/persons", ct);
    public Task<PersonDto> CreatePersonAsync(Guid orgId, CreatePersonRequest request, CancellationToken ct = default) => Send<PersonDto>(HttpMethod.Post, $"{Org(orgId)}/persons", request, ct);
    public Task<PersonDto> UpdatePersonAsync(Guid orgId, Guid personId, UpdatePersonRequest request, CancellationToken ct = default) => Send<PersonDto>(HttpMethod.Put, $"{Org(orgId)}/persons/{personId}", request, ct);

    public Task<IReadOnlyList<MembershipDto>> ListMembershipsAsync(Guid orgId, Guid? unitId = null, Guid? personId = null, CancellationToken ct = default)
    {
        var query = string.Join('&', new[] { unitId is { } u ? $"unitId={u}" : null, personId is { } p ? $"personId={p}" : null }.OfType<string>());
        return Get<IReadOnlyList<MembershipDto>>($"{Org(orgId)}/memberships{(query.Length > 0 ? "?" + query : "")}", ct);
    }

    public Task<MembershipDto> CreateMembershipAsync(Guid orgId, CreateMembershipRequest request, CancellationToken ct = default) => Send<MembershipDto>(HttpMethod.Post, $"{Org(orgId)}/memberships", request, ct);
    public Task<MembershipDto> UpdateMembershipAsync(Guid orgId, Guid membershipId, UpdateMembershipRequest request, CancellationToken ct = default) => Send<MembershipDto>(HttpMethod.Put, $"{Org(orgId)}/memberships/{membershipId}", request, ct);
    public Task RemoveMembershipAsync(Guid orgId, Guid membershipId, CancellationToken ct = default) => Send(HttpMethod.Delete, $"{Org(orgId)}/memberships/{membershipId}", null, ct);

    public Task<IReadOnlyList<PositionDto>> ListPositionsAsync(Guid orgId, CancellationToken ct = default) => Get<IReadOnlyList<PositionDto>>($"{Org(orgId)}/positions", ct);
    public Task<PositionDto> CreatePositionAsync(Guid orgId, CreatePositionRequest request, CancellationToken ct = default) => Send<PositionDto>(HttpMethod.Post, $"{Org(orgId)}/positions", request, ct);
    public Task<PositionDto> UpdatePositionAsync(Guid orgId, Guid positionId, UpdatePositionRequest request, CancellationToken ct = default) => Send<PositionDto>(HttpMethod.Put, $"{Org(orgId)}/positions/{positionId}", request, ct);

    public Task<IReadOnlyList<TenantSeedDto>> ListSeedsAsync(Guid orgId, CancellationToken ct = default) => Get<IReadOnlyList<TenantSeedDto>>($"{Org(orgId)}/seeds", ct);
    public Task<TenantSeedDto> CreateSeedAsync(Guid orgId, CreateSeedRequest request, CancellationToken ct = default) => Send<TenantSeedDto>(HttpMethod.Post, $"{Org(orgId)}/seeds", request, ct);
    public Task<TenantSeedDto> UpdateSeedAsync(Guid orgId, string seedId, UpdateSeedRequest request, CancellationToken ct = default) => Send<TenantSeedDto>(HttpMethod.Put, $"{Org(orgId)}/seeds/{E(seedId)}", request, ct);
    public Task<IReadOnlyList<TenantLinkDto>> ListLinksAsync(Guid orgId, CancellationToken ct = default) => Get<IReadOnlyList<TenantLinkDto>>($"{Org(orgId)}/links", ct);

    public Task<IReadOnlyList<DeploymentSeedDto>> ListDeploymentSeedsAsync(string deploymentId, CancellationToken ct = default) =>
        Get<IReadOnlyList<DeploymentSeedDto>>($"seeds?deployment={E(deploymentId)}", ct);

    public Task ReportSeedAsync(string seedId, SeedReportRequest report, CancellationToken ct = default) => Send(HttpMethod.Post, $"seeds/{E(seedId)}/reports", report, ct);

    public Task<FeedPage> ReadFeedAsync(Guid orgId, long after = 0, int take = 200, CancellationToken ct = default) =>
        Get<FeedPage>($"{Org(orgId)}/events?after={after}&take={take}", ct);

    private async Task<T> Get<T>(string path, CancellationToken ct)
    {
        using var response = await Http.GetAsync(path, ct).ConfigureAwait(false);
        return await Read<T>(response, ct).ConfigureAwait(false);
    }

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRaw(method, path, body, ct).ConfigureAwait(false);
        return await Read<T>(response, ct).ConfigureAwait(false);
    }

    private async Task Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRaw(method, path, body, ct).ConfigureAwait(false);
        await EnsureSuccess(response, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRaw(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        return await Http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await EnsureSuccess(response, ct).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false)
               ?? throw new OrganizationCallException(response.StatusCode, "The response had no body.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct).ConfigureAwait(false);
            if (problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("detail", out var d)) detail = d.GetString();
        }
        catch (JsonException)
        {
        }
        throw new OrganizationCallException(response.StatusCode, detail);
    }
}
