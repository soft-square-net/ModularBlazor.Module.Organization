using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;

namespace soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;

/// <summary>
/// A person's place in a unit: optionally with a position, between <see cref="From"/> and <see cref="To"/> (open when
/// null). <see cref="Primary"/> marks the person's main unit (one at a time; the use case clears the others).
/// </summary>
public sealed class Membership : AggregateRoot<Guid>
{
    private Membership() { } // EF Core

    public Guid OrgId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid? PositionId { get; private set; }
    public DateTimeOffset From { get; private set; }
    public DateTimeOffset? To { get; private set; }
    public bool Primary { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// The tenant seed whose admin list created this membership (a unit admin of the seed's root unit), or null when
    /// someone added it. The seed removes only its own memberships when a person leaves its admin list.
    /// </summary>
    public string? SeedId { get; private set; }

    public static Membership Create(Person person, OrgUnit unit, PositionDefinition? position, DateTimeOffset from, DateTimeOffset? to, bool primary, DateTimeOffset now,
        string? seedId = null)
    {
        if (person.OrgId != unit.OrgId) throw new DomainException("The person and the unit belong to different orgs.");
        var membership = new Membership { Id = Guid.CreateVersion7(), OrgId = unit.OrgId, PersonId = person.Id, UnitId = unit.Id, SeedId = seedId };
        membership.Set(position, from, to, primary, now);
        return membership;
    }

    public void Change(PositionDefinition? position, DateTimeOffset from, DateTimeOffset? to, bool primary, DateTimeOffset now) =>
        Set(position, from, to, primary, now);

    /// <summary>Another membership became the person's primary one.</summary>
    public void ClearPrimary(DateTimeOffset now)
    {
        Primary = false;
        UpdatedAt = now;
    }

    public bool IsActiveAt(DateTimeOffset at) => From <= at && (To is null || at < To);

    private void Set(PositionDefinition? position, DateTimeOffset from, DateTimeOffset? to, bool primary, DateTimeOffset now)
    {
        (from, to) = (from.ToUtc(), to.ToUtc()); // callers send local times (the browser's offset); the module stores UTC
        if (position is not null && position.OrgId != OrgId) throw new DomainException("The position belongs to another org.");
        if (to is not null && to <= from) throw new DomainException("A membership must end after it starts.");
        PositionId = position?.Id;
        From = from;
        To = to;
        Primary = primary;
        UpdatedAt = now.ToUtc();
    }
}
