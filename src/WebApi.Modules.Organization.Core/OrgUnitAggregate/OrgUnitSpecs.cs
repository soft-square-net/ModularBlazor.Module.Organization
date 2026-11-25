namespace soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;

public sealed class UnitByIdSpec : SingleResultSpecification<OrgUnit>
{
    public UnitByIdSpec(Guid orgId, Guid id) => Query.Where(u => u.OrgId == orgId && u.Id == id);
}

/// <summary>An org's units, in tree order (by depth, then path).</summary>
public sealed class UnitsOfOrgSpec : Specification<OrgUnit>
{
    public UnitsOfOrgSpec(Guid orgId) => Query.Where(u => u.OrgId == orgId).OrderBy(u => u.Depth).ThenBy(u => u.Path);
}

/// <summary>Some units of an org by id (the ancestors of a unit, for example).</summary>
public sealed class UnitsByIdsSpec : Specification<OrgUnit>
{
    public UnitsByIdsSpec(Guid orgId, IReadOnlyCollection<Guid> ids) => Query.Where(u => u.OrgId == orgId && ids.Contains(u.Id));
}

/// <summary>Every unit strictly below the unit at <paramref name="path"/>.</summary>
public sealed class UnitsBelowSpec : Specification<OrgUnit>
{
    public UnitsBelowSpec(Guid orgId, string path)
    {
        var prefix = path + UnitPaths.Separator;
        Query.Where(u => u.OrgId == orgId && u.Path.StartsWith(prefix));
    }
}

/// <summary>The direct children of a unit.</summary>
public sealed class ChildUnitsSpec : Specification<OrgUnit>
{
    public ChildUnitsSpec(Guid orgId, Guid parentId) => Query.Where(u => u.OrgId == orgId && u.ParentId == parentId);
}
