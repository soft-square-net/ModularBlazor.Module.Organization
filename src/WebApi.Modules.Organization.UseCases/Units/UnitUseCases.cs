using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Units;

/// <summary>The org's units, in tree order.</summary>
public sealed record ListUnitsQuery(Guid OrgId) : IQuery<Result<IReadOnlyList<OrgUnitDto>>>;

public sealed class ListUnitsHandler(IOrgAccess access, IReadRepository<OrgUnit> units) : IQueryHandler<ListUnitsQuery, Result<IReadOnlyList<OrgUnitDto>>>
{
    public async ValueTask<Result<IReadOnlyList<OrgUnitDto>>> Handle(ListUnitsQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<OrgUnitDto>>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<IReadOnlyList<OrgUnitDto>>("read this org");
        return (await units.ListAsync(new UnitsOfOrgSpec(query.OrgId), ct)).Select(u => u.ToDto()).ToList();
    }
}

public sealed record GetUnitQuery(Guid OrgId, Guid UnitId) : IQuery<Result<OrgUnitDto>>;

public sealed class GetUnitHandler(IOrgAccess access, IReadRepository<OrgUnit> units) : IQueryHandler<GetUnitQuery, Result<OrgUnitDto>>
{
    public async ValueTask<Result<OrgUnitDto>> Handle(GetUnitQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgUnitDto>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<OrgUnitDto>("read this org");
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(query.OrgId, query.UnitId), ct);
        return unit is null ? UnitResults.NotFound<OrgUnitDto>(query.UnitId) : unit.ToDto();
    }
}

/// <summary>The tenant a unit lives in: its own, or its nearest ancestor's that owns one.</summary>
public sealed record GetUnitTenantQuery(Guid OrgId, Guid UnitId) : IQuery<Result<UnitTenantDto>>;

public sealed class GetUnitTenantHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<TenantSeed> seeds)
    : IQueryHandler<GetUnitTenantQuery, Result<UnitTenantDto>>
{
    public async ValueTask<Result<UnitTenantDto>> Handle(GetUnitTenantQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<UnitTenantDto>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<UnitTenantDto>("read this org");
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(query.OrgId, query.UnitId), ct);
        if (unit is null) return UnitResults.NotFound<UnitTenantDto>(query.UnitId);

        var ancestors = await units.ListAsync(new UnitsByIdsSpec(query.OrgId, UnitTenancy.AncestorIds(unit)), ct);
        var owner = UnitTenancy.Owner(unit, ancestors);
        if (owner?.SeedId is null) return new UnitTenantDto(unit.Id, null, null, null, null);
        var seed = await seeds.FirstOrDefaultAsync(new SeedByIdSpec(owner.SeedId), ct);
        return new UnitTenantDto(unit.Id, owner.SeedId, owner.Id, seed?.DeploymentId, seed?.TenantId);
    }
}

/// <summary>A unit under a parent (<c>org.units.manage</c> on the parent): its type must be allowed under the parent's.</summary>
public sealed record CreateUnitCommand(Guid OrgId, CreateUnitRequest Request) : ICommand<Result<OrgUnitDto>>;

public sealed class CreateUnitHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<UnitType> types, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<CreateUnitCommand, Result<OrgUnitDto>>
{
    public async ValueTask<Result<OrgUnitDto>> Handle(CreateUnitCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgUnitDto>();
        var parent = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, r.ParentId), ct);
        if (parent is null) return UnitResults.NotFound<OrgUnitDto>(r.ParentId);
        if (!rights.Value.Can(OrgPermissions.UnitsManage, parent.Path)) return Refusals.Forbidden<OrgUnitDto>("add units here");

        var orgTypes = await types.ListAsync(new UnitTypesOfOrgSpec(command.OrgId), ct);
        var type = orgTypes.FirstOrDefault(t => t.Key == r.TypeKey?.Trim().ToLowerInvariant());
        if (type is null) return Result<OrgUnitDto>.Invalid(new ValidationError(nameof(r.TypeKey), $"The org has no unit type '{r.TypeKey}'."));
        var parentType = orgTypes.Single(t => t.Id == parent.TypeId);

        var unit = OrgUnit.CreateChild(parent, parentType, type, r.NameEn, r.NameAr, r.Tags, r.TimeZone, r.Categories, clock.GetUtcNow());
        store.Add(unit);
        feed.UnitCreated(unit);
        await store.SaveAsync(ct);
        return unit.ToDto();
    }
}

/// <summary>Names, tags, time zone and categories of a unit (<c>org.units.manage</c> on the unit).</summary>
public sealed record UpdateUnitCommand(Guid OrgId, Guid UnitId, UpdateUnitRequest Request) : ICommand<Result<OrgUnitDto>>;

public sealed class UpdateUnitHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<UnitType> types, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<UpdateUnitCommand, Result<OrgUnitDto>>
{
    public async ValueTask<Result<OrgUnitDto>> Handle(UpdateUnitCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgUnitDto>();
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, command.UnitId), ct);
        if (unit is null) return UnitResults.NotFound<OrgUnitDto>(command.UnitId);
        if (!rights.Value.Can(OrgPermissions.UnitsManage, unit.Path)) return Refusals.Forbidden<OrgUnitDto>("change this unit");
        if (unit.Version != r.Version) return UnitResults.Stale<OrgUnitDto>(unit, r.Version);

        var type = (await types.ListAsync(new UnitTypesOfOrgSpec(command.OrgId), ct)).Single(t => t.Id == unit.TypeId);
        unit.Update(type, r.NameEn, r.NameAr, r.Tags, r.TimeZone, r.Categories, clock.GetUtcNow());
        feed.UnitUpdated(unit);
        await store.SaveAsync(ct);
        return unit.ToDto();
    }
}

/// <summary>
/// Moves a unit and its subtree under another parent (<c>org.units.manage</c> on the old and the new parent), and
/// rewrites the paths of the whole subtree. One <c>org.unit-moved.v1</c> event: replicas rebase every path under the old one.
/// </summary>
public sealed record MoveUnitCommand(Guid OrgId, Guid UnitId, MoveUnitRequest Request) : ICommand<Result<OrgUnitDto>>;

public sealed class MoveUnitHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<UnitType> types, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<MoveUnitCommand, Result<OrgUnitDto>>
{
    public async ValueTask<Result<OrgUnitDto>> Handle(MoveUnitCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgUnitDto>();
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, command.UnitId), ct);
        if (unit is null) return UnitResults.NotFound<OrgUnitDto>(command.UnitId);
        var newParent = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, r.NewParentId), ct);
        if (newParent is null) return UnitResults.NotFound<OrgUnitDto>(r.NewParentId);
        if (unit.Version != r.Version) return UnitResults.Stale<OrgUnitDto>(unit, r.Version);
        if (unit.ParentId == newParent.Id) return unit.ToDto();

        var oldParentPath = UnitPaths.Ids(unit.Path).Count > 1 ? unit.Path[..unit.Path.LastIndexOf(UnitPaths.Separator)] : unit.Path;
        if (!rights.Value.Can(OrgPermissions.UnitsManage, oldParentPath) || !rights.Value.Can(OrgPermissions.UnitsManage, newParent.Path))
            return Refusals.Forbidden<OrgUnitDto>("move this unit there");

        var orgTypes = await types.ListAsync(new UnitTypesOfOrgSpec(command.OrgId), ct);
        var subtree = await units.ListAsync(new UnitsBelowSpec(command.OrgId, unit.Path), ct);
        var oldParentId = unit.ParentId;
        var oldPath = unit.MoveUnder(newParent, orgTypes.Single(t => t.Id == newParent.TypeId), orgTypes.Single(t => t.Id == unit.TypeId), subtree, clock.GetUtcNow());
        feed.UnitMoved(unit, oldParentId, oldPath);
        await store.SaveAsync(ct);
        return unit.ToDto();
    }
}

/// <summary>Removes a unit (<c>org.units.manage</c> on its parent) once it has no children, no memberships and owns no tenant.</summary>
public sealed record RemoveUnitCommand(Guid OrgId, Guid UnitId) : ICommand<Result>;

public sealed class RemoveUnitHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<Membership> memberships, IOrgStore store, OrgFeed feed)
    : ICommandHandler<RemoveUnitCommand, Result>
{
    public async ValueTask<Result> Handle(RemoveUnitCommand command, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail();
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, command.UnitId), ct);
        if (unit is null) return Result.NotFound($"There is no unit {command.UnitId}.");
        if (unit.ParentId is null) return Result.Conflict("The root unit cannot be removed.");
        var parentPath = unit.Path[..unit.Path.LastIndexOf(UnitPaths.Separator)];
        if (!rights.Value.Can(OrgPermissions.UnitsManage, parentPath)) return Refusals.Forbidden("remove this unit");
        if (unit.Binding == TenantBindingKind.OwnTenant) return Result.Conflict("The unit owns a tenant; it cannot be removed.");
        if (await units.AnyAsync(new ChildUnitsSpec(command.OrgId, unit.Id), ct)) return Result.Conflict("The unit has units below it; move or remove them first.");
        if (await memberships.AnyAsync(new MembershipsSpec(command.OrgId, unitId: unit.Id), ct)) return Result.Conflict("The unit has members; end their memberships first.");

        store.Remove(unit);
        feed.UnitRemoved(unit);
        await store.SaveAsync(ct);
        return Result.Success();
    }
}

internal static class UnitResults
{
    public static Result<T> NotFound<T>(Guid id) => Result<T>.NotFound($"There is no unit {id}.");

    public static Result<T> Stale<T>(OrgUnit unit, int version) =>
        Result<T>.Conflict($"The unit is at version {unit.Version}, not {version}; reload it and try again.");
}
