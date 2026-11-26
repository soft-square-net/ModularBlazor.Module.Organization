using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Modules.Organization.Web.Configurations;

namespace soft.square.net.WebApi.Modules.Organization.Web;

/// <summary>
/// The Organization module: one bundle, two modes (<c>Organization:Mode</c>).
/// <list type="bullet">
/// <item><b>Central</b> (one deployment): the platform-level <c>organization</c> database with every customer's orgs,
/// unit types, unit tree, persons, memberships, positions, tenant seeds, tenant links and change feed, and the
/// management API under <c>/api/organization</c>. Its endpoints run with or without a tenant (<c>TenantOptional</c>)
/// and authorize from the service's own data, not from tenant grants.</item>
/// <item><b>Connector</b> (every deployment, the default; Central mode runs it too): pulls the deployment's seeds and
/// the orgs' feeds from the central service (<c>Organization:CentralUrl</c>) into a replica in each tenant's database,
/// serves the seeds to the host (<c>ITenantSeedSource</c>), writes the positions' standing grants, republishes the
/// org.* events locally and serves the tenant's units under <c>/api/organization/local</c>. The central endpoints
/// answer 404 here.</item>
/// </list>
/// <para>Load it on the core pool too: the live hub runs there, and it asks this module for the ancestors of a
/// <c>unit:{id}</c> topic (without the module on the hub's replica only the exact unit's grant counts).</para>
/// </summary>
public sealed class OrganizationModule : IRuntimeModule
{
    /// <summary>
    /// The bundle's one version (<c>Version</c> in Directory.Build.props, or <c>-p:Version=</c> at release): the
    /// manifest's, the client parts' and the packages' alike. The build metadata (<c>+sha</c>) is dropped.
    /// </summary>
    public static string Version { get; } =
        typeof(OrganizationModule).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public ModuleManifest Manifest { get; } = new("organization", "Organization", Version, "organization")
    {
        Description = "Organization: customers, the unit tree (Org > Sector > Department > Section), members, positions, tenant seeds and links. Central or Connector mode.",
        DefaultSettings = new Dictionary<string, string?>
        {
            ["Organization:Mode"] = "Connector",
            ["Organization:TrustedIssuers"] = "",
            ["Organization:ServiceClients"] = "",
            ["Organization:DatabaseServer"] = "TenantStore",
            ["Organization:DatabaseName"] = "organization",
            ["Organization:MaxFeedPage"] = "500",
            ["Organization:CentralUrl"] = "",
            ["Organization:DeploymentId"] = "",
            ["Organization:TokenEndpoint"] = "",
            ["Organization:ClientId"] = "",
            ["Organization:ClientScope"] = "",
            ["Organization:PollInterval"] = "00:00:30",
            ["Organization:FeedOverlap"] = "00:01:00",
            ["Organization:FeedPageSize"] = "200"
        },
        Permissions = OrganizationPermissions.All,
        // unit:{id} topics: the platform's unit topics, provided by this module (the only one that declares the kind).
        // Subscribers need org.read on the unit or one of its ancestors (UnitLiveScopes gives the ancestors).
        // organization:org:{orgId:N} (OrgLiveTopic): a nudge after any change of the org; org.read on org:{id} or on
        // the org's root unit.
        LiveTopics =
        [
            new LiveTopic(LiveTopics.UnitKind, OrganizationPermissions.Read),
            new LiveTopic(OrgLiveTopic.Kind, OrganizationPermissions.Read)
        ]
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddServiceConfigs(settings);
}
