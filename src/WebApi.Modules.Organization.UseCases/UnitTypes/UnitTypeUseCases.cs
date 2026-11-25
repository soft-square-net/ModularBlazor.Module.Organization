using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.UnitTypes;

public sealed record ListUnitTypesQuery(Guid OrgId) : IQuery<Result<IReadOnlyList<UnitTypeDto>>>;

public sealed class ListUnitTypesHandler(IOrgAccess access, IReadRepository<UnitType> types) : IQueryHandler<ListUnitTypesQuery, Result<IReadOnlyList<UnitTypeDto>>>
{
    public async ValueTask<Result<IReadOnlyList<UnitTypeDto>>> Handle(ListUnitTypesQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<UnitTypeDto>>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<IReadOnlyList<UnitTypeDto>>("read this org");
        return (await types.ListAsync(new UnitTypesOfOrgSpec(query.OrgId), ct)).Select(t => t.ToDto()).ToList();
    }
}

/// <summary>A new unit type for the org (customer admins): its allowed parents must be types the org has.</summary>
public sealed record CreateUnitTypeCommand(Guid OrgId, CreateUnitTypeRequest Request) : ICommand<Result<UnitTypeDto>>;

public sealed class CreateUnitTypeHandler(IOrgAccess access, IReadRepository<UnitType> types, IOrgStore store)
    : ICommandHandler<CreateUnitTypeCommand, Result<UnitTypeDto>>
{
    public async ValueTask<Result<UnitTypeDto>> Handle(CreateUnitTypeCommand command, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<UnitTypeDto>();
        if (!rights.Value.Administers) return Refusals.Forbidden<UnitTypeDto>("define unit types");
        var r = command.Request;
        var existing = (await types.ListAsync(new UnitTypesOfOrgSpec(command.OrgId), ct)).Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
        var type = UnitType.Create(command.OrgId, r.Key, r.NameEn, r.NameAr, r.AllowedParents, r.HoldsCategories, r.Order, existing);
        store.Add(type);
        await store.SaveAsync(ct);
        return type.ToDto();
    }
}
