using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Central mode's data access, in the module's own container: the platform-level <see cref="OrganizationDbContext"/>
    /// on <paramref name="databaseServer"/> / <paramref name="databaseName"/> (not the request's tenant), the default
    /// repositories over it, the <see cref="IOrgStore"/> unit of work, and <see cref="CentralDatabaseMigrator"/>, which
    /// creates the database when missing and applies the migrations when the module loads.
    /// <para>Like <c>AddTenantDbContext</c>, the context runs on the module's own EF Core internal service provider
    /// (<see cref="TenantEfServices"/>, one per engine), so EF's caches unload with the module.</para>
    /// </summary>
    public static IServiceCollection AddCentralStore(this IServiceCollection services, string databaseServer, string databaseName)
    {
        services.AddSingleton(new CentralDatabase(databaseServer, databaseName));
        services.TryAddSingleton<TenantEfServices>();
        services.TryAddScoped<DomainEventDispatcher>();
        services.AddDbContext<OrganizationDbContext>((sp, options) =>
        {
            var central = sp.GetRequiredService<CentralDatabase>().Info;
            var databases = sp.GetRequiredService<TenantDatabases>();
            var server = databases.ServerOf(central);
            server.Adapter.Use(options, databases.ConnectionString(central), central.DatabaseName);
            options.UseInternalServiceProvider(sp.GetRequiredService<TenantEfServices>().For(server.Provider));
        });
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<OrganizationDbContext>());
        services.TryAddScoped(typeof(SharedKernel.IRepository<>), typeof(EfRepository<>));
        services.TryAddScoped(typeof(SharedKernel.IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped<IOrgStore, EfOrgStore>();
        services.AddHostedService<CentralDatabaseMigrator>();
        return services;
    }

    /// <summary>
    /// The connector, in every deployment (Central mode included):
    /// <list type="bullet">
    /// <item>the per-tenant <see cref="ReplicaDbContext"/>, migrated for every tenant, with the integration outbox the
    /// org.* events are republished through (<c>AddIntegrationEvents</c>);</item>
    /// <item><see cref="IUnitDirectory"/> and the <c>unit:</c> live topics' <see cref="ILiveScopeResolver"/>, from the replica;</item>
    /// <item>the typed HTTP <see cref="IOrganizationClient"/> with the connector's service token;</item>
    /// <item>when <c>Organization:CentralUrl</c> is set: the poller (<see cref="OrgConnector"/>), the first sync of new
    /// tenants (<see cref="ReplicaInitializer"/>) and the platform's <see cref="ITenantSeedSource"/>.</item>
    /// </list>
    /// Call it after <see cref="AddCentralStore"/>: the default repositories stay on the central context.
    /// </summary>
    public static IServiceCollection AddConnector(this IServiceCollection services, OrganizationOptions options)
    {
        services.AddTenantDbContext<ReplicaDbContext>();
        services.AddIntegrationEvents<ReplicaDbContext>(o => o.Source = "organization");
        services.AddScoped<IUnitDirectory, ReplicaUnitDirectory>();
        services.AddScoped<ILiveScopeResolver, UnitLiveScopes>();
        services.AddScoped<ReplicaSync>();

        services.AddSingleton<CentralHttp>();
        services.AddSingleton<IOrganizationClient, CentralOrganizationClient>();
        services.AddSingleton<SeedCatalog>();
        services.AddSingleton<IReplicaSyncLock, TenantDatabaseSyncLock>();
        services.AddSingleton<ReplicaSyncRunner>();
        if (!options.ConnectorEnabled) return services;

        services.AddSingleton<OrgConnector>();
        services.AddHostedService(sp => sp.GetRequiredService<OrgConnector>());
        services.AddSingleton<ITenantInitializer, ReplicaInitializer>(); // after the replica's migrations (AddTenantDbContext)
        services.AddScoped<ITenantSeedSource, OrgSeedSource>();
        return services;
    }
}

/// <summary>The unit of work over the central context: one SaveChanges for everything a use case changed.</summary>
public sealed class EfOrgStore(OrganizationDbContext db) : IOrgStore
{
    public void Add<T>(T entity) where T : class, SharedKernel.IAggregateRoot => db.Set<T>().Add(entity);

    public void Remove<T>(T entity) where T : class, SharedKernel.IAggregateRoot => db.Set<T>().Remove(entity);

    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

/// <summary>
/// Creates the central database when the server does not have it, then applies the pending migrations, once at a time
/// across replicas (a lock on the server). Runs in the background when the module loads, and retries until it works,
/// so a database that is not up yet does not fail the module's activation.
/// </summary>
public sealed class CentralDatabaseMigrator(IServiceProvider services, CentralDatabase central, TenantDatabases databases, ILoggerFactory loggers) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private readonly ILogger _log = loggers.CreateLogger("soft.square.net.WebApi.Modules.Organization.Infrastructure");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MigrateAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Could not migrate the central Organization database {Database}; retrying in {Delay}.", central.Info.DatabaseName, RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    public async Task MigrateAsync(CancellationToken ct)
    {
        if (await databases.EnsureDatabaseAsync(central.Info, ct))
            _log.LogInformation("Created the central Organization database {Database}.", central.Info.DatabaseName);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
        await using (await databases.LockAsync(central.Info, "migrate:organization-central", ct))
        {
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
            if (pending.Length == 0) return;
            await db.Database.MigrateAsync(ct);
            _log.LogInformation("Migrated the central Organization database: {Migrations}", string.Join(", ", pending));
        }
    }
}
