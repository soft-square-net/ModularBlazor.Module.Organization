using Carter;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.Organization.Web.Endpoints;

namespace soft.square.net.WebApi.Modules.Organization.Web;

/// <summary>
/// The module's route table, and its only Carter module (mapped under <c>/api/organization</c>).
/// <para>The central management API: every endpoint runs with or without a tenant (<c>TenantOptional</c>: the data is
/// platform-level), needs a signed-in caller, and authorizes in the use case from the service's own data (platform
/// admins, customer admins, unit admins and managers by membership position, service clients). It answers 404 outside
/// Central mode.</para>
/// <para>The local read API (<c>/local/units</c>): the current tenant's units from this deployment's replica, in every
/// mode. It needs a tenant and a signed-in caller of it.</para>
/// <para>One file per resource under <c>Endpoints/</c>, each endpoint named <c>Organization_&lt;Operation&gt;</c>
/// (the OpenAPI operation id, which names the generated client's method).</para>
/// </summary>
public sealed class OrganizationRoutes : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var central = app.MapGroup("")
            .TenantOptional()
            .RequireAuthorization()
            .AddEndpointFilter<CentralOnlyFilter>()
            .AddEndpointFilter<OrganizationProblemsFilter>();

        OrgEndpoints.Map(central.MapGroup("/orgs").WithTags("Orgs"));
        var org = central.MapGroup("/orgs/{orgId:guid}");
        UnitTypeEndpoints.Map(org.MapGroup("/unit-types").WithTags("Unit types"));
        UnitEndpoints.Map(org.MapGroup("/units").WithTags("Units"));
        PersonEndpoints.Map(org.MapGroup("/persons").WithTags("Persons"));
        MembershipEndpoints.Map(org.MapGroup("/memberships").WithTags("Memberships"));
        PositionEndpoints.Map(org.MapGroup("/positions").WithTags("Positions"));
        SeedEndpoints.MapOrg(org.MapGroup("/seeds").WithTags("Tenant seeds"));
        SeedEndpoints.MapDeployment(central.MapGroup("/seeds").WithTags("Tenant seeds"));
        FeedEndpoints.Map(org.WithTags("Feed and links"));

        // This deployment's replica, for the current tenant (a tenant is required): every mode.
        LocalEndpoints.Map(app.MapGroup("/" + LocalRoutes.Units)
            .RequireAuthorization()
            .AddEndpointFilter<OrganizationProblemsFilter>()
            .WithTags("Local units"));
    }
}
