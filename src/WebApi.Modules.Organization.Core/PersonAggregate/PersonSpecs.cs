namespace soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;

public sealed class PersonByIdSpec : SingleResultSpecification<Person>
{
    public PersonByIdSpec(Guid orgId, Guid id) => Query.Where(p => p.OrgId == orgId && p.Id == id);
}

public sealed class PersonByIdentitySpec : SingleResultSpecification<Person>
{
    public PersonByIdentitySpec(Guid orgId, string issuer, string subject) =>
        Query.Where(p => p.OrgId == orgId && p.Issuer == issuer && p.Subject == subject);
}

/// <summary>The person rows of one identity, in every org (the orgs a caller belongs to).</summary>
public sealed class PersonsOfIdentitySpec : Specification<Person>
{
    public PersonsOfIdentitySpec(string issuer, string subject) => Query.Where(p => p.Issuer == issuer && p.Subject == subject);
}

public sealed class PersonsOfOrgSpec : Specification<Person>
{
    public PersonsOfOrgSpec(Guid orgId) => Query.Where(p => p.OrgId == orgId).OrderBy(p => p.DisplayName);
}

public sealed class PersonsByIdsSpec : Specification<Person>
{
    public PersonsByIdsSpec(Guid orgId, IReadOnlyCollection<Guid> ids) => Query.Where(p => p.OrgId == orgId && ids.Contains(p.Id));
}
