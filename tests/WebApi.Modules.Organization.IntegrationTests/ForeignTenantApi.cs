using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Carter;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;
using soft.square.net.WebApi.Modules.Organization.UseCases.Persons;
using soft.square.net.WebApi.Modules.Organization.UseCases.Positions;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;
using soft.square.net.WebApi.Modules.Organization.UseCases.Units;
using soft.square.net.WebApi.Modules.Organization.Web;
using soft.square.net.WebApi.Modules.Organization.Web.Configurations;
using soft.square.net.WebApi.Modules.Organization.Web.Extensions;
using Db = soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data.OrganizationDatabase;

namespace soft.square.net.WebApi.Modules.Organization.IntegrationTests;

/// <summary>
/// The central API over HTTP for callers from tenants the central deployment does not know (platform preview.8): the
/// module's real routes, filters, <see cref="HttpCaller"/>, mediator and use cases on SQLite. The platform host is not a
/// package, so its tenant resolution on a tenant-optional endpoint is stood in for: this deployment knows the tenant
/// "central-tenant" only; any other tenant the token or the X-Tenant header names is foreign, resolved by "claim" when
/// the token is for it and by "header" when the request only names it.
///
/// <para>The data: org "news" with a seed for the branch tenant "newsdesk" on its department (admin: seed-admin), and a
/// manager of the Section below it; org "other", with no seed for that tenant, which the manager belongs to too.</para>
/// </summary>
public sealed class ForeignTenantApi : IAsyncLifetime
{
    private const string Known = "central-tenant";
    private const string Branch = "newsdesk";
    private const string ServiceClient = "organization-connector";

    private readonly Db _db = new();
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    private OrgDto _news = null!;
    private OrgDto _other = null!;
    private OrgUnitDto _sector = null!;
    private OrgUnitDto _department = null!;
    private OrgUnitDto _section = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await SeedDataAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var services = builder.Services;
        _db.AddCentralServices(services);
        services.AddSingleton(Options.Create(new OrganizationOptions { Mode = OrganizationMode.Central, ServiceClients = ServiceClient, MaxFeedPage = 50 }));
        services.AddHttpContextAccessor();
        services.AddScoped<ICaller, HttpCaller>();
        services.AddScoped<RequestTenant>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<RequestTenant>());
        services.AddMediatorSourceGen();
        services.AddAuthentication(TestAuthentication.Name).AddScheme<AuthenticationSchemeOptions, TestAuthentication>(TestAuthentication.Name, null);
        services.AddAuthorization();

        _app = builder.Build();
        _app.UseRouting();
        _app.UseAuthentication();
        _app.Use((context, next) =>
        {
            context.RequestServices.GetRequiredService<RequestTenant>().Resolve(context, Known);
            return next(context);
        });
        _app.UseAuthorization();
        new OrganizationRoutes().AddRoutes(_app.MapGroup("/api/organization"));
        await _app.StartAsync(Ct);

        var address = _app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        _http = new HttpClient { BaseAddress = new Uri(address + "/api/organization/") };
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _db.Dispose();
    }

    // --- The routes ---

    [Fact]
    public void EveryCentralEndpointIsTenantOptionalAndTheLocalOnesNeedATenant()
    {
        var endpoints = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText!, Optional: e.Metadata.GetMetadata<TenantOptionalMetadata>() is not null))
            .ToList();

        var local = endpoints.Where(e => e.Route.StartsWith("/api/organization/local/", StringComparison.Ordinal)).ToList();
        local.ShouldNotBeEmpty();
        local.ShouldAllBe(e => !e.Optional);
        var central = endpoints.Except(local).ToList();
        central.ShouldNotBeEmpty();
        central.ShouldAllBe(e => e.Optional);
    }

    // --- An allowed member ---

    [Fact]
    public async Task AForeignMemberSeesTheOrgOfTheirTenantAndHasTheirPositionsRights()
    {
        var manager = Foreign("manager", Branch);

        var orgs = await ReadAsync<List<OrgDto>>(await GetAsync("orgs", manager));
        orgs.Select(o => o.Id).ShouldBe([_news.Id]);
        (await GetAsync($"orgs/{_news.Id}", manager)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var units = await ReadAsync<List<OrgUnitDto>>(await GetAsync($"orgs/{_news.Id}/units", manager));
        units.Select(u => u.Id).ShouldContain(_section.Id);

        // The manager position gives org.units.manage on the Section, not above it.
        var rename = new UpdateUnitRequest(_section.Version, "Local desk", "المكتب المحلي", null, null, ["c1"]);
        (await PutAsync($"orgs/{_news.Id}/units/{_section.Id}", rename, manager)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var above = new UpdateUnitRequest(_department.Version, "Desk", "المكتب");
        (await PutAsync($"orgs/{_news.Id}/units/{_department.Id}", above, manager)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ATenantIdIsComparedWithoutCase()
    {
        var orgs = await ReadAsync<List<OrgDto>>(await GetAsync("orgs", Foreign("manager", "NewsDesk")));

        orgs.Select(o => o.Id).ShouldBe([_news.Id]);
    }

    [Fact]
    public async Task AnOrgWithoutASeedForTheForeignTenantStaysClosedToItsOwnMember()
    {
        var manager = Foreign("manager", Branch);

        (await GetAsync($"orgs/{_other.Id}", manager)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"orgs/{_other.Id}/persons", manager)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Nor does a tenant no org has a seed for open anything.
        var elsewhere = Foreign("manager", "someone-else");
        (await ReadAsync<List<OrgDto>>(await GetAsync("orgs", elsewhere))).ShouldBeEmpty();
        (await GetAsync($"orgs/{_news.Id}", elsewhere)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- The seed's admin ---

    [Fact]
    public async Task TheSeedsAdminManagesTheSeedsSubtreeOnly()
    {
        var admin = Foreign("seed-admin", Branch);

        var below = new CreateUnitRequest(_department.Id, "section", "Sport", "الرياضة", null, null, ["c2"]);
        (await PostAsync($"orgs/{_news.Id}/units", below, admin)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await GetAsync($"orgs/{_news.Id}/seeds", admin)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var above = new CreateUnitRequest(_sector.Id, "department", "Other", "آخر");
        (await PostAsync($"orgs/{_news.Id}/units", above, admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"orgs/{_news.Id}/events", admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AForeignCustomerAdminAdministersTheOrgOfTheirTenantOnly()
    {
        var admin = Foreign("customer-admin", Branch);

        (await GetAsync($"orgs/{_news.Id}/events", admin)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync($"orgs/{_other.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- A non-member, whatever the token claims ---

    [Fact]
    public async Task AForeignNonMemberGetsNothing()
    {
        var stranger = Foreign("stranger", Branch);

        (await ReadAsync<List<OrgDto>>(await GetAsync("orgs", stranger))).ShouldBeEmpty();
        foreach (var path in new[] { "", "/units", "/persons", "/memberships", "/positions", "/unit-types", "/seeds", "/events", "/links" })
            (await GetAsync($"orgs/{_news.Id}{path}", stranger)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
    }

    [Fact]
    public async Task AForeignPlatformAdminRoleCountsForNothing()
    {
        var admin = Foreign("branch-platform-admin", Branch, PlatformRoles.Admin);

        (await ReadAsync<List<OrgDto>>(await GetAsync("orgs", admin))).ShouldBeEmpty();
        (await GetAsync($"orgs/{_news.Id}", admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await PostAsync("orgs", new CreateOrgRequest("mine", "Mine", "لي"), admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync("seeds?deployment=branch", admin)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AForeignCallerIsNeverAServiceClient()
    {
        var client = Foreign(ServiceClient, Branch);

        (await GetAsync("seeds?deployment=branch", client)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync($"orgs/{_news.Id}/events", client)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // --- A tenant only the header names ---

    [Theory]
    [InlineData("manager", null)]
    [InlineData("seed-admin", null)]
    [InlineData("customer-admin", null)]
    [InlineData("platform-admin", PlatformRoles.Admin)]
    [InlineData(ServiceClient, null)]
    public async Task AHeaderOnlyForeignTenantGetsNothing(string subject, string? role)
    {
        var caller = new Caller(subject, null, Branch, role);

        foreach (var path in new[] { "orgs", $"orgs/{_news.Id}", $"orgs/{_news.Id}/units", "seeds?deployment=branch" })
            (await GetAsync(path, caller)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        (await PostAsync("orgs", new CreateOrgRequest("mine", "Mine", "لي"), caller)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnAnonymousHeaderOnlyForeignTenantIsUnauthorized() =>
        (await GetAsync("orgs", new Caller(null, null, Branch, null))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("header")]
    [InlineData("host")]
    [InlineData(null)]
    public void AForeignTenantNotResolvedByTheClaimGivesNoIdentity(string? resolvedBy)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(PlatformClaims.Subject, "manager"), new Claim("iss", Db.Issuer),
                new Claim(PlatformClaims.Role, PlatformRoles.Admin)], "test"))
        };
        var tenant = new RequestTenant { ForeignTenantId = Branch, ResolvedBy = resolvedBy };

        var caller = new HttpCaller(new HttpContextAccessor { HttpContext = context }, Options.Create(new OrganizationOptions()), tenant);

        (caller.Subject, caller.Issuer, caller.IsPlatformAdmin, caller.IsServiceClient, caller.ForeignTenantId).ShouldBe((null, null, false, false, null));
    }

    // --- Known tenants, and none: unchanged ---

    [Theory]
    [InlineData(Known)]
    [InlineData(null)]
    public async Task WithAKnownTenantOrNoneNothingChanges(string? tenant)
    {
        var platformAdmin = new Caller("platform-admin", tenant, null, PlatformRoles.Admin);
        var orgs = await ReadAsync<List<OrgDto>>(await GetAsync("orgs", platformAdmin));
        orgs.Select(o => o.Id).ShouldBe([_news.Id, _other.Id], ignoreOrder: true);

        // A member needs no seed for their tenant: both orgs they belong to.
        var manager = new Caller("manager", tenant, null, null);
        (await ReadAsync<List<OrgDto>>(await GetAsync("orgs", manager))).Count.ShouldBe(2);
        (await GetAsync($"orgs/{_other.Id}", manager)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var connector = new Caller(ServiceClient, tenant, null, null);
        (await GetAsync("seeds?deployment=branch", connector)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Requests ---

    /// <summary>A signed-in (or anonymous) caller: the token's subject, tenant and role, and the X-Tenant header.</summary>
    private sealed record Caller(string? Subject, string? TokenTenant, string? Header, string? Role);

    /// <summary>A user of a branch deployment: a token for its tenant, which the client sends in X-Tenant too.</summary>
    private static Caller Foreign(string subject, string tenant, string? role = null) => new(subject, tenant, tenant, role);

    private Task<HttpResponseMessage> GetAsync(string path, Caller caller) => SendAsync(HttpMethod.Get, path, null, caller);

    private Task<HttpResponseMessage> PostAsync<T>(string path, T body, Caller caller) => SendAsync(HttpMethod.Post, path, JsonContent.Create(body), caller);

    private Task<HttpResponseMessage> PutAsync<T>(string path, T body, Caller caller) => SendAsync(HttpMethod.Put, path, JsonContent.Create(body), caller);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? body, Caller caller)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body };
        if (caller.Subject is not null) request.Headers.Add(TestAuthentication.Subject, caller.Subject);
        if (caller.TokenTenant is not null) request.Headers.Add(TestAuthentication.Tenant, caller.TokenTenant);
        if (caller.Role is not null) request.Headers.Add(TestAuthentication.Role, caller.Role);
        if (caller.Header is not null) request.Headers.Add(TenancyNames.Header, caller.Header);
        return await _http.SendAsync(request, Ct);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    // --- Data ---

    private async Task SeedDataAsync()
    {
        _news = await CreateOrgAsync("news");
        _other = await CreateOrgAsync("other");
        _sector = await AddUnitAsync(_news.Id, _news.RootUnitId, "sector", "News");
        _department = await AddUnitAsync(_news.Id, _sector.Id, "department", "Newsdesk");
        _section = await AddUnitAsync(_news.Id, _department.Id, "section", "Local", "c1");

        var seedAdmin = await AddPersonAsync(_news.Id, "seed-admin");
        var manager = await AddPersonAsync(_news.Id, "manager");
        await AddPersonAsync(_other.Id, "manager");
        var position = (await RunAsync<ListPositionsHandler, IReadOnlyList<PositionDto>>(h => h.Handle(new ListPositionsQuery(_news.Id), Ct)))
            .Single(p => p.Key == PositionDefinition.ManagerKey);
        await RunAsync<CreateMembershipHandler, MembershipDto>(h =>
            h.Handle(new CreateMembershipCommand(_news.Id, new CreateMembershipRequest(manager.Id, _section.Id, position.Id, Primary: true)), Ct));
        await RunAsync<CreateSeedHandler, TenantSeedDto>(h => h.Handle(new CreateSeedCommand(_news.Id,
            new CreateSeedRequest(_department.Id, "branch", Branch, "Newsdesk", [], "tenant-data", "tenant-newsdesk", null, [seedAdmin.Id])), Ct));

        // The seed bound the department: read it again for its version.
        _department = await RunAsync<GetUnitHandler, OrgUnitDto>(h => h.Handle(new GetUnitQuery(_news.Id, _department.Id), Ct));
    }

    private async Task<T> RunAsync<THandler, T>(Func<THandler, ValueTask<Result<T>>> call) where THandler : class
    {
        var result = await _db.Run(Db.PlatformAdmin, call);
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private Task<OrgDto> CreateOrgAsync(string key) => RunAsync<CreateOrgHandler, OrgDto>(h =>
        h.Handle(new CreateOrgCommand(new CreateOrgRequest(key, key, key, [new(Db.Issuer, "customer-admin")])), Ct));

    private Task<OrgUnitDto> AddUnitAsync(Guid orgId, Guid parentId, string type, string name, params string[] categories) =>
        RunAsync<CreateUnitHandler, OrgUnitDto>(h => h.Handle(new CreateUnitCommand(orgId, new CreateUnitRequest(parentId, type, name, name, null, null, categories)), Ct));

    private Task<PersonDto> AddPersonAsync(Guid orgId, string subject) =>
        RunAsync<CreatePersonHandler, PersonDto>(h => h.Handle(new CreatePersonCommand(orgId, new CreatePersonRequest(Db.Issuer, subject, subject)), Ct));

    // --- The host's parts ---

    /// <summary>The request's tenant as the platform's middleware leaves it on a tenant-optional endpoint.</summary>
    private sealed class RequestTenant : ITenantContext
    {
        public TenantInfo? Tenant { get; private set; }
        public string? ResolvedBy { get; set; }
        public string? ForeignTenantId { get; set; }
        public bool IsForeign => ForeignTenantId is not null;

        public void Resolve(HttpContext context, string known)
        {
            var header = context.Request.Headers[TenancyNames.Header].ToString();
            var claim = context.User.FindFirst(TenancyNames.Claim)?.Value;
            var named = header.Length > 0 ? header : claim;
            if (named is null) return;
            var resolvedBy = claim is not null && string.Equals(claim, named, StringComparison.OrdinalIgnoreCase) ? "claim" : "header";
            if (string.Equals(named, known, StringComparison.OrdinalIgnoreCase))
                Tenant = new TenantInfo(known, known, TenantStatus.Active, [], "server", "database");
            else
                ForeignTenantId = named;
            ResolvedBy = resolvedBy;
        }
    }

    /// <summary>Stands in for the host's JWT bearer: the token's claims come in test headers.</summary>
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "test";
        public const string Subject = "X-Test-Sub";
        public const string Tenant = "X-Test-Tenant";
        public const string Role = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[Subject].ToString() is not { Length: > 0 } subject) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(PlatformClaims.Subject, subject), new("iss", Db.Issuer) };
            if (Request.Headers[Tenant].ToString() is { Length: > 0 } tenant) claims.Add(new(TenancyNames.Claim, tenant));
            if (Request.Headers[Role].ToString() is { Length: > 0 } role) claims.Add(new(PlatformClaims.Role, role));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Name)));
        }
    }
}
