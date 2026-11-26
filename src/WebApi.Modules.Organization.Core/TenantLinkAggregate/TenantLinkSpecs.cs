namespace soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;

public sealed class LinksOfOrgSpec : Specification<TenantLink>
{
    public LinksOfOrgSpec(Guid orgId) => Query.Where(l => l.OrgId == orgId).OrderBy(l => l.RequestedAt);
}
