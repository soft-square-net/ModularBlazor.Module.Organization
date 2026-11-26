using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data.Messaging;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;
using soft.square.net.WebApi.Modules.Organization.UseCases.Feed;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;

namespace soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data;

/// <summary>
/// One tenant's replica on in-memory SQLite (EnsureCreated, with the integration outbox), synced from a central
/// <see cref="OrganizationDatabase"/> through its real use cases (<see cref="CentralThroughUseCases"/>), with an
/// in-memory grant store and live notifier.
/// </summary>
public sealed class ReplicaDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<ReplicaDbContext> _options;

    public ReplicaDatabase(OrganizationDatabase central, string tenantId = "acme-news")
    {
        Central = new CentralThroughUseCases(central);
        Clock = central.Clock;
        Tenant = new TenantInfo(tenantId, tenantId, TenantStatus.Active, [], "test", tenantId);
        _connection.Open();
        _options = new DbContextOptionsBuilder<ReplicaDbContext>().UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsets>().Options;
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public CentralThroughUseCases Central { get; }
    public TestClock Clock { get; }
    public TenantInfo Tenant { get; }
    public MemoryGrantStore Grants { get; } = new();
    public MemoryLiveNotifier Live { get; } = new();
    public OrganizationOptions Options { get; } = new() { FeedOverlap = TimeSpan.FromMinutes(1), FeedPageSize = 3 };

    public DbContextOptions<ReplicaDbContext> ContextOptions => _options;

    public ReplicaDbContext NewContext() => new(_options, new FixedTenant(Tenant));

    /// <summary>One sync, in a new context, as the connector's runner would run it.</summary>
    public async Task<SyncOutcome> SyncAsync(ReplicaBinding binding, CancellationToken ct)
    {
        await using var context = NewContext();
        return await NewSync(context).SyncAsync(binding, ct);
    }

    public ReplicaSync NewSync(ReplicaDbContext context)
    {
        var events = new IntegrationEvents<ReplicaDbContext>(context, new IntegrationEventContext(),
            new IntegrationEventsSettings<ReplicaDbContext>(new IntegrationEventsOptions { Source = "organization" }));
        var services = new ServiceCollection().AddSingleton<ILiveNotifier>(Live).BuildServiceProvider();
        return new ReplicaSync(context, Central, events, Grants, services, Microsoft.Extensions.Options.Options.Create(Options), Clock, NullLogger<ReplicaSync>.Instance);
    }

    /// <summary>The events the replica republished (its outbox), in order.</summary>
    public async Task<IReadOnlyList<OutboxMessage>> OutboxAsync(CancellationToken ct)
    {
        await using var context = NewContext();
        return await context.Set<OutboxMessage>().OrderBy(m => m.OccurredAt).ThenBy(m => m.Id).ToListAsync(ct);
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class FixedTenant(TenantInfo tenant) : ITenantContext
{
    public TenantInfo? Tenant { get; } = tenant;
    public string? ResolvedBy => "test";
}

/// <summary>
/// The connector's view of the central service, through the central use cases as the connector's service client.
/// <see cref="Hidden"/> holds feed entries back, as a transaction that has taken its id but not committed yet.
/// </summary>
public sealed class CentralThroughUseCases(OrganizationDatabase central) : IOrganizationClient
{
    public HashSet<long> Hidden { get; } = [];
    public int FeedReads { get; private set; }
    public bool Down { get; set; }

    public async Task<FeedPage> ReadFeedAsync(Guid orgId, long after = 0, int take = 200, CancellationToken ct = default)
    {
        ThrowIfDown();
        FeedReads++;
        var page = (await central.Run<ReadFeedHandler, Result<FeedPage>>(OrganizationDatabase.Connector, h => h.Handle(new ReadFeedQuery(orgId, after, take), ct))).Value;
        if (Hidden.Count == 0) return page;
        // Uncommitted rows are invisible: the page ends before the first hidden one... and rows after it are visible.
        var visible = page.Entries.Where(e => !Hidden.Contains(e.Sequence)).ToList();
        return new FeedPage(visible, visible.Count == 0 ? after : visible[^1].Sequence, page.More);
    }

    public async Task<IReadOnlyList<DeploymentSeedDto>> ListDeploymentSeedsAsync(string deploymentId, CancellationToken ct = default)
    {
        ThrowIfDown();
        return (await central.Run<ListDeploymentSeedsHandler, Result<IReadOnlyList<DeploymentSeedDto>>>(OrganizationDatabase.Connector,
            h => h.Handle(new ListDeploymentSeedsQuery(deploymentId), ct))).Value;
    }

    public async Task ReportSeedAsync(string seedId, SeedReportRequest report, CancellationToken ct = default)
    {
        ThrowIfDown();
        var result = await central.Run<ReportSeedHandler, Result>(OrganizationDatabase.Connector, h => h.Handle(new ReportSeedCommand(seedId, report), ct));
        if (!result.IsSuccess) throw new InvalidOperationException(string.Join("; ", result.Errors));
    }

    private void ThrowIfDown()
    {
        if (Down) throw new HttpRequestException("The central service is down.");
    }

    // The connector only pulls seeds and feeds.
    public Task<IReadOnlyList<OrgDto>> ListOrgsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgDto> GetOrgAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgDto> CreateOrgAsync(CreateOrgRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgDto> UpdateOrgAsync(Guid orgId, UpdateOrgRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgDto> SetOrgAdminsAsync(Guid orgId, SetOrgAdminsRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<UnitTypeDto>> ListUnitTypesAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UnitTypeDto> CreateUnitTypeAsync(Guid orgId, CreateUnitTypeRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrgUnitDto>> ListUnitsAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgUnitDto> GetUnitAsync(Guid orgId, Guid unitId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<UnitTenantDto> GetUnitTenantAsync(Guid orgId, Guid unitId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgUnitDto> CreateUnitAsync(Guid orgId, CreateUnitRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgUnitDto> UpdateUnitAsync(Guid orgId, Guid unitId, UpdateUnitRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrgUnitDto> MoveUnitAsync(Guid orgId, Guid unitId, MoveUnitRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveUnitAsync(Guid orgId, Guid unitId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PersonDto>> ListPersonsAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PersonDto> CreatePersonAsync(Guid orgId, CreatePersonRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PersonDto> UpdatePersonAsync(Guid orgId, Guid personId, UpdatePersonRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MembershipDto>> ListMembershipsAsync(Guid orgId, Guid? unitId = null, Guid? personId = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<MembershipDto> CreateMembershipAsync(Guid orgId, CreateMembershipRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<MembershipDto> UpdateMembershipAsync(Guid orgId, Guid membershipId, UpdateMembershipRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task RemoveMembershipAsync(Guid orgId, Guid membershipId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<PositionDto>> ListPositionsAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PositionDto> CreatePositionAsync(Guid orgId, CreatePositionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<PositionDto> UpdatePositionAsync(Guid orgId, Guid positionId, UpdatePositionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<TenantSeedDto>> ListSeedsAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TenantSeedDto> CreateSeedAsync(Guid orgId, CreateSeedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TenantSeedDto> UpdateSeedAsync(Guid orgId, string seedId, UpdateSeedRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<TenantLinkDto>> ListLinksAsync(Guid orgId, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>The platform's grant store for one tenant, in memory.</summary>
public sealed class MemoryGrantStore : IGrantStore
{
    private readonly Dictionary<string, List<AccessGrant>> _sources = [];

    public IReadOnlyList<AccessGrant> All => [.. _sources.Values.SelectMany(g => g)];

    public IReadOnlyList<AccessGrant> Of(string source) => _sources.GetValueOrDefault(source) ?? [];

    public Task<IReadOnlyList<AccessGrant>> ListAsync(string? userId = null, string? source = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AccessGrant>>([.. All.Where(g => (userId is null || g.UserId == userId) && (source is null || g.Source == source))]);

    public Task<AccessGrant> AddAsync(NewGrant grant, string source = GrantSources.Standing, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> RevokeAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<int> EndAsync(string userId, string source, DateTimeOffset at, CancellationToken ct = default) => throw new NotSupportedException();

    public Task ReplaceSourceAsync(string source, IReadOnlyCollection<NewGrant> grants, CancellationToken ct = default)
    {
        if (grants.Count == 0) _sources.Remove(source);
        else
            _sources[source] = [.. grants.Select(g => new AccessGrant(Guid.NewGuid(), "t", g.UserId, g.Permission, g.Scope, g.ValidFrom, g.ValidTo, source, DateTimeOffset.UnixEpoch, null))];
        return Task.CompletedTask;
    }
}

public sealed class MemoryLiveNotifier : ILiveNotifier
{
    public List<(string Topic, string Type, long? Sequence)> Sent { get; } = [];

    public Task PublishAsync(string topic, string type, System.Text.Json.JsonElement? payload = null, long? sequence = null, CancellationToken ct = default)
    {
        Sent.Add((topic, type, sequence));
        return Task.CompletedTask;
    }

    public Task PublishToUnitAsync(string unitPath, string type, System.Text.Json.JsonElement? payload = null, long? sequence = null, CancellationToken ct = default)
    {
        foreach (var topic in LiveTopics.UnitAndAncestors(unitPath)) Sent.Add((topic, type, sequence));
        return Task.CompletedTask;
    }
}
