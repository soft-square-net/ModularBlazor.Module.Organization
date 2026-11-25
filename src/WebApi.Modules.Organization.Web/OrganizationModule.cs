using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Contracts;
using soft.square.net.WebApi.Modules.Organization.Web.Configurations;

namespace soft.square.net.WebApi.Modules.Organization.Web;

/// <summary>
/// The module: its manifest (id, name, version, route prefix, default settings, permissions) and its services.
/// This is the composition root (the Ardalis template's Program.cs): the runtime creates it, reads the manifest and
/// calls <see cref="ConfigureServices"/> on the module's own container; the endpoints are in
/// <see cref="OrganizationRoutes"/>, mapped under <c>/api/organization</c>. Tenant data, schema "organization".
/// </summary>
public sealed class OrganizationModule : IRuntimeModule
{
    public ModuleManifest Manifest { get; } = new("organization", "Organization", "0.1.0", "organization")
    {
        Description = "Organization: notes per tenant (the template's example; replace with the module's own features).",
        DefaultSettings = new Dictionary<string, string?> { ["Organization:MaxPageSize"] = "100" },
        Permissions = OrganizationPermissions.All,
        // Integration event patterns this module handles: the host binds the module's queue to them at install.
        Subscriptions = OrganizationEvents.Subscriptions,
        // Live topic kinds (organization:note:{id}) and the permission a browser needs to subscribe to one.
        LiveTopics = OrganizationEvents.LiveTopicKinds
    };

    public void ConfigureServices(IServiceCollection services, IConfiguration settings) => services.AddServiceConfigs(settings);
}
