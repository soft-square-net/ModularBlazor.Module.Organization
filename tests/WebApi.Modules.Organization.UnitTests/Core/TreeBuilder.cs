using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.Core;

/// <summary>An org's default types and a small tree: Org &gt; Sector &gt; Department &gt; Section.</summary>
public sealed class TreeBuilder
{
    public static readonly DateTimeOffset Now = TestClock.Start;

    public Guid OrgId { get; } = Guid.CreateVersion7();
    public IReadOnlyList<UnitType> Types { get; }
    public UnitType OrgType => Type(UnitType.OrgKey);
    public UnitType SectorType => Type(UnitType.SectorKey);
    public UnitType DepartmentType => Type(UnitType.DepartmentKey);
    public UnitType SectionType => Type(UnitType.SectionKey);

    public OrgUnit Root { get; }

    public TreeBuilder()
    {
        Types = UnitType.Defaults(OrgId);
        Root = OrgUnit.CreateRoot(Guid.CreateVersion7(), OrgId, OrgType, "Acme", "أكمي", Now);
    }

    public UnitType Type(string key) => Types.Single(t => t.Key == key);

    public OrgUnit Sector(string name = "News") => OrgUnit.CreateChild(Root, OrgType, SectorType, name, null, null, null, null, Now);

    public OrgUnit Department(OrgUnit sector, string name = "Sports") =>
        OrgUnit.CreateChild(sector, SectorType, DepartmentType, name, null, ["desk"], "Africa/Cairo", null, Now);

    public OrgUnit Section(OrgUnit department, string name = "Football", params string[] categories) =>
        OrgUnit.CreateChild(department, DepartmentType, SectionType, name, null, null, null, categories.Length == 0 ? ["cat-1"] : categories, Now);
}
