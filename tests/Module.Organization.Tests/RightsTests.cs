using System.Security.Claims;
using static soft.square.net.Module.Organization.Tests.Samples;

namespace soft.square.net.Module.Organization.Tests;

/// <summary>
/// What the pages show buttons by: the user's rights in an org, worked out like the central service's OrgRights, and
/// the user as the central service knows them (from the sign-in's claims).
/// </summary>
public class RightsTests
{
    private const string Issuer = "http://localhost:5090/";
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ann = Guid.NewGuid();
    private static readonly Guid Manager = Guid.NewGuid();

    private static OrgDto Org(params OrgAdminDto[] admins) => new()
    {
        Id = OrgId, Key = "acme", NameEn = "Acme", NameAr = "أكمي", RootUnitId = Root, Admins = admins, Version = 1,
    };

    private static PersonDto Person(Guid id, string subject) => new()
    {
        Id = id, OrgId = OrgId, Issuer = Issuer, Subject = subject, DisplayName = subject, Version = 1,
    };

    private static PositionDto ManagerPosition => new()
    {
        Id = Manager, OrgId = OrgId, Key = "manager", NameEn = "Manager", NameAr = "مدير",
        Permissions = [OrgPermissionNames.UnitsManage, "shifts.manage"], Version = 1,
    };

    private static MembershipDto Membership(Guid unit, Guid? position, DateTimeOffset from, DateTimeOffset? to = null) => new()
    {
        Id = Guid.NewGuid(), OrgId = OrgId, PersonId = Ann, UnitId = unit, PositionId = position, From = from, To = to,
    };

    private static OrgRightsView RightsOf(OrgCaller caller, OrgDto org, params MembershipDto[] memberships) =>
        OrgRightsView.For(caller, org, [Person(Ann, "ann")], memberships, [ManagerPosition], Units(), Now);

    private static string PathOf(Guid id) => Units().Single(u => u.Id == id).Path;

    [Fact]
    public void A_manager_of_a_department_manages_it_and_everything_below_it_only()
    {
        var rights = RightsOf(new OrgCaller(Issuer, "ann", false), Org(), Membership(Politics, Manager, Now.AddDays(-1)));

        rights.Administers.ShouldBeFalse();
        rights.Person!.Id.ShouldBe(Ann);
        rights.Can(OrgPermissionNames.UnitsManage, PathOf(Politics)).ShouldBeTrue();
        rights.Can(OrgPermissionNames.UnitsManage, PathOf(Local)).ShouldBeTrue();
        rights.Can(OrgPermissionNames.UnitsManage, PathOf(Sports)).ShouldBeFalse();
        rights.Can(OrgPermissionNames.UnitsManage, PathOf(News)).ShouldBeFalse();
        rights.Can(OrgPermissionNames.MembersManage, PathOf(Local)).ShouldBeFalse();
        rights.CanSomewhere(OrgPermissionNames.UnitsManage).ShouldBeTrue();
        rights.CanSomewhere(OrgPermissionNames.SeedsManage).ShouldBeFalse();
    }

    [Fact]
    public void Memberships_that_ended_have_not_started_or_have_no_position_give_nothing()
    {
        var rights = RightsOf(new OrgCaller(Issuer, "ann", false), Org(),
            Membership(Politics, Manager, Now.AddDays(-10), Now.AddDays(-1)),
            Membership(Sports, Manager, Now.AddDays(1)),
            Membership(News, null, Now.AddDays(-1)));

        rights.CanSomewhere(OrgPermissionNames.UnitsManage).ShouldBeFalse();
    }

    [Fact]
    public void Customer_admins_and_platform_admins_may_do_everything()
    {
        RightsOf(new OrgCaller(Issuer, "boss", false), Org(new OrgAdminDto { Issuer = "http://localhost:5090", Subject = "boss" }))
            .Can(OrgPermissionNames.SeedsManage, PathOf(Local)).ShouldBeTrue();
        RightsOf(new OrgCaller(Issuer, "root", true), Org()).Administers.ShouldBeTrue();
    }

    [Fact]
    public void An_admin_of_another_issuer_is_not_this_user()
    {
        RightsOf(new OrgCaller(Issuer, "boss", false), Org(new OrgAdminDto { Issuer = "https://elsewhere/", Subject = "boss" }))
            .Administers.ShouldBeFalse();
    }

    [Fact]
    public void Someone_outside_the_org_gets_nothing()
    {
        var rights = RightsOf(new OrgCaller(Issuer, "stranger", false), Org());

        rights.Person.ShouldBeNull();
        rights.CanSomewhere(OrgPermissionNames.UnitsManage).ShouldBeFalse();
        OrgRightsView.None.Administers.ShouldBeFalse();
    }

    [Fact]
    public void The_caller_comes_from_sub_iss_and_the_platform_admin_role()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "u1"), new Claim("iss", "http://id/"), new Claim("role", "platform-admin")], "oidc"));

        OrganizationPageBase.CallerOf(user, "http://other/").ShouldBe(new OrgCaller("http://id/", "u1", true));
    }

    [Fact]
    public void Without_an_iss_claim_the_configured_authority_is_the_issuer()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1"), new Claim("role", "editor")], "oidc"));

        OrganizationPageBase.CallerOf(user, "http://localhost:5090/").ShouldBe(new OrgCaller("http://localhost:5090/", "u1", false));
    }

    [Fact]
    public void Signed_out_is_anonymous()
    {
        OrganizationPageBase.CallerOf(new ClaimsPrincipal(new ClaimsIdentity()), "http://id/").ShouldBe(OrgCaller.Anonymous);
        OrgCaller.Anonymous.Is("http://id/", "u1").ShouldBeFalse();
    }
}
