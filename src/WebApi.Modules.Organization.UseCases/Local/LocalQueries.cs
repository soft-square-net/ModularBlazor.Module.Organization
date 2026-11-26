namespace soft.square.net.WebApi.Modules.Organization.UseCases.Local;

// The local read API: the current tenant's slice of its org tree, from this deployment's replica (IUnitDirectory).
// Units are readable by any signed-in caller of the tenant; members need org.read on the unit or one of its ancestors
// (a tenant grant, e.g. the one a position gives through the connector).

public sealed record ListLocalUnitsQuery : IQuery<Result<IReadOnlyList<DirectoryUnitDto>>>;

public sealed class ListLocalUnitsHandler(IUnitDirectory directory) : IQueryHandler<ListLocalUnitsQuery, Result<IReadOnlyList<DirectoryUnitDto>>>
{
    public async ValueTask<Result<IReadOnlyList<DirectoryUnitDto>>> Handle(ListLocalUnitsQuery query, CancellationToken ct) =>
        Result.Success(await directory.ListAsync(ct));
}

public sealed record GetLocalUnitQuery(Guid UnitId) : IQuery<Result<DirectoryUnitDto>>;

public sealed class GetLocalUnitHandler(IUnitDirectory directory) : IQueryHandler<GetLocalUnitQuery, Result<DirectoryUnitDto>>
{
    public async ValueTask<Result<DirectoryUnitDto>> Handle(GetLocalUnitQuery query, CancellationToken ct) =>
        await directory.GetAsync(query.UnitId, ct) is { } unit ? unit : Result.NotFound($"This tenant has no unit {query.UnitId}.");
}

public sealed record GetLocalAncestorsQuery(Guid UnitId) : IQuery<Result<IReadOnlyList<DirectoryUnitDto>>>;

public sealed class GetLocalAncestorsHandler(IUnitDirectory directory) : IQueryHandler<GetLocalAncestorsQuery, Result<IReadOnlyList<DirectoryUnitDto>>>
{
    public async ValueTask<Result<IReadOnlyList<DirectoryUnitDto>>> Handle(GetLocalAncestorsQuery query, CancellationToken ct) =>
        await directory.GetAsync(query.UnitId, ct) is null
            ? Result.NotFound($"This tenant has no unit {query.UnitId}.")
            : Result.Success(await directory.AncestorsAsync(query.UnitId, ct));
}

public sealed record GetLocalCategoriesQuery(Guid UnitId) : IQuery<Result<IReadOnlyList<string>>>;

public sealed class GetLocalCategoriesHandler(IUnitDirectory directory) : IQueryHandler<GetLocalCategoriesQuery, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(GetLocalCategoriesQuery query, CancellationToken ct) =>
        await directory.GetAsync(query.UnitId, ct) is null
            ? Result.NotFound($"This tenant has no unit {query.UnitId}.")
            : Result.Success(await directory.CategoriesOfAsync(query.UnitId, ct));
}

public sealed record GetLocalMembersQuery(Guid UnitId) : IQuery<Result<IReadOnlyList<DirectoryMemberDto>>>;

public sealed class GetLocalMembersHandler(IUnitDirectory directory, IAccessScopes access) : IQueryHandler<GetLocalMembersQuery, Result<IReadOnlyList<DirectoryMemberDto>>>
{
    public async ValueTask<Result<IReadOnlyList<DirectoryMemberDto>>> Handle(GetLocalMembersQuery query, CancellationToken ct)
    {
        var unit = await directory.GetAsync(query.UnitId, ct);
        if (unit is null) return Result.NotFound($"This tenant has no unit {query.UnitId}.");
        if (!await access.HasPermissionAsync(OrganizationPermissions.Read, UnitScopes.For(unit.Path), ct))
            return Result.Forbidden($"Reading the members of this unit needs {OrganizationPermissions.Read} on it or above it.");
        return Result.Success(await directory.MembersOfAsync(query.UnitId, ct));
    }
}
