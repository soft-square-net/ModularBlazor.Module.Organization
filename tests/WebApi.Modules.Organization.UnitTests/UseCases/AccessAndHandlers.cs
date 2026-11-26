using Ardalis.Specification;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;
using soft.square.net.WebApi.Modules.Organization.UnitTests.Core;
using soft.square.net.WebApi.Modules.Organization.UseCases;
using soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;
using soft.square.net.WebApi.Modules.Organization.UseCases.Units;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.UseCases;

/// <summary>The central service's authorization (own data, down the tree) and the unit use cases over substitutes.</summary>
public class AccessAndHandlers
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly TreeBuilder _tree = new();
    private readonly Org _org;
    private readonly IOrgAccess _access = Substitute.For<IOrgAccess>();
    private readonly IReadRepository<OrgUnit> _units = Substitute.For<IReadRepository<OrgUnit>>();
    private readonly IReadRepository<UnitType> _types = Substitute.For<IReadRepository<UnitType>>();
    private readonly IReadRepository<Membership> _memberships = Substitute.For<IReadRepository<Membership>>();
    private readonly IOrgStore _store = Substitute.For<IOrgStore>();
    private readonly OrgFeed _feed;
    private readonly TestClock _clock = new();

    public AccessAndHandlers()
    {
        _org = Org.Create(_tree.OrgId, _tree.Root.Id, "acme", "Acme", null, null, TreeBuilder.Now);
        _feed = new OrgFeed(_store, _clock);
        _access.IsSignedIn.Returns(true);
        _types.ListAsync(Arg.Any<ISpecification<UnitType>>(), Arg.Any<CancellationToken>()).Returns([.. _tree.Types]);
    }

    private void Rights(bool administers = false, params (string Path, string[] Permissions)[] grants) =>
        _access.ForOrgAsync(_tree.OrgId, Arg.Any<CancellationToken>())
            .Returns(Result<OrgRights>.Success(new OrgRights(_org, administers, true, false, [.. grants.Select(g => (g.Path, (IReadOnlyList<string>)g.Permissions))])));

    private void Unit(OrgUnit unit) =>
        _units.FirstOrDefaultAsync(Arg.Is<ISpecification<OrgUnit>>(s => s is UnitByIdSpec), Arg.Any<CancellationToken>())
            .Returns(call => ((UnitByIdSpec)call.Arg<ISpecification<OrgUnit>>()).Evaluate([_tree.Root, .. _known]).FirstOrDefault());

    private readonly List<OrgUnit> _known = [];

    [Fact]
    public void RightsOnAUnitCoverItsSubtree()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        var section = _tree.Section(department);
        var rights = new OrgRights(_org, false, true, false, [(department.Path, ["shifts.manage", OrgPermissions.UnitsManage])]);

        rights.Can("shifts.manage", section.Path).ShouldBeTrue();
        rights.Can("shifts.manage", department.Path).ShouldBeTrue();
        rights.Can("shifts.manage", sector.Path).ShouldBeFalse();
        rights.Can(OrgPermissions.MembersManage, section.Path).ShouldBeFalse();
        rights.CanSomewhere(OrgPermissions.UnitsManage).ShouldBeTrue();
        rights.CanRead.ShouldBeTrue();
        new OrgRights(_org, true, false, false, []).Can(OrgPermissions.MembersManage, section.Path).ShouldBeTrue();
        new OrgRights(_org, false, false, false, []).CanRead.ShouldBeFalse();
    }

    [Fact]
    public async Task OnlyPlatformAdminsCreateOrgs()
    {
        var caller = Substitute.For<ICaller>();
        _access.Caller.Returns(caller);

        var result = await new CreateOrgHandler(_access, Substitute.For<IReadRepository<Org>>(), _store, _feed, _clock)
            .Handle(new CreateOrgCommand(new CreateOrgRequest("acme", "Acme", null)), Ct);

        result.Status.ShouldBe(ResultStatus.Forbidden);
        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreatingAnOrgSeedsItsTreeTypesAndPositions()
    {
        var caller = Substitute.For<ICaller>();
        caller.IsPlatformAdmin.Returns(true);
        _access.Caller.Returns(caller);
        var added = new List<object>();
        _store.When(s => s.Add(Arg.Any<IAggregateRoot>())).Do(call => added.Add(call[0]));

        var result = await new CreateOrgHandler(_access, Substitute.For<IReadRepository<Org>>(), _store, _feed, _clock)
            .Handle(new CreateOrgCommand(new CreateOrgRequest("acme", "Acme", "أكمي", [new("https://id", "admin-1")])), Ct);

        result.IsSuccess.ShouldBeTrue();
        added.OfType<UnitType>().Select(t => t.Key).ShouldBe([UnitType.OrgKey, UnitType.SectorKey, UnitType.DepartmentKey, UnitType.SectionKey]);
        added.OfType<OrgUnit>().Single().Id.ShouldBe(result.Value.RootUnitId);
        added.OfType<FeedEntry>().Select(e => e.Type).ShouldContain(OrgEvents.UnitCreated);
        result.Value.Admins.Single().Subject.ShouldBe("admin-1");
        await _store.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AManagerOfADepartmentAddsSectionsBelowIt()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        _known.AddRange([sector, department]);
        Unit(department);
        Rights(grants: (department.Path, [OrgPermissions.UnitsManage]));

        var result = await new CreateUnitHandler(_access, _units, _types, _store, _feed, _clock)
            .Handle(new CreateUnitCommand(_tree.OrgId, new CreateUnitRequest(department.Id, "section", "Desk", null, null, null, ["cat-1"])), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ParentId.ShouldBe(department.Id);
        result.Value.Categories.ShouldBe(["cat-1"]);
        _store.Received().Add(Arg.Is<FeedEntry>(e => e.Type == OrgEvents.UnitCreated && e.UnitPath == result.Value.Path));
    }

    [Fact]
    public async Task AManagerCannotAddUnitsAboveTheirUnit()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        _known.AddRange([sector, department]);
        Unit(sector);
        Rights(grants: (department.Path, [OrgPermissions.UnitsManage]));

        var result = await new CreateUnitHandler(_access, _units, _types, _store, _feed, _clock)
            .Handle(new CreateUnitCommand(_tree.OrgId, new CreateUnitRequest(sector.Id, "department", "Other", null)), Ct);

        result.Status.ShouldBe(ResultStatus.Forbidden);
        await _store.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStaleUnitEditIsAConflict()
    {
        var sector = _tree.Sector();
        _known.Add(sector);
        Unit(sector);
        Rights(administers: true);

        var result = await new UpdateUnitHandler(_access, _units, _types, _store, _feed, _clock)
            .Handle(new UpdateUnitCommand(_tree.OrgId, sector.Id, new UpdateUnitRequest(Version: 3, "News", null)), Ct);

        result.Status.ShouldBe(ResultStatus.Conflict);
    }

    [Fact]
    public async Task AUnitWithChildrenIsNotRemoved()
    {
        var sector = _tree.Sector();
        _known.Add(sector);
        Unit(sector);
        Rights(administers: true);
        _units.AnyAsync(Arg.Any<ChildUnitsSpec>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await new RemoveUnitHandler(_access, _units, _memberships, _store, _feed).Handle(new RemoveUnitCommand(_tree.OrgId, sector.Id), Ct);

        result.Status.ShouldBe(ResultStatus.Conflict);
        _store.DidNotReceive().Remove(Arg.Any<OrgUnit>());
    }
}
