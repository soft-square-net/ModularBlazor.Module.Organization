using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Modules.Organization.Web.Configurations;

namespace soft.square.net.WebApi.Modules.Organization.Web;

/// <summary>
/// The Organization module: one bundle, two modes (<c>Organization:Mode</c>).
/// <list type="bullet">
/// <item><b>Central</b> (one deployment): the platform-level <c>organization</c> database with every customer's orgs,
/// unit types, unit tree, persons, memberships, positions, tenant seeds, tenant links and change feed, and the
/// management API under <c>/api/organization</c>. Its endpoints run with or without a tenant (<c>TenantOptional</c>)
/// and authorize from the service's own data, not from tenant grants.</item>
/// <item><b>Connector</b> (every deployment, the default): pulls seeds and feeds from the central service into
/// per-tenant replicas (P1-2). In this version it registers nothing, and the central endpoints answer 404.</item>
/// </list>
/// </summary>
public sealed class OrganizationModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("organization", "Organization", "0.1.0", "organization")
    {
        Description = "Organization: customers, the unit tree (Org > Sector > Department > Section), members, positions, tenant seeds and links. Central or Connector mode.",
        DefaultSettings = new Dictionary<string, string?>
        {
            ["Organization:Mode"] = "Connector",
            ["Organization:TrustedIssuers"] = "",
            ["Organization:ServiceClients"] = "",
            ["Organization:DatabaseServer"] = "TenantStore",
            ["Organization:DatabaseName"] = "organization",
            ["Organization:MaxFeedPage"] = "500"
        },
        Permissions = OrganizationPermissions.All
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddServiceConfigs(settings);
}
