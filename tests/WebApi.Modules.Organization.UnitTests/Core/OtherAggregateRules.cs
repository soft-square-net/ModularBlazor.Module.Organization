using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.Core;

/// <summary>Orgs, persons, memberships, positions, tenant seeds and links.</summary>
public class OtherAggregateRules
{
    private static readonly DateTimeOffset Now = TreeBuilder.Now;
    private readonly TreeBuilder _tree = new();

    [Fact]
    public void AnOrgKnowsItsAdmins()
    {
        var org = Org.Create(_tree.OrgId, _tree.Root.Id, "acme", "Acme", null, [("https://id", "u1"), ("https://id", "u1")], Now);

        org.Admins.Count.ShouldBe(1);
        org.IsAdmin("https://id", "u1").ShouldBeTrue();
        org.IsAdmin("https://other", "u1").ShouldBeFalse();
        Should.Throw<DomainException>(() => Org.Create(Guid.NewGuid(), Guid.NewGuid(), "Not A Key", "x", null, null, Now));
    }

    [Fact]
    public void APersonMayHaveAnEmployeeProfile()
    {
        var member = Person.Create(_tree.OrgId, "https://id", "u1", "Mona", null, null, Now);
        member.Employee.ShouldBeNull();

        member.Update("Mona A.", "mona@example.com", new EmployeeProfile("E-7", "Editor", new DateOnly(2020, 1, 1)), Now);
        member.IsEmployee.ShouldBeTrue();
        member.Employee!.JobTitle.ShouldBe("Editor");
        Should.Throw<DomainException>(() => member.Update("Mona", "not-an-email", null, Now));
        Should.Throw<DomainException>(() => Person.Create(_tree.OrgId, "", "u1", "x", null, null, Now));
    }

    [Fact]
    public void AMembershipEndsAfterItStarts()
    {
        var person = Person.Create(_tree.OrgId, "https://id", "u1", "Mona", null, null, Now);
        var manager = PositionDefinition.Defaults(_tree.OrgId, Now).Single(p => p.Key == PositionDefinition.ManagerKey);

        var membership = Membership.Create(person, _tree.Root, manager, Now, Now.AddDays(30), true, Now);
        membership.IsActiveAt(Now.AddDays(1)).ShouldBeTrue();
        membership.IsActiveAt(Now.AddDays(30)).ShouldBeFalse();
        Should.Throw<DomainException>(() => membership.Change(manager, Now, Now, false, Now));

        var otherOrg = Person.Create(Guid.NewGuid(), "https://id", "u1", "Mona", null, null, Now);
        Should.Throw<DomainException>(() => Membership.Create(otherOrg, _tree.Root, null, Now, null, false, Now));
    }

    [Fact]
    public void PositionsNamePermissions()
    {
        var defaults = PositionDefinition.Defaults(_tree.OrgId, Now);
        defaults.Single(p => p.Key == PositionDefinition.ManagerKey).Permissions.ShouldBe(["org.units.manage", "shifts.manage"]);
        defaults.Single(p => p.Key == PositionDefinition.UnitAdminKey).Gives("org.members.manage").ShouldBeTrue();

        Should.Throw<DomainException>(() => PositionDefinition.Create(_tree.OrgId, "x", "X", null, ["Shifts Manage"], Now));
        Should.Throw<DomainException>(() => PositionDefinition.Create(_tree.OrgId, "x", "X", null, ["shifts"], Now));
    }

    private TenantSeed Seed() =>
        TenantSeed.Create(_tree.OrgId, _tree.Root.Id, "eu-1", "acme", "Acme", ["acme.example.com"], "pg-1", "acme_db", ["content"], [], Now);

    [Fact]
    public void ASeedStartsRequestedAtVersionOne()
    {
        var seed = Seed();

        seed.Version.ShouldBe(1);
        seed.Status.ShouldBe(SeedState.Requested);
        seed.History.Single().Status.ShouldBe(SeedState.Requested);
        Should.Throw<DomainException>(() => TenantSeed.Create(_tree.OrgId, _tree.Root.Id, "eu-1", "Acme Corp", "Acme", [], "pg", "db", null, null, Now));
    }

    [Fact]
    public void EditingASeedIsANewRequestedVersion()
    {
        var seed = Seed();
        seed.Report(1, SeedState.Provisioned, null, null, null, Now);

        seed.Edit(1, "Acme Media", ["acme.example.com"], "pg-1", "acme_db", null, null, Now);

        seed.Version.ShouldBe(2);
        seed.Status.ShouldBe(SeedState.Requested);
        Should.Throw<DomainException>(() => seed.Edit(1, "Stale", [], "pg", "db", null, null, Now)).Message.ShouldContain("version 2");
    }

    [Fact]
    public void ReportsFollowTheCurrentVersion()
    {
        var seed = Seed();
        seed.Edit(1, "Acme", [], "pg-1", "acme_db", null, null, Now);

        seed.Report(1, SeedState.Provisioned, null, null, null, Now);  // a late report on the old version: history only
        seed.Status.ShouldBe(SeedState.Requested);
        seed.ReportedVersion.ShouldBeNull();

        seed.Report(2, SeedState.Adjusted, "pg-2", "acme", null, Now);
        seed.Status.ShouldBe(SeedState.Adjusted);
        (seed.ActualDatabaseServer, seed.ActualDatabaseName).ShouldBe(("pg-2", "acme"));
        seed.ReportedVersion.ShouldBe(2);

        Should.Throw<DomainException>(() => seed.Report(3, SeedState.Provisioned, null, null, null, Now));
        Should.Throw<DomainException>(() => seed.Report(2, SeedState.Rejected, null, null, null, Now)).Message.ShouldContain("reason");
        Should.Throw<DomainException>(() => seed.Report(2, SeedState.Adjusted, null, null, null, Now));
        Should.Throw<DomainException>(() => seed.Report(2, SeedState.Requested, null, null, null, Now));
    }

    [Fact]
    public void TheHistoryKeepsTheLatestEntries()
    {
        var seed = Seed();
        for (var i = 0; i < TenantSeed.HistoryLimit + 10; i++) seed.Report(1, SeedState.Provisioning, null, null, null, Now.AddMinutes(i));

        seed.History.Count.ShouldBe(TenantSeed.HistoryLimit);
        seed.History[^1].At.ShouldBe(Now.AddMinutes(TenantSeed.HistoryLimit + 9));
    }

    [Fact]
    public void LinksMoveThroughTheirStates()
    {
        var link = TenantLink.Request(_tree.OrgId, "seed-a", Guid.NewGuid(), "seed-b", Guid.NewGuid(), Now);
        link.State.ShouldBe(TenantLinkState.Requested);
        Should.Throw<DomainException>(() => link.Revoke(Now));

        link.Accept(Now);
        link.State.ShouldBe(TenantLinkState.Active);
        Should.Throw<DomainException>(() => link.Decline(Now));

        link.Revoke(Now);
        link.State.ShouldBe(TenantLinkState.Revoked);

        var declined = TenantLink.Request(_tree.OrgId, "seed-a", Guid.NewGuid(), "seed-b", Guid.NewGuid(), Now);
        declined.Decline(Now);
        declined.State.ShouldBe(TenantLinkState.Declined);
        Should.Throw<DomainException>(() => TenantLink.Request(_tree.OrgId, "seed-a", Guid.NewGuid(), "seed-a", Guid.NewGuid(), Now));
    }
}
