namespace soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;

public sealed class OrgByIdSpec : SingleResultSpecification<Org>
{
    public OrgByIdSpec(Guid id) => Query.Where(o => o.Id == id);
}

public sealed class OrgByKeySpec : SingleResultSpecification<Org>
{
    public OrgByKeySpec(string key) => Query.Where(o => o.Key == key);
}

/// <summary>The orgs an identity is a customer admin of.</summary>
public sealed class OrgsAdministeredBySpec : Specification<Org>
{
    public OrgsAdministeredBySpec(string issuer, string subject) =>
        Query.Where(o => o.Admins.Any(a => a.Issuer == issuer && a.Subject == subject));
}

/// <summary>Every org, or only those with the given ids, by key.</summary>
public sealed class OrgsSpec : Specification<Org>
{
    public OrgsSpec(IReadOnlyCollection<Guid>? ids = null)
    {
        if (ids is not null) Query.Where(o => ids.Contains(o.Id));
        Query.OrderBy(o => o.Key);
    }
}
