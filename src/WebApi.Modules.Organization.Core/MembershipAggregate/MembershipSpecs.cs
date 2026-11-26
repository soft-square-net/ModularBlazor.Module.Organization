namespace soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;

public sealed class MembershipByIdSpec : SingleResultSpecification<Membership>
{
    public MembershipByIdSpec(Guid orgId, Guid id) => Query.Where(m => m.OrgId == orgId && m.Id == id);
}

/// <summary>An org's memberships, optionally of one unit and/or one person.</summary>
public sealed class MembershipsSpec : Specification<Membership>
{
    public MembershipsSpec(Guid orgId, Guid? unitId = null, Guid? personId = null)
    {
        Query.Where(m => m.OrgId == orgId);
        if (unitId is not null) Query.Where(m => m.UnitId == unitId);
        if (personId is not null) Query.Where(m => m.PersonId == personId);
        Query.OrderBy(m => m.From);
    }
}

/// <summary>A person's memberships that have a position and are active at <paramref name="at"/> (what the person may do).</summary>
public sealed class ActivePositionsOfPersonSpec : Specification<Membership>
{
    public ActivePositionsOfPersonSpec(Guid personId, DateTimeOffset at) =>
        Query.Where(m => m.PersonId == personId && m.PositionId != null && m.From <= at && (m.To == null || at < m.To));
}

public sealed class MembershipsWithPositionSpec : Specification<Membership>
{
    public MembershipsWithPositionSpec(Guid positionId) => Query.Where(m => m.PositionId == positionId);
}
