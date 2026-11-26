namespace soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;

public sealed class SeedByIdSpec : SingleResultSpecification<TenantSeed>
{
    public SeedByIdSpec(string id) => Query.Where(s => s.Id == id);
}

public sealed class SeedOfUnitSpec : SingleResultSpecification<TenantSeed>
{
    public SeedOfUnitSpec(Guid rootUnitId) => Query.Where(s => s.RootUnitId == rootUnitId);
}

public sealed class SeedByTenantSpec : SingleResultSpecification<TenantSeed>
{
    public SeedByTenantSpec(string deploymentId, string tenantId) => Query.Where(s => s.DeploymentId == deploymentId && s.TenantId == tenantId);
}

public sealed class SeedsOfOrgSpec : Specification<TenantSeed>
{
    public SeedsOfOrgSpec(Guid orgId) => Query.Where(s => s.OrgId == orgId).OrderBy(s => s.TenantId);
}

public sealed class SeedsOfDeploymentSpec : Specification<TenantSeed>
{
    public SeedsOfDeploymentSpec(string deploymentId) => Query.Where(s => s.DeploymentId == deploymentId).OrderBy(s => s.TenantId);
}

public sealed class SeedsByIdsSpec : Specification<TenantSeed>
{
    public SeedsByIdsSpec(IReadOnlyCollection<string> ids) => Query.Where(s => ids.Contains(s.Id));
}
