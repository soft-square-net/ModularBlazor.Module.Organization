namespace soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;

public sealed class PositionByIdSpec : SingleResultSpecification<PositionDefinition>
{
    public PositionByIdSpec(Guid orgId, Guid id) => Query.Where(p => p.OrgId == orgId && p.Id == id);
}

public sealed class PositionByKeySpec : SingleResultSpecification<PositionDefinition>
{
    public PositionByKeySpec(Guid orgId, string key) => Query.Where(p => p.OrgId == orgId && p.Key == key);
}

public sealed class PositionsOfOrgSpec : Specification<PositionDefinition>
{
    public PositionsOfOrgSpec(Guid orgId) => Query.Where(p => p.OrgId == orgId).OrderBy(p => p.Key);
}

public sealed class PositionsByIdsSpec : Specification<PositionDefinition>
{
    public PositionsByIdsSpec(IReadOnlyCollection<Guid> ids) => Query.Where(p => ids.Contains(p.Id));
}
