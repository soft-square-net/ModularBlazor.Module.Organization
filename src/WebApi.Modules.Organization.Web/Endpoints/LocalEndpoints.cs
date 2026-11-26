using soft.square.net.WebApi.Modules.Organization.UseCases.Local;

namespace soft.square.net.WebApi.Modules.Organization.Web.Endpoints;

/// <summary>/local/units: the current tenant's slice of its org tree, from this deployment's replica (IUnitDirectory).</summary>
public static class LocalEndpoints
{
    public static void Map(IEndpointRouteBuilder units)
    {
        units.MapGet("/", async Task<Results<Ok<IReadOnlyList<DirectoryUnitDto>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListLocalUnitsQuery(), ct)).ToOkResult())
            .WithName("Organization_ListLocalUnits").WithSummary("The tenant's units and its root unit's ancestors, root first (from the replica).");

        units.MapGet("/{unitId:guid}", async Task<Results<Ok<DirectoryUnitDto>, ProblemHttpResult>> (Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetLocalUnitQuery(unitId), ct)).ToOkResult())
            .WithName("Organization_GetLocalUnit").WithSummary("One unit of the tenant (or an ancestor of its root unit).");

        units.MapGet("/{unitId:guid}/ancestors", async Task<Results<Ok<IReadOnlyList<DirectoryUnitDto>>, ProblemHttpResult>> (Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetLocalAncestorsQuery(unitId), ct)).ToOkResult())
            .WithName("Organization_GetLocalAncestors").WithSummary("A unit's ancestors, root first.");

        units.MapGet("/{unitId:guid}/categories", async Task<Results<Ok<IReadOnlyList<string>>, ProblemHttpResult>> (Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetLocalCategoriesQuery(unitId), ct)).ToOkResult())
            .WithName("Organization_GetLocalCategories").WithSummary("A unit's post categories: its own (a Section), or those of the tenant's units below it.");

        units.MapGet("/{unitId:guid}/members", async Task<Results<Ok<IReadOnlyList<DirectoryMemberDto>>, ProblemHttpResult>> (Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetLocalMembersQuery(unitId), ct)).ToOkResult())
            .WithName("Organization_GetLocalMembers").WithSummary("A unit's memberships (org.read on the unit or above it).");
    }
}
