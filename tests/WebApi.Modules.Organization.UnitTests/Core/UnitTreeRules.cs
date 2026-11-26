using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.Core;

/// <summary>The unit tree: types under their allowed parents, paths, moves, Section categories and tenant bindings.</summary>
public class UnitTreeRules
{
    private readonly TreeBuilder _tree = new();
    private static readonly DateTimeOffset Later = TreeBuilder.Now.AddHours(1);

    [Fact]
    public void DefaultTypesAreOrgSectorDepartmentSection()
    {
        _tree.Types.Select(t => t.Key).ShouldBe([UnitType.OrgKey, UnitType.SectorKey, UnitType.DepartmentKey, UnitType.SectionKey]);
        _tree.OrgType.IsRoot.ShouldBeTrue();
        _tree.SectionType.HoldsCategories.ShouldBeTrue();
        _tree.Types.Where(t => t.Key != UnitType.SectionKey).ShouldAllBe(t => !t.HoldsCategories);
        _tree.SectionType.AllowsUnder(_tree.DepartmentType).ShouldBeTrue();
        _tree.SectionType.AllowsUnder(_tree.SectorType).ShouldBeFalse();
    }

    [Fact]
    public void ChildPathsExtendTheParentPath()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        var section = _tree.Section(department);

        _tree.Root.Path.ShouldBe(UnitPaths.Root(_tree.Root.Id));
        section.Path.ShouldBe($"/{UnitPaths.Id(_tree.Root.Id)}/{UnitPaths.Id(sector.Id)}/{UnitPaths.Id(department.Id)}/{UnitPaths.Id(section.Id)}");
        section.Depth.ShouldBe(3);
        section.ParentId.ShouldBe(department.Id);
        UnitScopes.For(section.Path).ShouldBe([UnitScopes.Of(_tree.Root.Id), UnitScopes.Of(sector.Id), UnitScopes.Of(department.Id), UnitScopes.Of(section.Id)]);
    }

    [Fact]
    public void AUnitTypeMustBeAllowedUnderItsParentsType()
    {
        var sector = _tree.Sector();

        Should.Throw<DomainException>(() => OrgUnit.CreateChild(sector, _tree.SectorType, _tree.SectionType, "Desk", null, null, null, ["c"], TreeBuilder.Now))
            .Message.ShouldContain("cannot sit under");
        Should.Throw<DomainException>(() => OrgUnit.CreateChild(_tree.Root, _tree.OrgType, _tree.DepartmentType, "Dept", null, null, null, null, TreeBuilder.Now));
    }

    [Fact]
    public void OnlyTheRootTypeMakesARootUnit() =>
        Should.Throw<DomainException>(() => OrgUnit.CreateRoot(Guid.NewGuid(), _tree.OrgId, _tree.SectorType, "x", null, TreeBuilder.Now));

    [Fact]
    public void ASectionWithoutCategoriesIsRejected()
    {
        var department = _tree.Department(_tree.Sector());

        Should.Throw<DomainException>(() => OrgUnit.CreateChild(department, _tree.DepartmentType, _tree.SectionType, "Desk", null, null, null, [], TreeBuilder.Now))
            .Message.ShouldContain("at least one post category");
        var section = _tree.Section(department, "Desk", "c1", "c2");
        Should.Throw<DomainException>(() => section.Update(_tree.SectionType, "Desk", null, null, null, [" "], Later));
        section.CategoryIds.ShouldBe(["c1", "c2"]);
    }

    [Fact]
    public void OtherUnitsHoldNoCategories() =>
        Should.Throw<DomainException>(() => OrgUnit.CreateChild(_tree.Root, _tree.OrgType, _tree.SectorType, "News", null, null, null, ["c1"], TreeBuilder.Now))
            .Message.ShouldContain("does not hold post categories");

    [Fact]
    public void UpdateReplacesCategoriesAndBumpsTheVersion()
    {
        var section = _tree.Section(_tree.Department(_tree.Sector()), "Desk", "c1", "c2");

        section.Update(_tree.SectionType, "Desk 2", "ديسك", ["SEO", "seo"], "Asia/Riyadh", ["c2", "c3", "c3"], Later);

        section.CategoryIds.ShouldBe(["c2", "c3"]);
        section.Tags.ShouldBe(["seo"]);
        section.NameAr.ShouldBe("ديسك");
        section.Version.ShouldBe(1);
        section.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void AMissingNameCopiesTheOtherOne()
    {
        var sector = OrgUnit.CreateChild(_tree.Root, _tree.OrgType, _tree.SectorType, null, "الأخبار", null, null, null, TreeBuilder.Now);
        sector.NameEn.ShouldBe("الأخبار");
        Should.Throw<DomainException>(() => OrgUnit.CreateChild(_tree.Root, _tree.OrgType, _tree.SectorType, " ", null, null, null, null, TreeBuilder.Now));
    }

    [Fact]
    public void AnUnknownTimeZoneIsRejected() =>
        Should.Throw<DomainException>(() => OrgUnit.CreateChild(_tree.Root, _tree.OrgType, _tree.SectorType, "News", null, null, "Mars/Olympus", null, TreeBuilder.Now));

    [Fact]
    public void MovingAUnitRewritesItsSubtree()
    {
        var news = _tree.Sector("News");
        var sport = _tree.Sector("Sport");
        var department = _tree.Department(news);
        var section = _tree.Section(department);
        var oldSectionPath = section.Path;

        var oldPath = department.MoveUnder(sport, _tree.SectorType, _tree.DepartmentType, [section], Later);

        oldPath.ShouldBe(UnitPaths.Child(news.Path, department.Id));
        department.ParentId.ShouldBe(sport.Id);
        department.Path.ShouldBe(UnitPaths.Child(sport.Path, department.Id));
        section.Path.ShouldBe(UnitPaths.Child(department.Path, section.Id));
        section.Path.ShouldBe(UnitPaths.Rebase(oldSectionPath, oldPath, department.Path));
        section.Depth.ShouldBe(3);
        (department.Version, section.Version).ShouldBe((1, 1));
    }

    [Fact]
    public void AUnitCannotMoveUnderItselfOrItsDescendants()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        var other = _tree.Department(sector, "Other");

        Should.Throw<DomainException>(() => department.MoveUnder(department, _tree.DepartmentType, _tree.DepartmentType, [], Later));
        Should.Throw<DomainException>(() => sector.MoveUnder(department, _tree.DepartmentType, _tree.SectorType, [department, other], Later));
    }

    [Fact]
    public void AMoveMustKeepTheTypeRules()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        var section = _tree.Section(department);

        Should.Throw<DomainException>(() => section.MoveUnder(sector, _tree.SectorType, _tree.SectionType, [], Later)).Message.ShouldContain("cannot sit under");
        Should.Throw<DomainException>(() => _tree.Root.MoveUnder(sector, _tree.SectorType, _tree.OrgType, [], Later)).Message.ShouldContain("root");
    }

    [Fact]
    public void AnInheritingUnitLivesInTheNearestAncestorsTenant()
    {
        var sector = _tree.Sector();
        var department = _tree.Department(sector);
        var section = _tree.Section(department);
        var ancestors = new[] { _tree.Root, sector, department };

        UnitTenancy.Owner(section, ancestors).ShouldBeNull();

        _tree.Root.BindOwnTenant("seed-root", Later);
        sector.BindOwnTenant("seed-news", Later);
        UnitTenancy.Owner(section, ancestors).ShouldBe(sector);
        UnitTenancy.Owner(sector, [_tree.Root]).ShouldBe(sector);

        sector.InheritTenant(Later);
        UnitTenancy.Owner(section, ancestors).ShouldBe(_tree.Root);
        UnitTenancy.AncestorIds(section).ShouldBe([_tree.Root.Id, sector.Id, department.Id]);
    }

    [Fact]
    public void AUnitOwnsOneTenant()
    {
        var sector = _tree.Sector();
        sector.BindOwnTenant("seed-1", Later);
        Should.Throw<DomainException>(() => sector.BindOwnTenant("seed-2", Later));
        sector.Binding.ShouldBe(TenantBindingKind.OwnTenant);
    }

    [Fact]
    public void ANewTypeNeedsKnownParents()
    {
        var keys = _tree.Types.Select(t => t.Key).ToArray();

        var desk = UnitType.Create(_tree.OrgId, "desk", "Desk", null, ["department", "desk"], true, 4, keys);
        desk.AllowsUnder(_tree.DepartmentType).ShouldBeTrue();

        Should.Throw<DomainException>(() => UnitType.Create(_tree.OrgId, "team", "Team", null, ["squad"], false, 5, keys));
        Should.Throw<DomainException>(() => UnitType.Create(_tree.OrgId, "section", "Section", null, ["department"], true, 5, keys));
        Should.Throw<DomainException>(() => UnitType.Create(_tree.OrgId, "other-root", "Root", null, [], false, 5, keys));
    }
}
