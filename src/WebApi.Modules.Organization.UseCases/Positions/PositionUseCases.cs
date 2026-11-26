using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Positions;

public sealed record ListPositionsQuery(Guid OrgId) : IQuery<Result<IReadOnlyList<PositionDto>>>;

public sealed class ListPositionsHandler(IOrgAccess access, IReadRepository<PositionDefinition> positions)
    : IQueryHandler<ListPositionsQuery, Result<IReadOnlyList<PositionDto>>>
{
    public async ValueTask<Result<IReadOnlyList<PositionDto>>> Handle(ListPositionsQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<PositionDto>>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<IReadOnlyList<PositionDto>>("read this org");
        return (await positions.ListAsync(new PositionsOfOrgSpec(query.OrgId), ct)).Select(p => p.ToDto()).ToList();
    }
}

/// <summary>A new position (customer admins). Its permissions are names from the platform's permission catalog.</summary>
public sealed record CreatePositionCommand(Guid OrgId, CreatePositionRequest Request) : ICommand<Result<PositionDto>>;

public sealed class CreatePositionHandler(IOrgAccess access, IReadRepository<PositionDefinition> positions, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<CreatePositionCommand, Result<PositionDto>>
{
    public async ValueTask<Result<PositionDto>> Handle(CreatePositionCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<PositionDto>();
        if (!rights.Value.Administers) return Refusals.Forbidden<PositionDto>("define positions");

        var position = PositionDefinition.Create(command.OrgId, r.Key, r.NameEn, r.NameAr, r.Permissions, clock.GetUtcNow());
        if (await positions.AnyAsync(new PositionByKeySpec(command.OrgId, position.Key), ct))
            return Result<PositionDto>.Conflict($"The org already has a position '{position.Key}'.");
        store.Add(position);
        feed.PositionChanged(position);
        await store.SaveAsync(ct);
        return position.ToDto();
    }
}

/// <summary>
/// Names and permissions of a position (customer admins). Its <c>org.position-changed.v1</c> event lets every
/// deployment rewrite the grants of the memberships that hold it.
/// </summary>
public sealed record UpdatePositionCommand(Guid OrgId, Guid PositionId, UpdatePositionRequest Request) : ICommand<Result<PositionDto>>;

public sealed class UpdatePositionHandler(IOrgAccess access, IReadRepository<PositionDefinition> positions, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<UpdatePositionCommand, Result<PositionDto>>
{
    public async ValueTask<Result<PositionDto>> Handle(UpdatePositionCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<PositionDto>();
        if (!rights.Value.Administers) return Refusals.Forbidden<PositionDto>("change positions");
        var position = await positions.FirstOrDefaultAsync(new PositionByIdSpec(command.OrgId, command.PositionId), ct);
        if (position is null) return Result<PositionDto>.NotFound($"There is no position {command.PositionId}.");
        if (position.Version != r.Version) return Result<PositionDto>.Conflict($"The position is at version {position.Version}; reload and try again.");

        position.Update(r.NameEn, r.NameAr, r.Permissions, clock.GetUtcNow());
        feed.PositionChanged(position);
        await store.SaveAsync(ct);
        return position.ToDto();
    }
}
