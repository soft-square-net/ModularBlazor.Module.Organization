using System.Reflection;
using Microsoft.EntityFrameworkCore;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using OrgEntity = soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate.Org;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;
using TenantSeed = soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate.TenantSeed;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;

/// <summary>
/// The central Organization data: one platform-level database (<c>Organization:DatabaseName</c>, default
/// <c>organization</c>, on the server <c>Organization:DatabaseServer</c>), not a tenant's, because it spans customers
/// and tenants. Every row carries its org id instead of a tenant id; nothing here is <c>ITenantOwned</c>.
/// <para>It reuses the platform's <see cref="TenantDbContext"/> for what does not depend on the tenant: the module
/// schema "organization" (table prefix on MySQL and Oracle) with its own migrations history, one migration set per
/// engine, and domain events in SaveChanges. Its "tenant" is the fixed <see cref="CentralDatabase"/>, which only names
/// the server and the database.</para>
/// </summary>
public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options, CentralDatabase central) : TenantDbContext(options, central)
{
    public const string SchemaName = "organization";

    protected override string Schema => SchemaName;

    public DbSet<OrgEntity> Orgs => Set<OrgEntity>();
    public DbSet<UnitType> UnitTypes => Set<UnitType>();
    public DbSet<OrgUnit> Units => Set<OrgUnit>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<PositionDefinition> Positions => Set<PositionDefinition>();
    public DbSet<TenantSeed> Seeds => Set<TenantSeed>();
    public DbSet<TenantLink> Links => Set<TenantLink>();
    public DbSet<FeedEntry> Feed => Set<FeedEntry>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
}

/// <summary>
/// Where the central database lives, as the platform's data layer wants it: a <see cref="TenantInfo"/> that names a
/// configured database server and a database on it. A host singleton of the module's container; never a real tenant.
/// </summary>
public sealed class CentralDatabase(string databaseServer, string databaseName) : ITenantContext
{
    public const string Id = "organization-central";

    public TenantInfo? Tenant { get; } = new(Id, "Organization (central)", TenantStatus.Active, [], databaseServer, databaseName);

    public string? ResolvedBy => "organization";

    public TenantInfo Info => Tenant!;
}
