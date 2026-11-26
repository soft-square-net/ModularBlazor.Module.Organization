using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data.Messaging;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;
using soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases.Connector;
using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;
using soft.square.net.WebApi.Modules.Organization.UseCases.Persons;
using soft.square.net.WebApi.Modules.Organization.UseCases.Positions;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;
using soft.square.net.WebApi.Modules.Organization.UseCases.Units;
using Db = soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data.OrganizationDatabase;

namespace soft.square.net.WebApi.Modules.Organization.IntegrationTests;

/// <summary>
/// The connector end to end on SQLite: the central use cases write the feed, the replica sync applies it to a tenant's
/// replica, republishes the events through the replica's outbox, writes the grants, and serves the directory and the
/// seeds.
/// </summary>
public sealed class ConnectorFlows : IDisposable
{
    private readonly Db _central = new();
    private readonly ReplicaDatabase _replica;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ConnectorFlows() => _replica = new ReplicaDatabase(_central);

    public void Dispose()
    {
        _replica.Dispose();
        _central.Dispose();
    }

    // Acme: root > News (the tenant acme-news, admin alice) > Sports > { Football [c1, c2], Tennis [c3] }; root > Sport
    // (another tenant's) with carol. bob manages Sports.
    private sealed record Tree(
        OrgDto Org, OrgUnitDto News, OrgUnitDto Sport, OrgUnitDto Sports, OrgUnitDto Football, OrgUnitDto Tennis,
        PersonDto Alice, PersonDto Bob, PersonDto Carol, PositionDto Manager, TenantSeedDto Seed)
    {
        public ReplicaBinding Binding => new(Seed.Id, Org.Id, News.Id);
    }

    private async Task<Tree> BuildAsync()
    {
        var org = (await _central.Run<CreateOrgHandler, Result<OrgDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateOrgCommand(new CreateOrgRequest("acme", "Acme", "أكمي")), Ct))).Value;
        var news = await UnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var sport = await UnitAsync(org.Id, org.RootUnitId, "sector", "Sport");
        var sports = await UnitAsync(org.Id, news.Id, "department", "Sports");
        var football = await UnitAsync(org.Id, sports.Id, "section", "Football", "c1", "c2");
        var tennis = await UnitAsync(org.Id, sports.Id, "section", "Tennis", "c3");
        var alice = await PersonAsync(org.Id, "alice");
        var bob = await PersonAsync(org.Id, "bob");
        var carol = await PersonAsync(org.Id, "carol");
        var positions = (await _central.Run<ListPositionsHandler, Result<IReadOnlyList<PositionDto>>>(Db.PlatformAdmin, h => h.Handle(new ListPositionsQuery(org.Id), Ct))).Value;
        var manager = positions.Single(p => p.Key == PositionDefinition.ManagerKey);
        await MemberAsync(org.Id, bob.Id, sports.Id, manager.Id);
        await MemberAsync(org.Id, carol.Id, sport.Id, manager.Id);
        var seed = (await _central.Run<CreateSeedHandler, Result<TenantSeedDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateSeedCommand(org.Id, new CreateSeedRequest(news.Id, "eu-1", "acme-news", "Acme News", ["news.acme.test"], "pg-1", "acme_news", ["content"], [alice.Id])), Ct))).Value;
        return new Tree(org, news, sport, sports, football, tennis, alice, bob, carol, manager, seed);
    }

    private async Task<OrgUnitDto> UnitAsync(Guid orgId, Guid parentId, string type, string name, params string[] categories)
    {
        var result = await _central.Run<CreateUnitHandler, Result<OrgUnitDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateUnitCommand(orgId, new CreateUnitRequest(parentId, type, name, name, null, null, categories)), Ct));
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private async Task<PersonDto> PersonAsync(Guid orgId, string subject) =>
        (await _central.Run<CreatePersonHandler, Result<PersonDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreatePersonCommand(orgId, new CreatePersonRequest(Db.Issuer, subject, subject)), Ct))).Value;

    private async Task<MembershipDto> MemberAsync(Guid orgId, Guid personId, Guid unitId, Guid? positionId = null)
    {
        var result = await _central.Run<CreateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateMembershipCommand(orgId, new CreateMembershipRequest(personId, unitId, positionId)), Ct));
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private async Task MoveAsync(Guid orgId, Guid unitId, Guid newParentId)
    {
        var unit = (await _central.Run<GetUnitHandler, Result<OrgUnitDto>>(Db.PlatformAdmin, h => h.Handle(new GetUnitQuery(orgId, unitId), Ct))).Value;
        var moved = await _central.Run<MoveUnitHandler, Result<OrgUnitDto>>(Db.PlatformAdmin,
            h => h.Handle(new MoveUnitCommand(orgId, unitId, new MoveUnitRequest(unit.Version, newParentId)), Ct));
        moved.IsSuccess.ShouldBeTrue(string.Join("; ", moved.Errors));
    }

    private async Task<T> InReplicaAsync<T>(Func<ReplicaDbContext, Task<T>> read)
    {
        await using var context = _replica.NewContext();
        return await read(context);
    }

    private static IReadOnlyList<string> Subjects(IEnumerable<OutboxMessage> messages, string type) =>
        [.. messages.Where(m => m.Type == type).Select(m => IntegrationEnvelope.Parse(m.Envelope).Subject)];

    [Fact]
    public async Task TheReplicaHoldsTheTenantsSliceAndASecondSyncChangesNothing()
    {
        var t = await BuildAsync();

        var first = await _replica.SyncAsync(t.Binding, Ct);

        first.Applied.ShouldBeGreaterThan(0);
        var directory = new ReplicaUnitDirectory(_replica.NewContext());
        var units = await directory.ListAsync(Ct);
        units.Select(u => u.Id).ShouldBe([t.Org.RootUnitId, t.News.Id, t.Sports.Id, t.Football.Id, t.Tennis.Id], ignoreOrder: true);
        units.Single(u => u.Id == t.Org.RootUnitId).InTenant.ShouldBeFalse();
        units.Where(u => u.Id != t.Org.RootUnitId).ShouldAllBe(u => u.InTenant);
        (await directory.GetAsync(t.Sport.Id, Ct)).ShouldBeNull(); // another tenant's unit: kept for moves, never served
        (await directory.AncestorsAsync(t.Football.Id, Ct)).Select(u => u.Id).ShouldBe([t.Org.RootUnitId, t.News.Id, t.Sports.Id]);
        (await directory.CategoriesOfAsync(t.Football.Id, Ct)).ShouldBe(["c1", "c2"]);
        (await directory.CategoriesOfAsync(t.Sports.Id, Ct)).ShouldBe(["c1", "c2", "c3"]);
        (await directory.MembersOfAsync(t.Sports.Id, Ct)).Single().DisplayName.ShouldBe("bob");
        (await InReplicaAsync(db => db.Memberships.Select(m => m.Subject).ToListAsync(Ct))).ShouldBe(["alice", "bob"], ignoreOrder: true);

        // Grants: bob's manager permissions on Sports, alice's unit-admin permissions (from the seed) on News.
        _replica.Grants.Of(OrgGrants.Source(t.Sports.Id)).Select(g => (g.UserId, g.Permission, g.Scope))
            .ShouldBe([.. t.Manager.Permissions.Select(p => ("bob", p, UnitScopes.Of(t.Sports.Id)))], ignoreOrder: true);
        _replica.Grants.Of(OrgGrants.Source(t.News.Id)).ShouldContain(g => g.UserId == "alice" && g.Permission == "org.members.manage");
        _replica.Grants.All.ShouldNotContain(g => g.UserId == "carol");

        // Events: republished for the tenant's units and the root's ancestors, never for the other tenant's.
        var outbox = await _replica.OutboxAsync(Ct);
        Subjects(outbox, OrgEvents.UnitCreated).ShouldBe(
            [.. new[] { t.Org.RootUnitId, t.News.Id, t.Sports.Id, t.Football.Id, t.Tennis.Id }.Select(OrgEvents.UnitSubject)], ignoreOrder: true);
        outbox.Where(m => m.Type == OrgEvents.MembershipChanged).Count().ShouldBe(2);
        outbox.ShouldAllBe(m => m.TenantId == "acme-news");
        IntegrationEnvelope.Parse(outbox.First(m => m.Type == OrgEvents.MembershipChanged).Envelope).UnitPath.ShouldNotBeNull();
        _replica.Live.Sent.ShouldContain(s => s.Topic == LiveTopicsOf(t.Football.Id) && s.Type == OrgEvents.UnitCreated);
        _replica.Live.Sent.ShouldAllBe(s => s.Sequence == null);
        // The org topic: one nudge per applied feed page (page size 3).
        var orgMessages = _replica.Live.Sent.Where(s => s.Topic == OrgLiveTopic.Topic(t.Org.Id)).ToList();
        orgMessages.ShouldNotBeEmpty();
        orgMessages.ShouldAllBe(s => s.Type == OrgLiveTopic.ChangedMessage);
        _replica.Live.Payloads.Where(p => p.Topic == OrgLiveTopic.Topic(t.Org.Id))
            .ShouldAllBe(p => p.Payload.GetProperty("type").GetString() != null && p.Payload.GetProperty("id").GetString() != null);

        var scopes = new UnitLiveScopes(new ReplicaUnitDirectory(_replica.NewContext()), _replica.NewContext());
        (await scopes.ScopesAsync("unit", UnitPaths.Id(t.Football.Id), Ct)).ShouldBe(UnitScopes.For(t.Football.Path));
        (await scopes.ScopesAsync("unit", UnitPaths.Id(t.Sport.Id), Ct)).ShouldBeEmpty();
        (await scopes.ScopesAsync("post", UnitPaths.Id(t.Football.Id), Ct)).ShouldBeEmpty();
        (await scopes.ScopesAsync(OrgLiveTopic.Kind, OrgLiveTopic.Key(t.Org.Id), Ct)).ShouldBe([UnitScopes.Of(t.Org.RootUnitId)]);
        (await scopes.ScopesAsync(OrgLiveTopic.Kind, OrgLiveTopic.Key(Guid.NewGuid()), Ct)).ShouldBeEmpty(); // not this tenant's org
        (await scopes.ScopesAsync(OrgLiveTopic.Kind, "not-an-id", Ct)).ShouldBeEmpty();

        var sent = _replica.Live.Sent.Count;
        var second = await _replica.SyncAsync(t.Binding, Ct);
        second.Applied.ShouldBe(0);
        (await _replica.OutboxAsync(Ct)).Count.ShouldBe(outbox.Count);
        _replica.Live.Sent.Count.ShouldBe(sent); // nothing applied, nothing sent
    }

    private static string LiveTopicsOf(Guid unitId) => $"unit:{UnitPaths.Id(unitId)}";

    [Fact]
    public async Task AFeedEntryThatCommitsAfterAHigherOneIsAppliedOnce()
    {
        var t = await BuildAsync();
        await _replica.SyncAsync(t.Binding, Ct);
        var dave = await PersonAsync(t.Org.Id, "dave");

        // dave's membership takes its feed id first but commits after the next change (a new section) was read.
        var before = await InReplicaAsync(db => db.Sync.SingleAsync(Ct));
        await MemberAsync(t.Org.Id, dave.Id, t.Football.Id);
        var late = (await _replica.Central.ReadFeedAsync(t.Org.Id, before.Cursor, 500, Ct)).Entries[^1].Sequence;
        _replica.Central.Hidden.Add(late);
        var squash = await UnitAsync(t.Org.Id, t.Sports.Id, "section", "Squash", "c4");

        await _replica.SyncAsync(t.Binding, Ct);
        (await InReplicaAsync(db => db.Units.AnyAsync(u => u.Id == squash.Id, Ct))).ShouldBeTrue();
        (await InReplicaAsync(db => db.Memberships.AnyAsync(m => m.Subject == "dave", Ct))).ShouldBeFalse();

        _central.Clock.Advance(TimeSpan.FromSeconds(30)); // within the overlap
        _replica.Central.Hidden.Clear();
        await _replica.SyncAsync(t.Binding, Ct);
        await _replica.SyncAsync(t.Binding, Ct);

        (await InReplicaAsync(db => db.Memberships.CountAsync(m => m.Subject == "dave", Ct))).ShouldBe(1);
        var outbox = await _replica.OutboxAsync(Ct);
        outbox.Count(m => m.Type == OrgEvents.MembershipChanged && IntegrationEnvelope.Parse(m.Envelope).Data.GetProperty("subject").GetString() == "dave").ShouldBe(1);
        Subjects(outbox, OrgEvents.UnitCreated).Count(s => s == OrgEvents.UnitSubject(squash.Id)).ShouldBe(1);

        // Once the entries are older than the overlap, the cursor moves past them and they are not read again.
        _central.Clock.Advance(TimeSpan.FromMinutes(5));
        await _replica.SyncAsync(t.Binding, Ct);
        var settled = await InReplicaAsync(db => db.Sync.SingleAsync(Ct));
        settled.Cursor.ShouldBeGreaterThanOrEqualTo(late);
        FeedWindow.Deserialize(settled.Cursor, settled.Applied).Applied.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnitsMovingOutAndBackTakeTheirMembershipsAndGrantsWithThem()
    {
        var t = await BuildAsync();
        await _replica.SyncAsync(t.Binding, Ct);
        _replica.Grants.Of(OrgGrants.Source(t.Sports.Id)).ShouldNotBeEmpty();

        await MoveAsync(t.Org.Id, t.Sports.Id, t.Sport.Id); // into the other tenant
        await _replica.SyncAsync(t.Binding, Ct);

        (await InReplicaAsync(db => db.Memberships.AnyAsync(m => m.Subject == "bob", Ct))).ShouldBeFalse();
        _replica.Grants.Of(OrgGrants.Source(t.Sports.Id)).ShouldBeEmpty();
        (await new ReplicaUnitDirectory(_replica.NewContext()).GetAsync(t.Football.Id, Ct)).ShouldBeNull();

        await MoveAsync(t.Org.Id, t.Sports.Id, t.News.Id); // back: the skipped memberships are read again
        await _replica.SyncAsync(t.Binding, Ct);

        (await InReplicaAsync(db => db.Memberships.SingleAsync(m => m.Subject == "bob", Ct))).UnitId.ShouldBe(t.Sports.Id);
        _replica.Grants.Of(OrgGrants.Source(t.Sports.Id)).ShouldNotBeEmpty();
        _replica.Grants.All.ShouldNotContain(g => g.UserId == "carol");
        var football = await new ReplicaUnitDirectory(_replica.NewContext()).GetAsync(t.Football.Id, Ct);
        football!.Path.ShouldBe(UnitPaths.Child(UnitPaths.Child(t.News.Path, t.Sports.Id), t.Football.Id));
    }

    [Fact]
    public async Task APositionChangeRewritesTheGrantsOfItsMemberships()
    {
        var t = await BuildAsync();
        await _replica.SyncAsync(t.Binding, Ct);

        var updated = await _central.Run<UpdatePositionHandler, Result<PositionDto>>(Db.PlatformAdmin,
            h => h.Handle(new UpdatePositionCommand(t.Org.Id, t.Manager.Id, new UpdatePositionRequest(t.Manager.Version, "Manager", "مدير", ["shifts.manage", "content.publish"])), Ct));
        updated.IsSuccess.ShouldBeTrue(string.Join("; ", updated.Errors));
        await _replica.SyncAsync(t.Binding, Ct);

        _replica.Grants.Of(OrgGrants.Source(t.Sports.Id)).Select(g => g.Permission).ShouldBe(["shifts.manage", "content.publish"], ignoreOrder: true);
    }

    [Fact]
    public async Task RemovingASeedAdminRemovesOnlyTheMembershipTheSeedCreated()
    {
        var t = await BuildAsync();
        var erin = await PersonAsync(t.Org.Id, "erin");
        var unitAdmin = (await _central.Run<ListPositionsHandler, Result<IReadOnlyList<PositionDto>>>(Db.PlatformAdmin, h => h.Handle(new ListPositionsQuery(t.Org.Id), Ct)))
            .Value.Single(p => p.Key == PositionDefinition.UnitAdminKey);
        await MemberAsync(t.Org.Id, erin.Id, t.News.Id, unitAdmin.Id); // added by hand
        // erin also becomes a seed admin: she already is a unit admin, so the seed creates nothing for her.
        await UpdateSeedAsync(t, [t.Alice.Id, erin.Id]);
        await _replica.SyncAsync(t.Binding, Ct);
        _replica.Grants.Of(OrgGrants.Source(t.News.Id)).Select(g => g.UserId).Distinct().ShouldBe(["alice", "erin"], ignoreOrder: true);

        await UpdateSeedAsync(t, []);

        var left = (await _central.Run<ListMembershipsHandler, Result<IReadOnlyList<MembershipDto>>>(Db.PlatformAdmin,
            h => h.Handle(new ListMembershipsQuery(t.Org.Id, t.News.Id), Ct))).Value;
        left.Select(m => m.PersonId).ShouldBe([erin.Id]);
        await _replica.SyncAsync(t.Binding, Ct);
        _replica.Grants.Of(OrgGrants.Source(t.News.Id)).Select(g => g.UserId).Distinct().ShouldBe(["erin"]);
    }

    private async Task UpdateSeedAsync(Tree t, IReadOnlyList<Guid> admins)
    {
        var seed = (await _central.Run<ListSeedsHandler, Result<IReadOnlyList<TenantSeedDto>>>(Db.PlatformAdmin, h => h.Handle(new ListSeedsQuery(t.Org.Id), Ct))).Value.Single();
        var result = await _central.Run<UpdateSeedHandler, Result<TenantSeedDto>>(Db.PlatformAdmin,
            h => h.Handle(new UpdateSeedCommand(t.Org.Id, seed.Id, new UpdateSeedRequest(seed.Version, seed.Name, seed.Hosts, seed.DatabaseServer, seed.DatabaseName, seed.Modules, admins)), Ct));
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task TheSeedSourceServesTheDeploymentsSeedsAndReportsBack()
    {
        var t = await BuildAsync();
        var catalog = new SeedCatalog();
        var source = new OrgSeedSource(_replica.Central, catalog, NullLogger<OrgSeedSource>.Instance);

        var seeds = await source.ListAsync("eu-1", Ct);

        var seed = seeds.ShouldHaveSingleItem();
        (seed.Id, seed.TenantId, seed.DeploymentId, seed.DatabaseServer, seed.DatabaseName).ShouldBe((t.Seed.Id, "acme-news", "eu-1", "pg-1", "acme_news"));
        seed.Admins.ShouldBe(["alice"]);
        seed.Extra.Deserialize<SeedExtra>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(new SeedExtra(t.Org.Id, t.News.Id));
        (await source.ListAsync("eu-2", Ct)).ShouldBeEmpty();

        await source.ReportAsync(seed.Id, seed.Version, SeedStatus.Adjusted, new TenantPlacement("pg-2", "acme_news"), null, Ct);
        var reported = (await _central.Run<ListSeedsHandler, Result<IReadOnlyList<TenantSeedDto>>>(Db.PlatformAdmin, h => h.Handle(new ListSeedsQuery(t.Org.Id), Ct))).Value.Single();
        (reported.Status, reported.ReportedVersion, reported.ActualPlacement).ShouldBe((SeedState.Adjusted, seed.Version, new PlacementDto("pg-2", "acme_news")));

        // The central service is down: the last list is served (a version the tenant has is a no-op for the host).
        await source.ListAsync("eu-1", Ct);
        _replica.Central.Down = true;
        (await source.ListAsync("eu-1", Ct)).ShouldHaveSingleItem().Id.ShouldBe(t.Seed.Id);
    }

    [Fact]
    public async Task TheConnectorSyncsSeededTenantsAndPublishesSeedChangesThroughTheOutbox()
    {
        var t = await BuildAsync();
        await using var services = ConnectorServices();
        var connector = services.GetRequiredService<OrgConnector>();
        services.GetRequiredService<SeedCatalog>().UseDeployment("eu-1");

        await connector.RunOnceAsync(Ct);

        var outbox = await _replica.OutboxAsync(Ct);
        outbox.Count(m => m.Type == TenantSeedNames.SeedChangedEvent).ShouldBe(1);
        outbox.ShouldContain(m => m.Type == OrgEvents.UnitCreated);
        (await new ReplicaUnitDirectory(_replica.NewContext()).ListAsync(Ct)).ShouldNotBeEmpty();

        await connector.RunOnceAsync(Ct); // nothing changed
        (await _replica.OutboxAsync(Ct)).Count(m => m.Type == TenantSeedNames.SeedChangedEvent).ShouldBe(1);

        await UpdateSeedAsync(t, []); // a new version
        await connector.RunOnceAsync(Ct);
        (await _replica.OutboxAsync(Ct)).Count(m => m.Type == TenantSeedNames.SeedChangedEvent).ShouldBe(2);

        // The central service is down: the replica keeps what it has and the cycle does not fail.
        _replica.Central.Down = true;
        await connector.RunOnceAsync(Ct);
        (await new ReplicaUnitDirectory(_replica.NewContext()).GetAsync(t.Football.Id, Ct)).ShouldNotBeNull();
        (await InReplicaAsync(db => db.Sync.SingleAsync(Ct))).LastError.ShouldNotBeNull();
    }

    [Fact]
    public async Task ANewTenantGetsItsFirstSyncFromTheInitializerOnlyOnce()
    {
        var t = await BuildAsync();
        _replica.Options.CentralUrl = "https://central.test/api/organization/";
        await using var services = ConnectorServices();
        services.GetRequiredService<SeedCatalog>().UseDeployment("eu-1");
        var initializer = services.GetRequiredService<ReplicaInitializer>();

        await initializer.InitializeAsync(_replica.Tenant, Ct);

        (await new ReplicaUnitDirectory(_replica.NewContext()).GetAsync(t.Football.Id, Ct)).ShouldNotBeNull();
        var reads = _replica.Central.FeedReads;
        _replica.Central.Down = true;
        await initializer.InitializeAsync(_replica.Tenant, Ct); // already synced: the connector's loop takes it from here
        _replica.Central.FeedReads.ShouldBe(reads);
    }

    /// <summary>The connector's services as the module's container has them, over the test databases.</summary>
    private ServiceProvider ConnectorServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ScopeTenant>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopeTenant>());
        services.AddScoped<ITenantScopeInitializer>(sp => sp.GetRequiredService<ScopeTenant>());
        services.AddScoped(sp => new ReplicaDbContext(_replica.ContextOptions, sp.GetRequiredService<ITenantContext>()));
        services.AddScoped<IntegrationEventContext>();
        services.AddScoped<IIntegrationEventContext>(sp => sp.GetRequiredService<IntegrationEventContext>());
        services.AddSingleton(new IntegrationEventsSettings<ReplicaDbContext>(new IntegrationEventsOptions { Source = "organization" }));
        services.AddScoped<IIntegrationEvents, IntegrationEvents<ReplicaDbContext>>();
        services.AddSingleton<IOrganizationClient>(_replica.Central);
        services.AddSingleton<soft.square.net.WebApi.Contracts.Security.IGrantStore>(_replica.Grants);
        services.AddSingleton<soft.square.net.WebApi.Contracts.Live.ILiveNotifier>(_replica.Live);
        services.AddSingleton<TimeProvider>(_central.Clock);
        services.AddSingleton(Options.Create(_replica.Options));
        services.AddSingleton<ITenantDirectory>(new OneTenant(_replica.Tenant));
        services.AddScoped<ReplicaSync>();
        services.AddSingleton<SeedCatalog>();
        services.AddSingleton<IReplicaSyncLock, NoLock>();
        services.AddSingleton<ReplicaSyncRunner>();
        services.AddSingleton<OrgConnector>();
        services.AddSingleton<ReplicaInitializer>();
        return services.BuildServiceProvider();
    }

    private sealed class ScopeTenant : ITenantContext, ITenantScopeInitializer
    {
        public TenantInfo? Tenant { get; private set; }
        public string? ResolvedBy { get; private set; }

        public void Use(TenantInfo tenant, string resolvedBy) => (Tenant, ResolvedBy) = (tenant, resolvedBy);
    }

    private sealed class OneTenant(TenantInfo tenant) : ITenantDirectory
    {
        public IReadOnlyCollection<TenantInfo> All { get; } = [tenant];
        public TenantInfo? Find(string id) => id == tenant.Id ? tenant : null;
        public TenantInfo? FindByHost(string host) => null;
        public Task NotifyChangedAsync(string tenantId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class NoLock : IReplicaSyncLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> AcquireAsync(TenantInfo tenant, CancellationToken ct) => Task.FromResult<IAsyncDisposable>(this);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
