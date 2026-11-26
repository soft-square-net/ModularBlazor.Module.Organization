using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.Organization.UseCases;
using soft.square.net.WebApi.Modules.Organization.UseCases.Connector;
using soft.square.net.WebApi.Modules.Organization.UseCases.Local;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.UseCases;

public sealed class FeedWindowTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(1);

    [Fact]
    public void AnEntryBelowAHigherOneIsStillNewUntilTheCursorPassesIt()
    {
        var window = new FeedWindow();
        window.MarkApplied(1, T0);
        window.MarkApplied(2, T0);
        window.MarkApplied(4, T0); // 3 has its id, but has not committed yet
        window.Advance(T0.AddSeconds(10), Overlap);

        window.Cursor.ShouldBe(0); // nothing is older than the overlap: everything is read again next time
        window.IsNew(4).ShouldBeFalse();
        window.IsNew(3).ShouldBeTrue();

        window.MarkApplied(3, T0); // committed late; read on the next pass
        window.Advance(T0.AddMinutes(2), Overlap);

        window.Cursor.ShouldBe(4);
        window.Applied.ShouldBeEmpty();
        window.IsNew(3).ShouldBeFalse();
        window.IsNew(5).ShouldBeTrue();
    }

    [Fact]
    public void TheCursorPassesOnlyTheEntriesOlderThanTheOverlap()
    {
        var window = new FeedWindow(10);
        window.MarkApplied(11, T0);
        window.MarkApplied(12, T0.AddMinutes(5));
        window.Advance(T0.AddMinutes(5).AddSeconds(30), Overlap);

        window.Cursor.ShouldBe(11);
        window.Applied.ShouldBe([new AppliedEntry(12, T0.AddMinutes(5))]);
    }

    [Fact]
    public void ItRoundTripsThroughItsStoredForm()
    {
        var window = new FeedWindow(3);
        window.MarkApplied(5, T0);
        window.MarkApplied(2, T0); // at or below the cursor: already settled
        window.MarkApplied(7, T0.AddSeconds(1));

        var again = FeedWindow.Deserialize(window.Cursor, window.Serialize());

        again.Cursor.ShouldBe(3);
        again.Applied.ShouldBe([new AppliedEntry(5, T0), new AppliedEntry(7, T0.AddSeconds(1))]);
        FeedWindow.Deserialize(9, "").Cursor.ShouldBe(9);
    }

    [Fact]
    public void WithoutAnOverlapTheCursorFollowsTheHighestEntry()
    {
        var window = new FeedWindow();
        window.MarkApplied(1, T0);
        window.MarkApplied(3, T0);
        window.Advance(T0, TimeSpan.Zero);
        window.Cursor.ShouldBe(3);
    }
}

public sealed class OrgGrantsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Unit = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    [Fact]
    public void AMembershipGivesItsPositionsPermissionsOnTheUnitBetweenItsDates()
    {
        var grants = OrgGrants.For(Unit,
        [
            new("bob", ["shifts.manage", "org.units.manage", "shifts.manage"], Now.AddDays(-1), Now.AddDays(30)),
            new("ended", ["shifts.manage"], Now.AddDays(-10), Now.AddDays(-1)),
            new("", ["shifts.manage"], Now, null)
        ], Now);

        grants.Select(g => (g.UserId, g.Permission, g.Scope, g.ValidFrom, g.ValidTo)).ShouldBe(
        [
            ("bob", "org.read", UnitScopes.Of(Unit), Now.AddDays(-1), Now.AddDays(30)),
            ("bob", "shifts.manage", UnitScopes.Of(Unit), Now.AddDays(-1), Now.AddDays(30)),
            ("bob", "org.units.manage", UnitScopes.Of(Unit), Now.AddDays(-1), Now.AddDays(30))
        ]);
    }

    [Fact]
    public void EveryPositionGivesOrgReadOnItsUnitButAMembershipWithoutPermissionsGivesNothing()
    {
        var grants = OrgGrants.For(Unit,
        [
            new("carol", ["org.read", "shifts.manage"], Now, null),
            new("dave", [], Now, null)
        ], Now);

        grants.Select(g => (g.UserId, g.Permission)).ShouldBe([("carol", "org.read"), ("carol", "shifts.manage")]);
    }

    [Fact]
    public void PlatformPermissionsOtherThanGrantsManageAreLeftOut()
    {
        var grants = OrgGrants.For(Unit, [new("alice", [PlatformPermissions.ModulesManage, PlatformPermissions.GrantsManage], Now, null)], Now);
        grants.Select(g => g.Permission).ShouldBe(["org.read", PlatformPermissions.GrantsManage]);
    }

    [Fact]
    public void SourcesNameTheirUnit()
    {
        OrgGrants.Source(Unit).ShouldBe($"org:{Unit:N}");
        OrgGrants.UnitOf(OrgGrants.Source(Unit)).ShouldBe(Unit);
        OrgGrants.UnitOf("shift:42").ShouldBeNull();
        OrgGrants.UnitOf("org:not-a-guid").ShouldBeNull();
    }
}

public sealed class SeedMappingTests
{
    private static readonly DeploymentSeedDto Seed = new("seed-1", 3, "eu-1", "acme-news", "Acme News", ["news.acme.test"], "pg-1", "acme_news",
        ["content"], ["alice"], Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void ADeploymentSeedIsThePlatformsSeedWithItsOrgAndUnitInExtra()
    {
        var seed = SeedMapping.ToPlatform(Seed);

        (seed.Id, seed.Version, seed.DeploymentId, seed.TenantId, seed.Name).ShouldBe(("seed-1", 3L, "eu-1", "acme-news", "Acme News"));
        seed.RequestedPlacement.ShouldBe(new TenantPlacement("pg-1", "acme_news"));
        seed.Admins.ShouldBe(["alice"]);
        seed.Extra.GetProperty("orgId").GetGuid().ShouldBe(Seed.OrgId);
        seed.Extra.GetProperty("rootUnitId").GetGuid().ShouldBe(Seed.RootUnitId);
    }

    [Fact]
    public void EveryPlatformStatusHasTheSameState()
    {
        foreach (var status in Enum.GetValues<SeedStatus>())
            SeedMapping.ToState(status).ToString().ShouldBe(status.ToString());
        SeedMapping.ToReport(3, SeedStatus.Adjusted, new TenantPlacement("pg-2", "db"), null)
            .ShouldBe(new SeedReportRequest(3, SeedState.Adjusted, new PlacementDto("pg-2", "db"), null));
    }

    [Fact]
    public void OnlyNewSeedsAndHigherVersionsAreChanges()
    {
        var known = new Dictionary<string, long> { ["seed-1"] = 3, ["seed-2"] = 1 };
        var changed = SeedMapping.Changed(known, [Seed, Seed with { Id = "seed-2", Version = 2 }, Seed with { Id = "seed-3", Version = 1 }]);
        changed.Select(s => s.Id).ShouldBe(["seed-2", "seed-3"]);
    }
}

public sealed class LocalQueryTests
{
    private static readonly Guid UnitId = Guid.NewGuid();
    private static readonly DirectoryUnitDto Unit = new(UnitId, Guid.NewGuid(), null, "section", $"/{Guid.NewGuid():N}/{UnitId:N}", "Desk", "مكتب", [], null,
        ["c1"], TenantBindingKind.Inherit, null, 1, true);

    [Fact]
    public async Task MembersNeedOrgReadOnTheUnitOrAnAncestor()
    {
        var directory = Substitute.For<IUnitDirectory>();
        directory.GetAsync(UnitId, Arg.Any<CancellationToken>()).Returns(Unit);
        directory.MembersOfAsync(UnitId, Arg.Any<CancellationToken>()).Returns([]);
        var access = Substitute.For<IAccessScopes>();
        var handler = new GetLocalMembersHandler(directory, access);

        (await handler.Handle(new GetLocalMembersQuery(UnitId), CancellationToken.None)).Status.ShouldBe(ResultStatus.Forbidden);

        access.HasPermissionAsync(OrganizationPermissions.Read, Arg.Is<IReadOnlyCollection<string>>(s => s.SequenceEqual(UnitScopes.For(Unit.Path))), Arg.Any<CancellationToken>())
            .Returns(true);
        (await handler.Handle(new GetLocalMembersQuery(UnitId), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await handler.Handle(new GetLocalMembersQuery(Guid.NewGuid()), CancellationToken.None)).Status.ShouldBe(ResultStatus.NotFound);
    }
}
