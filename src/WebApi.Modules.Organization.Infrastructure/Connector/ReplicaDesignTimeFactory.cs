using Microsoft.EntityFrameworkCore.Design;
using soft.square.net.WebApi.Data;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;

/// <summary>
/// Design time for the replica, one migration set per engine (Data/Replica/Migrations/&lt;Provider&gt;): the platform's
/// src/WebApi/scripts/Add-Migration.ps1 -Project &lt;this project&gt; -Context ReplicaDbContext -Name &lt;Name&gt;
/// -MigrationsDir Data/Replica/Migrations -Namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Replica.Migrations
/// </summary>
internal sealed class ReplicaDesignTimeFactory : IDesignTimeDbContextFactory<ReplicaDbContext>
{
    public ReplicaDbContext CreateDbContext(string[] args) =>
        new(TenantDbDesignTime.Options<ReplicaDbContext>(args), TenantDbDesignTime.Tenant);
}
