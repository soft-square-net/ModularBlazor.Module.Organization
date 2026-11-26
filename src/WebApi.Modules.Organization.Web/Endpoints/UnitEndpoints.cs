using soft.square.net.WebApi.Modules.Organization.UseCases.UnitTypes;
using soft.square.net.WebApi.Modules.Organization.UseCases.Units;

namespace soft.square.net.WebApi.Modules.Organization.Web.Endpoints;

/// <summary>/orgs/{orgId}/unit-types.</summary>
public static class UnitTypeEndpoints
{
    public static void Map(IEndpointRouteBuilder types)
    {
        types.MapGet("/", async Task<Results<Ok<IReadOnlyList<UnitTypeDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListUnitTypesQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_ListUnitTypes");

        types.MapPost("/", async Task<Results<Created<UnitTypeDto>, ProblemHttpResult>> (Guid orgId, CreateUnitTypeRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateUnitTypeCommand(orgId, request), ct)).ToCreatedResult(t => $"unit-types/{t.Id}"))
            .WithName("Organization_CreateUnitType").WithSummary("A new unit type with the types it may sit under (customer admins).");
    }
}

/// <summary>/orgs/{orgId}/units: the unit tree.</summary>
public static class UnitEndpoints
{
    public static void Map(IEndpointRouteBuilder units)
    {
        units.MapGet("/", async Task<Results<Ok<IReadOnlyList<OrgUnitDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListUnitsQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_ListUnits").WithSummary("The org's units in tree order.");

        units.MapGet("/{unitId:guid}", async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> (Guid orgId, Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetUnitQuery(orgId, unitId), ct)).ToOkResult())
            .WithName("Organization_GetUnit");

        units.MapGet("/{unitId:guid}/tenant", async Task<Results<Ok<UnitTenantDto>, ProblemHttpResult>> (Guid orgId, Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetUnitTenantQuery(orgId, unitId), ct)).ToOkResult())
            .WithName("Organization_GetUnitTenant").WithSummary("The tenant the unit lives in: its own or its nearest ancestor's.");

        units.MapPost("/", async Task<Results<Created<OrgUnitDto>, ProblemHttpResult>> (Guid orgId, CreateUnitRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateUnitCommand(orgId, request), ct)).ToCreatedResult(u => $"units/{u.Id}"))
            .WithName("Organization_CreateUnit").WithSummary("A unit under a parent; a Section needs at least one post category.");

        units.MapPut("/{unitId:guid}", async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> (Guid orgId, Guid unitId, UpdateUnitRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateUnitCommand(orgId, unitId, request), ct)).ToOkResult())
            .WithName("Organization_UpdateUnit");

        units.MapPost("/{unitId:guid}/move", async Task<Results<Ok<OrgUnitDto>, ProblemHttpResult>> (Guid orgId, Guid unitId, MoveUnitRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new MoveUnitCommand(orgId, unitId, request), ct)).ToOkResult())
            .WithName("Organization_MoveUnit").WithSummary("Moves the unit and its subtree under another parent.");

        units.MapDelete("/{unitId:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid orgId, Guid unitId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemoveUnitCommand(orgId, unitId), ct)).ToNoContentResult())
            .WithName("Organization_RemoveUnit").WithSummary("Removes a unit with no children, no members and no tenant.");
    }
}
