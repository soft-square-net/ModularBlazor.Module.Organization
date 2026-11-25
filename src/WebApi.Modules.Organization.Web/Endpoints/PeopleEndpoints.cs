using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Persons;
using soft.square.net.WebApi.Modules.Organization.UseCases.Positions;

namespace soft.square.net.WebApi.Modules.Organization.Web.Endpoints;

/// <summary>/orgs/{orgId}/persons.</summary>
public static class PersonEndpoints
{
    public static void Map(IEndpointRouteBuilder persons)
    {
        persons.MapGet("/", async Task<Results<Ok<IReadOnlyList<PersonDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListPersonsQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_ListPersons");

        persons.MapPost("/", async Task<Results<Created<PersonDto>, ProblemHttpResult>> (Guid orgId, CreatePersonRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreatePersonCommand(orgId, request), ct)).ToCreatedResult(p => $"persons/{p.Id}"))
            .WithName("Organization_CreatePerson");

        persons.MapPut("/{personId:guid}", async Task<Results<Ok<PersonDto>, ProblemHttpResult>> (Guid orgId, Guid personId, UpdatePersonRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdatePersonCommand(orgId, personId, request), ct)).ToOkResult())
            .WithName("Organization_UpdatePerson");
    }
}

/// <summary>/orgs/{orgId}/memberships.</summary>
public static class MembershipEndpoints
{
    public static void Map(IEndpointRouteBuilder memberships)
    {
        memberships.MapGet("/", async Task<Results<Ok<IReadOnlyList<MembershipDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct, Guid? unitId = null, Guid? personId = null) =>
                (await sender.Send(new ListMembershipsQuery(orgId, unitId, personId), ct)).ToOkResult())
            .WithName("Organization_ListMemberships").WithSummary("The org's memberships, optionally of one unit and/or one person.");

        memberships.MapPost("/", async Task<Results<Created<MembershipDto>, ProblemHttpResult>> (Guid orgId, CreateMembershipRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateMembershipCommand(orgId, request), ct)).ToCreatedResult(m => $"memberships/{m.Id}"))
            .WithName("Organization_CreateMembership");

        memberships.MapPut("/{membershipId:guid}", async Task<Results<Ok<MembershipDto>, ProblemHttpResult>> (Guid orgId, Guid membershipId, UpdateMembershipRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateMembershipCommand(orgId, membershipId, request), ct)).ToOkResult())
            .WithName("Organization_UpdateMembership");

        memberships.MapDelete("/{membershipId:guid}", async Task<Results<NoContent, ProblemHttpResult>> (Guid orgId, Guid membershipId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new RemoveMembershipCommand(orgId, membershipId), ct)).ToNoContentResult())
            .WithName("Organization_RemoveMembership");
    }
}

/// <summary>/orgs/{orgId}/positions.</summary>
public static class PositionEndpoints
{
    public static void Map(IEndpointRouteBuilder positions)
    {
        positions.MapGet("/", async Task<Results<Ok<IReadOnlyList<PositionDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListPositionsQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_ListPositions");

        positions.MapPost("/", async Task<Results<Created<PositionDto>, ProblemHttpResult>> (Guid orgId, CreatePositionRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreatePositionCommand(orgId, request), ct)).ToCreatedResult(p => $"positions/{p.Id}"))
            .WithName("Organization_CreatePosition");

        positions.MapPut("/{positionId:guid}", async Task<Results<Ok<PositionDto>, ProblemHttpResult>> (Guid orgId, Guid positionId, UpdatePositionRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdatePositionCommand(orgId, positionId, request), ct)).ToOkResult())
            .WithName("Organization_UpdatePosition");
    }
}
