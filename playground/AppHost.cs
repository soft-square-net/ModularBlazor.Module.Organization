// Organization playground: a small platform with this repo's module in it, for local development. Two loops:
//
//   dev (default, launch profile "dev"): the WebApi loads the module, and its client parts, straight from their build
//     output (Modules:Local and ClientModules:Local, Development only). Change code, rebuild, restart the "webapi" resource in the dashboard (or F5 again): the new
//     build is installed and active, with no packing or uploading.
//   install (launch profile "install"): the platform starts without the module, as in production. Every build zips
//     the module to playground/out/organization.zip and copies its client parts to playground/out/client; upload the zip on
//     the client's Host Modules page (http://localhost:5070, sign in as the dev platform admin) and activate it, then
//     the two dlls on the Client Modules page (the API client first). This runs the real install path before a release.
//
//   postgres ─► identity (5090)    tenant store + tenant databases + identity database, one server
//            └► webapi (5080)  ◄── blzwebclient (5070)
//
// The platform's projects build from the ModularBlazor.Platform checkout next to this repo (Organization.Playground.csproj).
// It uses the same ports as the platform's Hybird lab (the client and identity are registered for them), so stop the
// lab, and any other module's playground, first. Data stays in the Docker volume "organization-playground-data" and in
// playground/.data (the module catalogs).

using System.Reflection;

var builder = DistributedApplication.CreateBuilder(args);

var mode = (builder.Configuration["Playground:Mode"] ?? "dev").Trim().ToLowerInvariant();
if (mode is not ("dev" or "install"))
    throw new InvalidOperationException($"Playground:Mode '{mode}' is not supported: use dev or install.");

// Fixed by the platform: the identity service registers the client's callbacks on 5070 and the client calls the API on
// 5080 (the Hybird gateway's port; here the one WebApi answers on it directly).
const int IdentityPort = 5090;
const int WebApiPort = 5080;
const int ClientPort = 5070;
const string Issuer = "http://localhost:5090/";
const string ClientOrigin = "http://localhost:5070";

// One PostgreSQL server, named like the lab's ("tenant-data"): the tenant store, the identity database and the tenants'
// databases. In Development the platform seeds the tenants acme and globex on it.
var tenantData = builder.AddPostgres("tenant-data")
    .WithDataVolume("organization-playground-data")
    .WithPgAdmin();
var tenantStore = tenantData.AddDatabase("TenantStore", databaseName: "PlaygroundTenants");
var identityDb = tenantData.AddDatabase("identity-db", databaseName: "identity");

// Development settings of the identity service: dev accounts (admin@modular.local, ...) and the dev client secrets.
var identity = builder.AddProject<Projects.WebApi_Identity_Server>("identity", launchProfileName: null)
    .WithHttpEndpoint(port: IdentityPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Identity__Issuer", Issuer)
    .WithReference(identityDb, connectionName: "identity").WaitFor(identityDb)
    .WithHttpHealthCheck("/health");

var data = Metadata("PlaygroundData");
var catalog = Path.Combine(data, mode == "dev" ? "modules" : "modules-install");
// The client-modules core module keeps its catalog in a folder relative to the WebApi, i.e. inside the platform
// checkout; keep the playground's in playground/.data instead (a host override, Modules:Settings:{id}:{key}).
var clientCatalog = Path.Combine(data, mode == "dev" ? "client-modules" : "client-modules-install");
var webApi = builder.AddProject<Projects.WebApi>("webapi", launchProfileName: null)
    .WithHttpEndpoint(port: WebApiPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("Modules__Pool", "playground")
    .WithEnvironment("Modules__Role", "Api")
    // Every module the platform ships plus this one; the catalog lives in this repo, not in the platform checkout.
    .WithEnvironment("Modules__Path", catalog)
    .WithEnvironment("Modules__Settings__client-modules__ClientModules__Path", clientCatalog)
    .WithReference(tenantStore).WaitFor(tenantStore)
    .WithEnvironment("DatabaseServers__TenantStore__Provider", "PostgreSql")
    .WithReference(tenantData)
    .WithEnvironment("DatabaseServers__tenant-data__Provider", "PostgreSql")
    .WithEnvironment("Auth__Authority", Issuer)
    .WithEnvironment("Auth__Audience", "modular-webapi")
    .WithEnvironment("Cors__AllowedOrigins__0", ClientOrigin)
    // This deployment runs the central Organization service (Organization:Mode, a host override of the module's
    // settings): the platform-level database "organization" on the TenantStore server, created on first start, and the
    // management API under /api/organization, trusting the dev identity service. It authorizes from its own data:
    // sign in as the dev platform admin (admin@modular.local) to create the first org.
    .WithEnvironment("Modules__Settings__organization__Organization__Mode", "Central")
    .WithEnvironment("Modules__Settings__organization__Organization__TrustedIssuers", Issuer)
    .WaitFor(identity)
    .WithHttpHealthCheck("/health");
if (mode == "dev")
{
    // The server part from its build output (Modules:Local), and the client parts too (the client-modules module's
    // ClientModules:Local, set through a host override): every start installs what changed, the API client first.
    // Sign in on http://localhost:5070 as admin@modular.local and open Organization (/organization).
    const string ClientLocal = "Modules__Settings__client-modules__ClientModules__Local__";
    webApi.WithEnvironment("Modules__Local__organization", Metadata("ModuleOutput"))
        .WithEnvironment(ClientLocal + "WebApi.Modules.Organization.Client", Metadata("ApiClientOutput"))
        .WithEnvironment(ClientLocal + "Module.Organization", Metadata("UiOutput"));
}

builder.AddProject<Projects.ModularBlazor_BlzWebClient>("blzwebclient", launchProfileName: null)
    .WithHttpEndpoint(port: ClientPort)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WaitFor(identity)
    .WaitFor(webApi);

if (mode == "install")
    Console.WriteLine($"Install loop: upload {Metadata("ModulePackage")} on {ClientOrigin}/host-modules and activate it, then the client parts (out/client, API client first) on {ClientOrigin}/client-modules.");

builder.Build().Run();

// Paths the project file computes (AssemblyMetadata in Organization.Playground.csproj).
static string Metadata(string key) =>
    Assembly.GetEntryAssembly()!.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;
