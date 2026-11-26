namespace soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

public sealed class UnitTypesOfOrgSpec : Specification<UnitType>
{
    public UnitTypesOfOrgSpec(Guid orgId) => Query.Where(t => t.OrgId == orgId).OrderBy(t => t.Order).ThenBy(t => t.Key);
}
