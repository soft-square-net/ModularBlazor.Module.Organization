namespace soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;

/// <summary>
/// A link between two sibling tenants of an org: a unit of one tenant connects to a unit of another. A skeleton for
/// now: the states only (Requested → Active | Declined, Active → Revoked), with no capabilities and no actions. A
/// future Webhooks module will let linked tenants subscribe to each other's actions.
/// </summary>
public sealed class TenantLink : AggregateRoot<Guid>
{
    private TenantLink() { } // EF Core

    public Guid OrgId { get; private set; }
    public string FromSeedId { get; private set; } = "";
    public Guid FromUnitId { get; private set; }
    public string ToSeedId { get; private set; } = "";
    public Guid ToUnitId { get; private set; }
    public TenantLinkState State { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }

    public static TenantLink Request(Guid orgId, string fromSeedId, Guid fromUnitId, string toSeedId, Guid toUnitId, DateTimeOffset now)
    {
        if (string.Equals(fromSeedId, toSeedId, StringComparison.Ordinal)) throw new DomainException("A link joins two different tenants.");
        return new TenantLink
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            FromSeedId = Rules.Required(fromSeedId, "The source tenant's seed", TenantSeedAggregate.TenantSeed.IdMaxLength),
            FromUnitId = fromUnitId,
            ToSeedId = Rules.Required(toSeedId, "The target tenant's seed", TenantSeedAggregate.TenantSeed.IdMaxLength),
            ToUnitId = toUnitId,
            State = TenantLinkState.Requested,
            RequestedAt = now
        };
    }

    public void Accept(DateTimeOffset now) => Decide(TenantLinkState.Active, now);

    public void Decline(DateTimeOffset now) => Decide(TenantLinkState.Declined, now);

    public void Revoke(DateTimeOffset now)
    {
        if (State != TenantLinkState.Active) throw new DomainException($"Only an active link can be revoked; this one is {State}.");
        State = TenantLinkState.Revoked;
        RevokedAt = now;
    }

    private void Decide(TenantLinkState state, DateTimeOffset now)
    {
        if (State != TenantLinkState.Requested) throw new DomainException($"Only a requested link can be accepted or declined; this one is {State}.");
        State = state;
        DecidedAt = now;
    }
}
