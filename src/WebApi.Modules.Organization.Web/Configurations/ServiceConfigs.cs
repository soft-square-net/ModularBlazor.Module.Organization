using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using soft.square.net.WebApi.Modules.Organization.Infrastructure;
using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;
using soft.square.net.WebApi.Modules.Organization.Web.Extensions;

namespace soft.square.net.WebApi.Modules.Organization.Web.Configurations;

public static class ServiceConfigs
{
    /// <summary>
    /// Everything the module registers, in the module's own container (never the host's). Central mode adds the
    /// platform-level database and the services of the management API. Both modes run the connector: the per-tenant
    /// replica, its local read API and live scopes, and, once <c>Organization:CentralUrl</c> is set, the sync with the
    /// central service and the tenant seed source.
    /// </summary>
    public static IServiceCollection AddServiceConfigs(this IServiceCollection services, IConfiguration settings)
    {
        var section = settings.GetSection("Organization");
        services.Configure<OrganizationOptions>(section);
        var options = section.Get<OrganizationOptions>() ?? new OrganizationOptions();

        if (options.Mode == OrganizationMode.Central)
        {
            services.AddCentralStore(options.DatabaseServer, options.DatabaseName);
            services.AddHttpContextAccessor();
            services.AddScoped<ICaller, HttpCaller>();
            services.AddScoped<IOrgAccess, OrgAccess>();
            services.AddScoped<OrgFeed>();
            services.AddScoped<MembershipParts>();
            services.AddScoped<SeedAdmins>();
        }
        services.AddConnector(options);

        services.AddMediatorSourceGen();
        return services;
    }
}
