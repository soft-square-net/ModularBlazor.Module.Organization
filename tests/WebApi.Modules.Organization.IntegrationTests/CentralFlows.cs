using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases.Feed;
using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;
using soft.square.net.WebApi.Modules.Organization.UseCases.Persons;
using soft.square.net.WebApi.Modules.Organization.UseCases.Positions;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;
using soft.square.net.WebApi.Modules.Organization.UseCases.Units;
using Db = soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data.OrganizationDatabase;

namespace soft.square.net.WebApi.Modules.Organization.IntegrationTests;

/// <summary>The central service's use cases end to end on SQLite: the real handlers, repositories, mapping and feed.</summary>
public sealed class CentralFlows : IDisposable
{
    private readonly Db _db = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    private async Task<OrgDto> CreateOrgAsync(string key = "acme") =>
        (await _db.Run<CreateOrgHandler, Result<OrgDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateOrgCommand(new CreateOrgRequest(key, "Acme", "أكمي", [new(Db.Issuer, "customer-admin")])), Ct))).Value;

    private async Task<OrgUnitDto> AddUnitAsync(Guid orgId, Guid parentId, string type, string name, TestCaller? caller = null, params string[] categories)
    {
        var result = await _db.Run<CreateUnitHandler, Result<OrgUnitDto>>(caller ?? Db.PlatformAdmin,
            h => h.Handle(new CreateUnitCommand(orgId, new CreateUnitRequest(parentId, type, name, null!, null, null, categories)), Ct));
        result.IsSuccess.ShouldBeTrue(string.Join("; ", result.Errors));
        return result.Value;
    }

    private Task<Result<FeedPage>> FeedAsync(Guid orgId, long after = 0, TestCaller? caller = null) =>
        _db.Run<ReadFeedHandler, Result<FeedPage>>(caller ?? Db.Connector, h => h.Handle(new ReadFeedQuery(orgId, after, 50), Ct));

    [Fact]
    public async Task ANewOrgHasItsRootUnitTypesAndPositions()
    {
        var org = await CreateOrgAsync();

        var units = await _db.Run<ListUnitsHandler, Result<IReadOnlyList<OrgUnitDto>>>(Db.PlatformAdmin, h => h.Handle(new ListUnitsQuery(org.Id), Ct));
        units.Value.Single().Id.ShouldBe(org.RootUnitId);
        units.Value.Single().TypeKey.ShouldBe("org");
        var positions = await _db.Run<ListPositionsHandler, Result<IReadOnlyList<PositionDto>>>(Db.PlatformAdmin, h => h.Handle(new ListPositionsQuery(org.Id), Ct));
        positions.Value.Select(p => p.Key).ShouldBe([PositionDefinition.ManagerKey, PositionDefinition.UnitAdminKey]);

        var duplicate = await _db.Run<CreateOrgHandler, Result<OrgDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateOrgCommand(new CreateOrgRequest("acme", "Again", null!)), Ct));
        duplicate.Status.ShouldBe(ResultStatus.Conflict);
    }

    [Fact]
    public async Task TheTreeFollowsTheTypeRulesAndSectionsNeedCategories()
    {
        var org = await CreateOrgAsync();
        var sector = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var department = await AddUnitAsync(org.Id, sector.Id, "department", "Sports");
        var section = await AddUnitAsync(org.Id, department.Id, "section", "Football", null, "cat-football", "cat-sport");

        section.Categories.ShouldBe(["cat-football", "cat-sport"]);
        section.Path.ShouldBe(UnitPaths.Child(department.Path, section.Id));

        await Should.ThrowAsync<DomainException>(() => AddUnitAsync(org.Id, department.Id, "section", "No categories"));
        await Should.ThrowAsync<DomainException>(() => AddUnitAsync(org.Id, org.RootUnitId, "section", "Too high", null, "c"));

        await using var context = _db.NewContext();
        (await context.Units.SingleAsync(u => u.Id == section.Id, Ct)).CategoryIds.ShouldBe(["cat-football", "cat-sport"]);
    }

    [Fact]
    public async Task MovingADepartmentRewritesItsSubtreeAndWritesOneEvent()
    {
        var org = await CreateOrgAsync();
        var news = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var sport = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "Sport");
        var department = await AddUnitAsync(org.Id, news.Id, "department", "Football");
        var section = await AddUnitAsync(org.Id, department.Id, "section", "Desk", null, "c1");
        var cursor = (await FeedAsync(org.Id)).Value.Next;

        var moved = await _db.Run<MoveUnitHandler, Result<OrgUnitDto>>(Db.PlatformAdmin,
            h => h.Handle(new MoveUnitCommand(org.Id, department.Id, new MoveUnitRequest(department.Version, sport.Id)), Ct));

        moved.Value.Path.ShouldBe(UnitPaths.Child(sport.Path, department.Id));
        var reloaded = await _db.Run<GetUnitHandler, Result<OrgUnitDto>>(Db.PlatformAdmin, h => h.Handle(new GetUnitQuery(org.Id, section.Id), Ct));
        reloaded.Value.Path.ShouldBe(UnitPaths.Child(moved.Value.Path, section.Id));

        var events = (await FeedAsync(org.Id, cursor)).Value.Entries;
        events.Single().Type.ShouldBe(OrgEvents.UnitMoved);
        var data = events.Single().Data.Deserialize<UnitMoved>(System.Text.Json.JsonSerializerOptions.Web)!;
        (data.OldPath, data.NewPath, data.NewParentId).ShouldBe((department.Path, moved.Value.Path, sport.Id));
    }

    [Fact]
    public async Task ManagersManageTheirSubtreeOnly()
    {
        var org = await CreateOrgAsync();
        var sector = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var department = await AddUnitAsync(org.Id, sector.Id, "department", "Sports");
        var manager = await AddPersonAsync(org.Id, "manager-1");
        var managerPosition = (await _db.Run<ListPositionsHandler, Result<IReadOnlyList<PositionDto>>>(Db.PlatformAdmin,
            h => h.Handle(new ListPositionsQuery(org.Id), Ct))).Value.Single(p => p.Key == PositionDefinition.ManagerKey);
        await _db.Run<CreateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateMembershipCommand(org.Id, new CreateMembershipRequest(manager.Id, department.Id, managerPosition.Id, Primary: true)), Ct));
        var asManager = Db.User("manager-1");

        var section = await AddUnitAsync(org.Id, department.Id, "section", "Desk", asManager, "c1");
        section.ParentId.ShouldBe(department.Id);

        var above = await _db.Run<CreateUnitHandler, Result<OrgUnitDto>>(asManager,
            h => h.Handle(new CreateUnitCommand(org.Id, new CreateUnitRequest(sector.Id, "department", "Other", null!)), Ct));
        above.Status.ShouldBe(ResultStatus.Forbidden);
        var stranger = await _db.Run<ListUnitsHandler, Result<IReadOnlyList<OrgUnitDto>>>(Db.User("nobody"), h => h.Handle(new ListUnitsQuery(org.Id), Ct));
        stranger.Status.ShouldBe(ResultStatus.Forbidden);
        var member = await _db.Run<ListUnitsHandler, Result<IReadOnlyList<OrgUnitDto>>>(asManager, h => h.Handle(new ListUnitsQuery(org.Id), Ct));
        member.Value.Count.ShouldBe(4);
        var customerAdmin = await _db.Run<CreateUnitHandler, Result<OrgUnitDto>>(Db.User("customer-admin"),
            h => h.Handle(new CreateUnitCommand(org.Id, new CreateUnitRequest(sector.Id, "department", "Other", null!)), Ct));
        customerAdmin.IsSuccess.ShouldBeTrue();
    }

    private async Task<PersonDto> AddPersonAsync(Guid orgId, string subject) =>
        (await _db.Run<CreatePersonHandler, Result<PersonDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreatePersonCommand(orgId, new CreatePersonRequest(Db.Issuer, subject, subject)), Ct))).Value;

    [Fact]
    public async Task ASeedBindsItsUnitAndMakesItsAdminsUnitAdmins()
    {
        var org = await CreateOrgAsync();
        var sector = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var department = await AddUnitAsync(org.Id, sector.Id, "department", "Sports");
        var admin = await AddPersonAsync(org.Id, "tenant-admin");

        var seed = await _db.Run<CreateSeedHandler, Result<TenantSeedDto>>(Db.User("customer-admin"),
            h => h.Handle(new CreateSeedCommand(org.Id, new CreateSeedRequest(sector.Id, "eu-1", "acme-news", "Acme News", ["news.acme.test"], "pg-1", "acme_news", ["content"], [admin.Id])), Ct));
        seed.IsSuccess.ShouldBeTrue(string.Join("; ", seed.Errors));
        seed.Value.Status.ShouldBe(SeedState.Requested);

        var tenant = await _db.Run<GetUnitTenantHandler, Result<UnitTenantDto>>(Db.PlatformAdmin, h => h.Handle(new GetUnitTenantQuery(org.Id, department.Id), Ct));
        (tenant.Value.SeedId, tenant.Value.OwnerUnitId, tenant.Value.TenantId).ShouldBe((seed.Value.Id, sector.Id, "acme-news"));
        var rootTenant = await _db.Run<GetUnitTenantHandler, Result<UnitTenantDto>>(Db.PlatformAdmin, h => h.Handle(new GetUnitTenantQuery(org.Id, org.RootUnitId), Ct));
        rootTenant.Value.SeedId.ShouldBeNull();

        // The seed's admin is now a unit admin of the seed's unit: they manage its subtree.
        var section = await AddUnitAsync(org.Id, department.Id, "section", "Desk", Db.User("tenant-admin"), "c1");
        section.ShouldNotBeNull();

        var pulled = await _db.Run<ListDeploymentSeedsHandler, Result<IReadOnlyList<DeploymentSeedDto>>>(Db.Connector, h => h.Handle(new ListDeploymentSeedsQuery("eu-1"), Ct));
        pulled.Value.Single().Admins.ShouldBe(["tenant-admin"]);
        pulled.Value.Single().RootUnitId.ShouldBe(sector.Id);
        var denied = await _db.Run<ListDeploymentSeedsHandler, Result<IReadOnlyList<DeploymentSeedDto>>>(Db.User("tenant-admin"), h => h.Handle(new ListDeploymentSeedsQuery("eu-1"), Ct));
        denied.Status.ShouldBe(ResultStatus.Forbidden);

        var again = await _db.Run<CreateSeedHandler, Result<TenantSeedDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateSeedCommand(org.Id, new CreateSeedRequest(sector.Id, "eu-1", "acme-2", "Again", [], "pg-1", "db2")), Ct));
        again.Status.ShouldBe(ResultStatus.Conflict);
    }

    [Fact]
    public async Task HostsReportSeedStatusPerVersion()
    {
        var org = await CreateOrgAsync();
        var sector = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var seed = (await _db.Run<CreateSeedHandler, Result<TenantSeedDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateSeedCommand(org.Id, new CreateSeedRequest(sector.Id, "eu-1", "acme-news", "Acme News", [], "pg-1", "acme_news")), Ct))).Value;

        (await _db.Run<ReportSeedHandler, Result>(Db.Connector,
            h => h.Handle(new ReportSeedCommand(seed.Id, new SeedReportRequest(1, SeedState.Adjusted, new PlacementDto("pg-2", "acme_news"))), Ct))).IsSuccess.ShouldBeTrue();
        var edited = await _db.Run<UpdateSeedHandler, Result<TenantSeedDto>>(Db.PlatformAdmin,
            h => h.Handle(new UpdateSeedCommand(org.Id, seed.Id, new UpdateSeedRequest(1, "Acme Newsroom", [], "pg-1", "acme_news")), Ct));
        edited.Value.Version.ShouldBe(2);
        edited.Value.Status.ShouldBe(SeedState.Requested);

        var forbidden = await _db.Run<ReportSeedHandler, Result>(Db.User("customer-admin"),
            h => h.Handle(new ReportSeedCommand(seed.Id, new SeedReportRequest(2, SeedState.Provisioned)), Ct));
        forbidden.Status.ShouldBe(ResultStatus.Forbidden);
        await _db.Run<ReportSeedHandler, Result>(Db.Connector, h => h.Handle(new ReportSeedCommand(seed.Id, new SeedReportRequest(2, SeedState.Provisioned)), Ct));

        var seeds = await _db.Run<ListSeedsHandler, Result<IReadOnlyList<TenantSeedDto>>>(Db.PlatformAdmin, h => h.Handle(new ListSeedsQuery(org.Id), Ct));
        var stored = seeds.Value.Single();
        (stored.Status, stored.ReportedVersion, stored.ActualPlacement).ShouldBe((SeedState.Provisioned, 2L, new PlacementDto("pg-1", "acme_news")));
        stored.History.Select(e => (e.Version, e.Status)).ShouldBe([(1L, SeedState.Requested), (1L, SeedState.Adjusted), (2L, SeedState.Requested), (2L, SeedState.Provisioned)]);
    }

    [Fact]
    public async Task TheFeedPagesInOrderPerOrg()
    {
        var acme = await CreateOrgAsync("acme");
        var globex = await CreateOrgAsync("globex");
        for (var i = 0; i < 3; i++) await AddUnitAsync(acme.Id, acme.RootUnitId, "sector", $"Sector {i}");
        await AddUnitAsync(globex.Id, globex.RootUnitId, "sector", "Other org");

        var all = (await FeedAsync(acme.Id)).Value;
        all.Entries.Select(e => e.Type).ShouldBe([OrgEvents.PositionChanged, OrgEvents.PositionChanged, OrgEvents.UnitCreated,
            OrgEvents.UnitCreated, OrgEvents.UnitCreated, OrgEvents.UnitCreated]);
        all.Entries.ShouldAllBe(e => e.OrgId == acme.Id);
        all.Entries.Select(e => e.Sequence).ShouldBeInOrder();
        all.More.ShouldBeFalse();

        var rest = (await FeedAsync(acme.Id, all.Entries[3].Sequence)).Value;
        rest.Entries.Count.ShouldBe(2);
        rest.Next.ShouldBe(all.Next);
        (await FeedAsync(acme.Id, all.Next)).Value.Entries.ShouldBeEmpty();
        (await FeedAsync(acme.Id, caller: Db.User("someone"))).Status.ShouldBe(ResultStatus.Forbidden);

        var created = all.Entries[^1].Data.Deserialize<UnitCreated>(System.Text.Json.JsonSerializerOptions.Web)!;
        created.Unit.TypeKey.ShouldBe("sector");
        all.Entries[^1].UnitPath.ShouldBe(created.Unit.Path);
    }

    [Fact]
    public async Task MembershipsCarryTheirPositionsPermissionsInTheFeed()
    {
        var org = await CreateOrgAsync();
        var sector = await AddUnitAsync(org.Id, org.RootUnitId, "sector", "News");
        var person = await AddPersonAsync(org.Id, "editor-1");
        var position = (await _db.Run<CreatePositionHandler, Result<PositionDto>>(Db.User("customer-admin"),
            h => h.Handle(new CreatePositionCommand(org.Id, new CreatePositionRequest("moderator", "Moderator", "مشرف", ["newsroom.moderate"])), Ct))).Value;
        var cursor = (await FeedAsync(org.Id)).Value.Next;

        var first = (await _db.Run<CreateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateMembershipCommand(org.Id, new CreateMembershipRequest(person.Id, sector.Id, position.Id, Primary: true)), Ct))).Value;
        var second = (await _db.Run<CreateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateMembershipCommand(org.Id, new CreateMembershipRequest(person.Id, org.RootUnitId, null, Primary: true)), Ct))).Value;

        var list = (await _db.Run<ListMembershipsHandler, Result<IReadOnlyList<MembershipDto>>>(Db.PlatformAdmin,
            h => h.Handle(new ListMembershipsQuery(org.Id, PersonId: person.Id), Ct))).Value;
        list.Single(m => m.Id == first.Id).Primary.ShouldBeFalse();
        list.Single(m => m.Id == second.Id).Primary.ShouldBeTrue();
        list.Single(m => m.Id == first.Id).PositionKey.ShouldBe("moderator");

        var events = (await FeedAsync(org.Id, cursor)).Value.Entries.Where(e => e.Type == OrgEvents.MembershipChanged)
            .Select(e => e.Data.Deserialize<MembershipChanged>(System.Text.Json.JsonSerializerOptions.Web)!).ToList();
        events[0].Permissions.ShouldBe(["newsroom.moderate"]);
        (events[0].Subject, events[0].UnitPath).ShouldBe(("editor-1", sector.Path));
        events.Count.ShouldBe(3); // first, the second, and the first losing its primary flag

        var removed = await _db.Run<RemoveUnitHandler, Result>(Db.PlatformAdmin, h => h.Handle(new RemoveUnitCommand(org.Id, sector.Id), Ct));
        removed.Status.ShouldBe(ResultStatus.Conflict);
        (await _db.Run<RemoveMembershipHandler, Result>(Db.PlatformAdmin, h => h.Handle(new RemoveMembershipCommand(org.Id, first.Id), Ct))).IsSuccess.ShouldBeTrue();
        (await _db.Run<RemoveUnitHandler, Result>(Db.PlatformAdmin, h => h.Handle(new RemoveUnitCommand(org.Id, sector.Id), Ct))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task OrgAdminsAndVisibility()
    {
        var acme = await CreateOrgAsync("acme");
        await CreateOrgAsync("globex");

        var mine = await _db.Run<ListOrgsHandler, Result<IReadOnlyList<OrgDto>>>(Db.User("customer-admin"), h => h.Handle(new ListOrgsQuery(), Ct));
        mine.Value.Count.ShouldBe(2);
        (await _db.Run<ListOrgsHandler, Result<IReadOnlyList<OrgDto>>>(Db.User("nobody"), h => h.Handle(new ListOrgsQuery(), Ct))).Value.ShouldBeEmpty();
        (await _db.Run<ListOrgsHandler, Result<IReadOnlyList<OrgDto>>>(new TestCaller(null, null), h => h.Handle(new ListOrgsQuery(), Ct))).Status.ShouldBe(ResultStatus.Unauthorized);

        var changed = await _db.Run<SetOrgAdminsHandler, Result<OrgDto>>(Db.User("customer-admin"),
            h => h.Handle(new SetOrgAdminsCommand(acme.Id, new SetOrgAdminsRequest([new(Db.Issuer, "new-admin")])), Ct));
        changed.Value.Admins.Single().Subject.ShouldBe("new-admin");
        (await _db.Run<GetOrgHandler, Result<OrgDto>>(Db.User("customer-admin"), h => h.Handle(new GetOrgQuery(acme.Id), Ct))).Status.ShouldBe(ResultStatus.Forbidden);
        (await _db.Run<GetOrgHandler, Result<OrgDto>>(Db.User("new-admin"), h => h.Handle(new GetOrgQuery(Guid.NewGuid()), Ct))).Status.ShouldBe(ResultStatus.NotFound);
    }
}
