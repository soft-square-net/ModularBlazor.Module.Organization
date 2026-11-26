using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases;
using soft.square.net.WebApi.Modules.Organization.UseCases.Connector;
using PlatformSeed = soft.square.net.WebApi.Contracts.Tenancy.TenantSeed;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;

/// <summary>
/// The deployment's seeds as last pulled from the central service (a module singleton): the connector syncs the
/// tenants they name, and the seed source falls back on them while the central service is down.
/// </summary>
public sealed class SeedCatalog
{
    private readonly Lock _lock = new();
    private IReadOnlyList<DeploymentSeedDto> _seeds = [];
    private Dictionary<string, long> _versions = [];

    /// <summary>The deployment the seeds are for: the configured one, or the one the host's reconciler asked for.</summary>
    public string? DeploymentId { get; private set; }

    public bool Loaded { get; private set; }

    public IReadOnlyList<DeploymentSeedDto> Seeds { get { lock (_lock) return _seeds; } }

    public void UseDeployment(string? deploymentId)
    {
        if (string.IsNullOrWhiteSpace(deploymentId)) return;
        lock (_lock)
        {
            if (DeploymentId == deploymentId) return;
            DeploymentId = deploymentId;
            _seeds = [];
            _versions = [];
            Loaded = false;
        }
    }

    /// <summary>Takes a fresh list; returns the seeds that are new or have a higher version than the last list.</summary>
    public IReadOnlyList<DeploymentSeedDto> Update(IReadOnlyList<DeploymentSeedDto> seeds)
    {
        lock (_lock)
        {
            var changed = SeedMapping.Changed(_versions, seeds);
            _seeds = seeds;
            _versions = seeds.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Max(s => s.Version));
            Loaded = true;
            return changed;
        }
    }

    public DeploymentSeedDto? ForTenant(string tenantId) => Seeds.FirstOrDefault(s => string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The platform's <see cref="ITenantSeedSource"/>: the central service's seeds of this deployment, and the host's
/// reports back to it. Resolved by the host from this module's container on each reconciliation cycle. While the
/// central service is down it serves the last list it had (a version the tenant already has is a no-op).
/// </summary>
public sealed class OrgSeedSource(IOrganizationClient central, SeedCatalog catalog, ILogger<OrgSeedSource> log) : ITenantSeedSource
{
    public async Task<IReadOnlyList<PlatformSeed>> ListAsync(string deploymentId, CancellationToken ct)
    {
        catalog.UseDeployment(deploymentId);
        try
        {
            catalog.Update(await central.ListDeploymentSeedsAsync(deploymentId, ct));
        }
        catch (Exception e) when (e is not OperationCanceledException && catalog.Loaded)
        {
            log.LogWarning(e, "Could not pull the seeds of deployment {Deployment}; serving the last list.", deploymentId);
        }
        return [.. catalog.Seeds.Where(s => s.DeploymentId == deploymentId).Select(SeedMapping.ToPlatform)];
    }

    public Task ReportAsync(string seedId, long version, SeedStatus status, TenantPlacement? actualPlacement, string? reason, CancellationToken ct) =>
        central.ReportSeedAsync(seedId, SeedMapping.ToReport(version, status, actualPlacement, reason), ct);
}

/// <summary>One sync of a tenant's replica at a time, across every replica of every pool that runs the module.</summary>
public interface IReplicaSyncLock
{
    Task<IAsyncDisposable> AcquireAsync(TenantInfo tenant, CancellationToken ct);
}

/// <summary>The lock in the tenant's own database (the platform's advisory lock, as for migrations).</summary>
public sealed class TenantDatabaseSyncLock(TenantDatabases databases) : IReplicaSyncLock
{
    public const string LockName = "organization:replica-sync";

    public async Task<IAsyncDisposable> AcquireAsync(TenantInfo tenant, CancellationToken ct) => await databases.LockAsync(tenant, LockName, ct);
}

/// <summary>
/// Runs one tenant's sync in a tenant scope of the module's container, under <see cref="IReplicaSyncLock"/>, and keeps
/// the error on the replica when it fails. The first successful sync of each tenant after the module starts writes all
/// of its grants again (idempotent), so grant rules that changed in an upgrade reach the tenants without a migration.
/// </summary>
public sealed class ReplicaSyncRunner(IServiceProvider services, IReplicaSyncLock locks, ILogger<ReplicaSyncRunner> log)
{
    private readonly ConcurrentDictionary<string, bool> _grantsRewritten = new(StringComparer.OrdinalIgnoreCase);

    public async Task<SyncOutcome?> SyncAsync(TenantInfo tenant, DeploymentSeedDto seed, CancellationToken ct)
    {
        await using var scope = services.CreateTenantScope(tenant);
        var sync = scope.ServiceProvider.GetRequiredService<ReplicaSync>();
        try
        {
            await using (await locks.AcquireAsync(tenant, ct))
            {
                var outcome = await sync.SyncAsync(ReplicaBinding.Of(seed), ct, rewriteGrants: !_grantsRewritten.ContainsKey(tenant.Id));
                _grantsRewritten[tenant.Id] = true;
                if (outcome.Applied > 0)
                    log.LogInformation("Synced the Organization replica of tenant {Tenant}: {Applied} entries, {Published} events.", tenant.Id, outcome.Applied, outcome.Published);
                return outcome;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(e, "Could not sync the Organization replica of tenant {Tenant}; the replica keeps serving what it has.", tenant.Id);
            await sync.RecordErrorAsync(e.Message, CancellationToken.None);
            return null;
        }
    }
}

/// <summary>
/// The connector's loop (a hosted service of the module's container, stopped on unload): every
/// <see cref="OrganizationOptions.PollInterval"/> it pulls the deployment's seeds, publishes
/// <c>tenancy.seed-changed.v1</c> when one changed (the host's reconciler then runs at once), and syncs the replica of
/// every active tenant created from one of them. When the central service is down, nothing changes locally: the
/// replicas keep serving and the cursors resume later.
/// </summary>
public sealed class OrgConnector(
    IServiceProvider services,
    IOrganizationClient central,
    SeedCatalog catalog,
    ReplicaSyncRunner runner,
    ITenantDirectory tenants,
    IOptions<OrganizationOptions> options,
    ILogger<OrgConnector> log) : BackgroundService
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private bool _waitingLogged;

    /// <summary>Runs a cycle now instead of at the next interval.</summary>
    public void Wake()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.ConnectorEnabled)
        {
            log.LogInformation("Organization:CentralUrl is not set: the Organization connector does not sync.");
            return;
        }
        catalog.UseDeployment(options.Value.DeploymentId);
        var interval = options.Value.PollInterval > TimeSpan.Zero ? options.Value.PollInterval : TimeSpan.FromSeconds(30);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "The Organization connector's cycle failed; retrying in {Interval}.", interval);
            }
            try
            {
                await _wake.WaitAsync(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One cycle: seeds, then every tenant's replica.</summary>
    public async Task RunOnceAsync(CancellationToken ct)
    {
        if (catalog.DeploymentId is not { } deployment)
        {
            if (!_waitingLogged) log.LogInformation("The Organization connector waits for the deployment id (Organization:DeploymentId, or the host's seed reconciler).");
            _waitingLogged = true;
            return;
        }

        try
        {
            var changed = catalog.Update(await central.ListDeploymentSeedsAsync(deployment, ct));
            if (changed.Count > 0) await PublishSeedChangedAsync(deployment, changed, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException && catalog.Loaded)
        {
            log.LogWarning(e, "Could not pull the seeds of deployment {Deployment}; syncing the tenants of the last list.", deployment);
        }

        var again = false;
        foreach (var tenant in tenants.All.Where(t => t.IsActive))
        {
            if (catalog.ForTenant(tenant.Id) is not { } seed) continue;
            var outcome = await runner.SyncAsync(tenant, seed, ct);
            again |= outcome?.SeedsChanged == true;
        }
        if (again) Wake(); // a seed of the org changed: pull the seeds again soon
    }

    /// <summary>
    /// Publishes <c>tenancy.seed-changed.v1</c> through the outbox of one of the deployment's tenants (an event always
    /// belongs to a tenant; the host ignores it). Without a tenant yet, the reconciler's own timer finds the seed.
    /// </summary>
    private async Task PublishSeedChangedAsync(string deployment, IReadOnlyList<DeploymentSeedDto> changed, CancellationToken ct)
    {
        foreach (var tenant in tenants.All.Where(t => t.IsActive))
        {
            try
            {
                await using var scope = services.CreateTenantScope(tenant);
                var db = scope.ServiceProvider.GetRequiredService<ReplicaDbContext>();
                scope.ServiceProvider.GetRequiredService<IIntegrationEvents>().Publish(TenantSeedNames.SeedChangedEvent,
                    new { DeploymentId = deployment, Seeds = changed.Select(s => new { s.Id, s.Version, s.TenantId }) }, $"deployment:{deployment}");
                await db.SaveChangesAsync(ct);
                log.LogInformation("Seeds changed for deployment {Deployment}: {Seeds}", deployment, string.Join(", ", changed.Select(s => $"{s.Id}@{s.Version}")));
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogDebug(e, "Could not publish the seed change through tenant {Tenant}.", tenant.Id);
            }
        }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// The first sync of a new tenant, before the module serves it (after the replica's migrations, which are registered
/// first). Only for a replica that never synced, so loading the module with many tenants does not wait on the central
/// service. Best effort: when the central service does not answer in time, the connector's loop syncs it later.
/// </summary>
public sealed class ReplicaInitializer(
    IServiceProvider services, IOrganizationClient central, SeedCatalog catalog, ReplicaSyncRunner runner, IOptions<OrganizationOptions> options,
    ILogger<ReplicaInitializer> log) : ITenantInitializer
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public async Task InitializeAsync(TenantInfo tenant, CancellationToken ct)
    {
        if (!options.Value.ConnectorEnabled) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            await using (var scope = services.CreateTenantScope(tenant))
            {
                if (await scope.ServiceProvider.GetRequiredService<ReplicaDbContext>().Sync.AnyAsync(timeout.Token)) return;
            }
            if (catalog.ForTenant(tenant.Id) is null && catalog.DeploymentId is { } deployment)
                catalog.Update(await central.ListDeploymentSeedsAsync(deployment, timeout.Token));
            if (catalog.ForTenant(tenant.Id) is { } seed) await runner.SyncAsync(tenant, seed, timeout.Token);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            log.LogWarning(e, "The first Organization sync of tenant {Tenant} did not finish; the connector retries it.", tenant.Id);
        }
    }
}

/// <summary>
/// The module's <see cref="ILiveScopeResolver"/>, server-side scopes only (never what a client sends), from the
/// subscriber's tenant replica. Anything it does not know gets no scopes, so only the topic's exact scope counts
/// (fails closed):
/// <list type="bullet">
/// <item><c>unit:{id}</c>: the unit's own scope and its ancestors' (<c>unit:{ancestor}</c>, from the replica's paths), so
/// a grant on a Department covers the topics of its Sections. Units the tenant cannot see have none.</item>
/// <item><c>organization:org:{orgId:N}</c> (<see cref="OrgLiveTopic"/>): the org's root unit <c>unit:{root}</c>, when the
/// tenant's replica follows that org, so an <c>org.read</c> grant on the whole org counts. A grant on a unit below
/// the root does not: the topic speaks for the whole org.</item>
/// </list>
/// </summary>
public sealed class UnitLiveScopes(IUnitDirectory directory, ReplicaDbContext db) : ILiveScopeResolver
{
    public async Task<IReadOnlyList<string>> ScopesAsync(string kind, string key, CancellationToken ct)
    {
        if (kind == OrgLiveTopic.Kind) return await OrgScopesAsync(key, ct);
        if (kind != LiveTopics.UnitKind || !Guid.TryParse(key, out var unitId)) return [];
        return await directory.GetAsync(unitId, ct) is { } unit ? UnitScopes.For(unit.Path) : [];
    }

    /// <summary>
    /// The org topic of the org this tenant's replica follows: the org's root unit, and the tenant's own root unit (its
    /// seed's) with every unit above it, so <c>org.read</c> on the tenant's root, as its seed admins and members of
    /// that unit hold, is enough. A grant only on a unit below the tenant's root does not count. Any other org: none.
    /// </summary>
    private async Task<IReadOnlyList<string>> OrgScopesAsync(string key, CancellationToken ct)
    {
        if (!Guid.TryParseExact(key, "N", out var orgId)) return [];
        if (await db.Sync.AsNoTracking().FirstOrDefaultAsync(ct) is not { } state || state.OrgId != orgId) return [];
        var root = await db.Units.AsNoTracking().Where(u => u.OrgId == orgId && u.ParentId == null).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        var tenantRoot = await db.Units.AsNoTracking().Where(u => u.Id == state.RootUnitId).Select(u => u.Path).FirstOrDefaultAsync(ct);
        IEnumerable<string> scopes = root is { } id ? [UnitScopes.Of(id)] : [];
        if (tenantRoot is not null) scopes = scopes.Concat(UnitScopes.For(tenantRoot));
        return [.. scopes.Distinct(StringComparer.Ordinal)];
    }
}

/// <summary><see cref="IUnitDirectory"/> from the current tenant's replica.</summary>
public sealed class ReplicaUnitDirectory(ReplicaDbContext db) : IUnitDirectory
{
    private (bool Loaded, string? RootPath) _root;

    public async Task<IReadOnlyList<DirectoryUnitDto>> ListAsync(CancellationToken ct = default)
    {
        if (await RootPathAsync(ct) is not { } root) return [];
        var ancestors = Ids(root);
        var prefix = root + UnitPaths.Separator;
        var units = await db.Units.AsNoTracking()
            .Where(u => ancestors.Contains(u.Id) || u.Path.StartsWith(prefix))
            .ToListAsync(ct);
        return [.. units.OrderBy(u => u.Path, StringComparer.Ordinal).Select(u => ToDto(u, root))];
    }

    public async Task<DirectoryUnitDto?> GetAsync(Guid unitId, CancellationToken ct = default)
    {
        if (await RootPathAsync(ct) is not { } root) return null;
        var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        return unit is not null && Visible(unit.Path, root) ? ToDto(unit, root) : null;
    }

    public async Task<IReadOnlyList<DirectoryUnitDto>> AncestorsAsync(Guid unitId, CancellationToken ct = default)
    {
        if (await GetAsync(unitId, ct) is not { } unit || _root.RootPath is not { } root) return [];
        var ids = Ids(unit.Path).Where(id => id != unitId).ToList();
        var units = await db.Units.AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        return [.. units.OrderBy(u => u.Path.Length).Select(u => ToDto(u, root))];
    }

    public async Task<IReadOnlyList<string>> CategoriesOfAsync(Guid unitId, CancellationToken ct = default)
    {
        if (await GetAsync(unitId, ct) is not { } unit || _root.RootPath is not { } root) return [];
        if (unit.Categories.Count > 0) return unit.Categories;
        var prefix = unit.Path + UnitPaths.Separator;
        var below = await db.Units.AsNoTracking().Where(u => u.Path.StartsWith(prefix)).ToListAsync(ct);
        return [.. below.Where(u => UnitPaths.IsSelfOrBelow(u.Path, root)).OrderBy(u => u.Path, StringComparer.Ordinal)
            .SelectMany(u => u.Categories).Distinct(StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<DirectoryMemberDto>> MembersOfAsync(Guid unitId, CancellationToken ct = default)
    {
        if (await GetAsync(unitId, ct) is not { InTenant: true }) return [];
        var memberships = await db.Memberships.AsNoTracking().Where(m => m.UnitId == unitId).ToListAsync(ct);
        var personIds = memberships.Select(m => m.PersonId).Distinct().ToList();
        var names = await db.Persons.AsNoTracking().Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);
        return [.. memberships.OrderByDescending(m => m.Primary).ThenBy(m => m.From)
            .Select(m => new DirectoryMemberDto(m.Id, m.PersonId, m.Issuer, m.Subject, names.GetValueOrDefault(m.PersonId), m.UnitId, m.PositionId,
                m.PositionKey, m.Permissions, m.From, m.To, m.Primary))];
    }

    private async Task<string?> RootPathAsync(CancellationToken ct)
    {
        if (_root.Loaded) return _root.RootPath;
        var state = await db.Sync.AsNoTracking().FirstOrDefaultAsync(ct);
        string? path = null;
        if (state is not null)
            path = await db.Units.AsNoTracking().Where(u => u.Id == state.RootUnitId).Select(u => u.Path).FirstOrDefaultAsync(ct);
        _root = (true, path);
        return path;
    }

    private static List<Guid> Ids(string path) => [.. UnitPaths.Ids(path).Select(id => Guid.TryParseExact(id, "N", out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)];

    private static bool Visible(string path, string root) => UnitPaths.IsSelfOrBelow(path, root) || UnitPaths.IsSelfOrBelow(root, path);

    private static DirectoryUnitDto ToDto(UnitReplica u, string root) =>
        new(u.Id, u.OrgId, u.ParentId, u.TypeKey, u.Path, u.NameEn, u.NameAr, u.Tags, u.TimeZone, u.Categories, u.Binding, u.SeedId, u.Version,
            UnitPaths.IsSelfOrBelow(u.Path, root));
}
