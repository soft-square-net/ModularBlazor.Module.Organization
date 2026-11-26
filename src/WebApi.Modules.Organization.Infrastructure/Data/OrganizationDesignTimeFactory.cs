using Microsoft.EntityFrameworkCore.Design;
using soft.square.net.WebApi.Data;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;

/// <summary>
/// Design time, one migration set per engine (Data/Migrations/&lt;Provider&gt;): add migrations with the platform's
/// src/WebApi/scripts/Add-Migration.ps1 -Project &lt;this project&gt; -Name &lt;Name&gt; -MigrationsDir Data/Migrations
/// -Namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Migrations (see the README).
/// </summary>
internal sealed class OrganizationDesignTimeFactory : IDesignTimeDbContextFactory<OrganizationDbContext>
{
    public OrganizationDbContext CreateDbContext(string[] args) =>
        new(TenantDbDesignTime.Options<OrganizationDbContext>(args), new CentralDatabase("design-time", "design_time"));
}
