using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;

public sealed record ListMembershipsQuery(Guid OrgId, Guid? UnitId = null, Guid? PersonId = null) : IQuery<Result<IReadOnlyList<MembershipDto>>>;

public sealed class ListMembershipsHandler(IOrgAccess access, IReadRepository<Membership> memberships, IReadRepository<PositionDefinition> positions)
    : IQueryHandler<ListMembershipsQuery, Result<IReadOnlyList<MembershipDto>>>
{
    public async ValueTask<Result<IReadOnlyList<MembershipDto>>> Handle(ListMembershipsQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<MembershipDto>>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<IReadOnlyList<MembershipDto>>("read this org");
        var list = await memberships.ListAsync(new MembershipsSpec(query.OrgId, query.UnitId, query.PersonId), ct);
        var byId = (await positions.ListAsync(new PositionsOfOrgSpec(query.OrgId), ct)).ToDictionary(p => p.Id);
        return list.Select(m => m.ToDto(m.PositionId is { } id ? byId.GetValueOrDefault(id) : null)).ToList();
    }
}

/// <summary>Puts a person in a unit (<c>org.members.manage</c> on the unit), optionally with a position.</summary>
public sealed record CreateMembershipCommand(Guid OrgId, CreateMembershipRequest Request) : ICommand<Result<MembershipDto>>;

public sealed class CreateMembershipHandler(IOrgAccess access, MembershipParts parts, IReadRepository<Membership> memberships, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<CreateMembershipCommand, Result<MembershipDto>>
{
    public async ValueTask<Result<MembershipDto>> Handle(CreateMembershipCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<MembershipDto>();
        var loaded = await parts.LoadAsync(command.OrgId, r.PersonId, r.UnitId, r.PositionId, ct);
        if (!loaded.IsSuccess) return loaded.Fail<MembershipDto>();
        var (person, unit, position) = loaded.Value;
        if (!rights.Value.Can(OrgPermissions.MembersManage, unit.Path)) return Refusals.Forbidden<MembershipDto>("add members to this unit");

        var now = clock.GetUtcNow();
        var membership = Membership.Create(person, unit, position, r.From ?? now, r.To, r.Primary, now);
        store.Add(membership);
        if (membership.Primary) await parts.ClearOtherPrimariesAsync(membership, memberships, feed, now, ct);
        feed.MembershipChanged(membership, person, unit, position);
        await store.SaveAsync(ct);
        return membership.ToDto(position);
    }
}

/// <summary>Changes a membership's position, period or primary flag (<c>org.members.manage</c> on its unit).</summary>
public sealed record UpdateMembershipCommand(Guid OrgId, Guid MembershipId, UpdateMembershipRequest Request) : ICommand<Result<MembershipDto>>;

public sealed class UpdateMembershipHandler(IOrgAccess access, MembershipParts parts, IReadRepository<Membership> memberships, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<UpdateMembershipCommand, Result<MembershipDto>>
{
    public async ValueTask<Result<MembershipDto>> Handle(UpdateMembershipCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<MembershipDto>();
        var membership = await memberships.FirstOrDefaultAsync(new MembershipByIdSpec(command.OrgId, command.MembershipId), ct);
        if (membership is null) return Result<MembershipDto>.NotFound($"There is no membership {command.MembershipId}.");
        var loaded = await parts.LoadAsync(command.OrgId, membership.PersonId, membership.UnitId, r.PositionId, ct);
        if (!loaded.IsSuccess) return loaded.Fail<MembershipDto>();
        var (person, unit, position) = loaded.Value;
        if (!rights.Value.Can(OrgPermissions.MembersManage, unit.Path)) return Refusals.Forbidden<MembershipDto>("change members of this unit");

        var now = clock.GetUtcNow();
        membership.Change(position, r.From, r.To, r.Primary, now);
        if (membership.Primary) await parts.ClearOtherPrimariesAsync(membership, memberships, feed, now, ct);
        feed.MembershipChanged(membership, person, unit, position);
        await store.SaveAsync(ct);
        return membership.ToDto(position);
    }
}

/// <summary>
/// Deletes a membership (<c>org.members.manage</c> on its unit). Ending one is an update with <c>To</c>; removing is
/// for a mistake. Either way every deployment drops the grants it gave.
/// </summary>
public sealed record RemoveMembershipCommand(Guid OrgId, Guid MembershipId) : ICommand<Result>;

public sealed class RemoveMembershipHandler(IOrgAccess access, MembershipParts parts, IReadRepository<Membership> memberships, IOrgStore store, OrgFeed feed)
    : ICommandHandler<RemoveMembershipCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveMembershipCommand command, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail();
        var membership = await memberships.FirstOrDefaultAsync(new MembershipByIdSpec(command.OrgId, command.MembershipId), ct);
        if (membership is null) return Result.NotFound($"There is no membership {command.MembershipId}.");
        var loaded = await parts.LoadAsync(command.OrgId, membership.PersonId, membership.UnitId, membership.PositionId, ct);
        if (!loaded.IsSuccess) return loaded.Fail();
        var (person, unit, position) = loaded.Value;
        if (!rights.Value.Can(OrgPermissions.MembersManage, unit.Path)) return Refusals.Forbidden("remove members of this unit");

        store.Remove(membership);
        feed.MembershipChanged(membership, person, unit, position, removed: true);
        await store.SaveAsync(ct);
        return Result.Success();
    }
}

/// <summary>Loads what a membership refers to (scoped helper of the membership use cases).</summary>
public sealed class MembershipParts(IReadRepository<Person> persons, IReadRepository<OrgUnit> units, IReadRepository<PositionDefinition> positions)
{
    public async Task<Result<(Person Person, OrgUnit Unit, PositionDefinition? Position)>> LoadAsync(Guid orgId, Guid personId, Guid unitId, Guid? positionId, CancellationToken ct)
    {
        var person = await persons.FirstOrDefaultAsync(new PersonByIdSpec(orgId, personId), ct);
        if (person is null) return Result.NotFound($"There is no person {personId}.");
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(orgId, unitId), ct);
        if (unit is null) return Result.NotFound($"There is no unit {unitId}.");
        PositionDefinition? position = null;
        if (positionId is { } id)
        {
            position = await positions.FirstOrDefaultAsync(new PositionByIdSpec(orgId, id), ct);
            if (position is null) return Result.NotFound($"There is no position {id}.");
        }
        return (person, unit, position);
    }

    /// <summary>A person has one primary membership: the others lose the flag (and say so in the feed).</summary>
    public async Task ClearOtherPrimariesAsync(Membership primary, IReadRepository<Membership> memberships, OrgFeed feed, DateTimeOffset now, CancellationToken ct)
    {
        var others = (await memberships.ListAsync(new MembershipsSpec(primary.OrgId, personId: primary.PersonId), ct))
            .Where(m => m.Id != primary.Id && m.Primary).ToList();
        foreach (var other in others)
        {
            other.ClearPrimary(now);
            var loaded = await LoadAsync(other.OrgId, other.PersonId, other.UnitId, other.PositionId, ct);
            if (loaded.IsSuccess) feed.MembershipChanged(other, loaded.Value.Person, loaded.Value.Unit, loaded.Value.Position);
        }
    }
}
