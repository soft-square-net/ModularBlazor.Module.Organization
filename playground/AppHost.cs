// Organization playground: two deployments of the platform with this repo's module in both, for local development
// and the end-to-end demo (playground/demo.ps1). Two loops:
//
//   dev (default, launch profile "dev"): the WebApis load the module, and its client parts, straight from their build
//     output (Modules:Local and ClientModules:Local, Development only). Change code, rebuild, restart the "central"
//     and "branch-core" resources in the dashboard: the new build is installed and active, with no packing or uploading.
//   install (launch profile "install"): the platforms start without the module, as in production. Every build zips
//     the module to playground/out/organization.zip and copies its client parts to playground/out/client; upload the
//     zip on each deployment's Host Modules page and activate it, then the two dlls on the Client Modules page (the API
//     client first). This runs the real install path before a release.
//
//   identity (5090)  one issuer for both deployments; registers the connector's service client
//
//   Deployment A, "central"                         Deployment B, "branch"
//   central-data ─► central (5081)                  branch-data ─► branch-core ◄── gateway (5080) ◄── blzwebclient (5070)
//                   Organization:Mode=Central                      Organization connector         │
//                   + its own connector ◄──────────────────────────── seeds, feed, reports ◄─────────┤
//                                       ◄── /api/organization/* (management API) ────────────────────┘
//
// Each deployment has its own PostgreSQL server (tenant store and tenant databases) and its own Platform:DeploymentId;
// both run Organization on their "core" pool. B's gateway sends the management API to the central service and
// everything else, the module's local routes included, to B's own core pool. The central service does not know B's
// tenant "newsdesk": B's users are foreign callers there (platform preview.8), authorized from Organization's own data.
//
// The platform's projects build from a ModularBlazor.Platform checkout next to this repo (Organization.Playground.csproj).
// It uses the platform lab's ports (the client and identity are registered for them), so stop the lab, and any other
// module's playground, first. Data stays in the Docker volumes "organization-playground-central" and
// "organization-playground-branch" and in playground/.data (the module catalogs).

using System.Reflection;
using Aspire.Hosting.Yarp;
using Aspire.Hosting.Yarp.Transforms;

var builder = DistributedApplication.CreateBuilder(args);

var mode = (builder.Configuration["Playground:Mode"] ?? "dev").Trim().ToLowerInvariant();
if (mode is not ("dev" or "install"))
    throw new InvalidOperationException($"Playground:Mode '{mode}' is not supported: use dev or install.");

// Fixed by the platform: the identity service registers the client's callbacks on 5070 and the client calls its
// gateway on 5080. The central service gets 5081.
const int IdentityPort = 5090;
const int GatewayPort = 5080;
const int CentralPort = 5081;
const int ClientPort = 5070;
const string Issuer = "http://localhost:5090/";
const string ClientOrigin = "http://localhost:5070";
const string CentralUrl = "http://localhost:5081/api/organization/";

// The connector's service client (client credentials, scope "api"). The identity service registers one configurable
// service client, Identity:ElsaClient; the playground runs no Elsa, so it registers the connector in that slot. In a
// real deployment register it on the identity admin's Applications page and keep the secret in a secret store.
// DEVELOPMENT ONLY: this secret is a playground value, never reuse it.
const string ConnectorClientId = "organization-connector";
const string ConnectorSecretDefault = "dev-organization-connector-secret";
var connectorSecret = string.IsNullOrEmpty(builder.Configuration["Parameters:organization-connector-secret"])
    ? builder.AddParameter("organization-connector-secret", ConnectorSecretDefault, secret: true)
    : builder.AddParameter("organization-connector-secret", secret: true);

// ---------------------------------------------------------------- one PostgreSQL server per deployment

// Each deployment's ConnectionStrings:tenant-data is its own server: the tenancy module creates new tenants there
// (and seeds the dev tenants acme and globex on both, in separate servers). The identity database lives on A's.
var centralData = builder.AddPostgres("central-data")
    .WithDataVolume("organization-playground-central")
    .WithPgAdmin();
var centralStore = centralData.AddDatabase("central-store", databaseName: "CentralTenants");
var identityDb = centralData.AddDatabase("identity-db", databaseName: "identity");

var branchData = builder.AddPostgres("branch-data")
    .WithDataVolume("organization-playground-branch")
    .WithPgAdmin();
var branchStore = branchData.AddDatabase("branch-store", databaseName: "BranchTenants");

// ---------------------------------------------------------------- identity (shared)

// Development settings of the identity service: dev accounts (admin@modular.local, ...) and the dev client secrets.
// The playground adds the tenant the demo's seed creates on B ("newsdesk") and a manager account for it. The platform
// admin gets no membership there: the identity service signs a user with one membership in to that tenant, and from a
// tenant the central service does not know, the platform-admin role counts for nothing there.
var identity = builder.AddProject<Projects.WebApi_Identity_Server>("identity", launchProfileName: null)
    .WithHttpEndpoint(port: IdentityPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Identity__Issuer", Issuer)
    .WithEnvironment("Identity__ElsaClient__ClientId", ConnectorClientId)
    .WithEnvironment("Identity__ElsaClient__ClientSecret", connectorSecret)
    .WithEnvironment("Identity__Seed__Tenants__2__Id", "newsdesk")
    .WithEnvironment("Identity__Seed__Tenants__2__DisplayName", "Newsdesk")
    .WithEnvironment("Identity__Seed__Users__3__Email", "manager@modular.local")
    .WithEnvironment("Identity__Seed__Users__3__Password", "Dev-Manager-2026!")
    .WithEnvironment("Identity__Seed__Users__3__DisplayName", "Mona Manager")
    .WithEnvironment("Identity__Seed__Users__3__Memberships__0__Tenant", "newsdesk")
    .WithEnvironment("Identity__Seed__Users__3__Memberships__0__Role", "member")
    .WithReference(identityDb, connectionName: "identity").WaitFor(identityDb)
    .WithHttpHealthCheck("/health");

var data = Metadata("PlaygroundData");
var suffix = mode == "dev" ? "" : "-install";

// ---------------------------------------------------------------- deployment A: the central service

// One replica of the core pool, reached directly on 5081 (a real central deployment has its own gateway; one route,
// /api/organization/*, is all the other deployments need). Its connector syncs its own tenants from itself.
var central = AddDeployment("central", "central", CentralPort, centralData, centralStore)
    .WithEnvironment("Modules__Settings__organization__Organization__Mode", "Central")
    .WithEnvironment("Modules__Settings__organization__Organization__TrustedIssuers", Issuer)
    .WithEnvironment("Modules__Settings__organization__Organization__ServiceClients", ConnectorClientId);

// ---------------------------------------------------------------- deployment B: a connector

var branchCore = AddDeployment("branch-core", "branch", port: null, branchData, branchStore)
    .WaitFor(central);

// B's edge: the management API goes to the central service, the module's local routes (and everything else) to B.
var gateway = builder.AddYarp("gateway")
    .WithHostPort(GatewayPort)
    .WithConfiguration(yarp =>
    {
        var centralCluster = yarp.AddCluster("central", [central.GetEndpoint("http")]);
        var coreCluster = yarp.AddCluster("core", [branchCore.GetEndpoint("http")]);
        yarp.AddRoute("/api/organization/local/{**catch-all}", coreCluster);
        yarp.AddRoute("/api/organization/{**catch-all}", centralCluster);
        yarp.AddRoute("/{**catch-all}", coreCluster);
        // Lab only, as in the platform's Hybird lab: reach B's pool directly (/pools/core/scalar, ...).
        yarp.AddRoute("/pools/core/{**catch-all}", coreCluster)
            .WithTransformPathRemovePrefix("/pools/core")
            .WithTransformRequestHeader("X-Forwarded-Prefix", "/pools/core", append: false);
    })
    .WithReference(central).WaitFor(central)
    .WithReference(branchCore).WaitFor(branchCore);

builder.AddProject<Projects.ModularBlazor_BlzWebClient>("blzwebclient", launchProfileName: null)
    .WithHttpEndpoint(port: ClientPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WaitFor(identity)
    .WaitFor(gateway);

if (mode == "install")
    Console.WriteLine($"Install loop: upload {Metadata("ModulePackage")} on {ClientOrigin}/host-modules and activate it, then the client parts (out/client, API client first) on {ClientOrigin}/client-modules; for the central deployment use http://localhost:{CentralPort}/scalar.");

builder.Build().Run();

// One deployment's core pool (a single replica): its own tenant store and database server, its deployment id, its
// module catalog in playground/.data/<deployment>, and the Organization connector pointed at the central service.
IResourceBuilder<ProjectResource> AddDeployment(string name, string deploymentId, int? port,
    IResourceBuilder<PostgresServerResource> server, IResourceBuilder<PostgresDatabaseResource> store)
{
    const string Org = "Modules__Settings__organization__Organization__";
    var catalog = Path.Combine(data, deploymentId, "modules" + suffix);
    var clientCatalog = Path.Combine(data, deploymentId, "client-modules" + suffix);
    var webApi = builder.AddProject<Projects.WebApi>(name, launchProfileName: null)
        .WithHttpEndpoint(port: port)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        // Quieter SQL; live subscriptions that are refused show in the console (the hub logs them at Debug).
        .WithEnvironment("Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command", "Warning")
        .WithEnvironment("Logging__LogLevel__soft.square.net.WebApi.Live", "Debug")
        .WithEnvironment("Modules__Pool", "core")
        .WithEnvironment("Modules__Role", "Api")
        .WithEnvironment("Modules__Path", catalog)
        .WithEnvironment("Modules__Settings__client-modules__ClientModules__Path", clientCatalog)
        .WithReference(store, connectionName: "TenantStore").WaitFor(store)
        .WithEnvironment("DatabaseServers__TenantStore__Provider", "PostgreSql")
        .WithReference(server, connectionName: "tenant-data")
        .WithEnvironment("DatabaseServers__tenant-data__Provider", "PostgreSql")
        .WithEnvironment("Auth__Authority", Issuer)
        .WithEnvironment("Auth__Audience", "modular-webapi")
        .WithEnvironment("Cors__AllowedOrigins__0", ClientOrigin)
        // The host's seed reconciler serves this deployment's seeds only (and does nothing without the id); a short
        // interval for the playground (default one minute).
        .WithEnvironment("Platform__DeploymentId", deploymentId)
        .WithEnvironment("Tenancy__Seeds__Interval", "00:00:15")
        // The Organization connector: the central service's module route, the service client and its secret (a host
        // override, so it is never written to the module catalog). Short poll for the playground (default 30 s).
        .WithEnvironment(Org + "CentralUrl", CentralUrl)
        .WithEnvironment(Org + "TokenEndpoint", Issuer + "connect/token")
        .WithEnvironment(Org + "ClientId", ConnectorClientId)
        .WithEnvironment(Org + "ClientScope", "api")
        .WithEnvironment(Org + "ClientSecret", connectorSecret)
        .WithEnvironment(Org + "PollInterval", "00:00:05")
        .WaitFor(identity)
        .WithHttpHealthCheck("/health");
    if (mode == "dev")
    {
        // The server part from its build output (Modules:Local), and the client parts too (the client-modules
        // module's ClientModules:Local, a host override): every start installs what changed, the API client first.
        const string ClientLocal = "Modules__Settings__client-modules__ClientModules__Local__";
        webApi.WithEnvironment("Modules__Local__organization", Metadata("ModuleOutput"))
            .WithEnvironment(ClientLocal + "WebApi.Modules.Organization.Client", Metadata("ApiClientOutput"))
            .WithEnvironment(ClientLocal + "Module.Organization", Metadata("UiOutput"));
    }
    return webApi;
}

// Paths the project file computes (AssemblyMetadata in Organization.Playground.csproj).
static string Metadata(string key) =>
    Assembly.GetEntryAssembly()!.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;
