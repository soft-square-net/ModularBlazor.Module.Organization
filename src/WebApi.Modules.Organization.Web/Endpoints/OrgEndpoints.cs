using soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;

namespace soft.square.net.WebApi.Modules.Organization.Web.Endpoints;

/// <summary>/orgs: the customers.</summary>
public static class OrgEndpoints
{
    public static void Map(IEndpointRouteBuilder orgs)
    {
        orgs.MapGet("/", async Task<Results<Ok<IReadOnlyList<OrgDto>>, ProblemHttpResult>> (ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListOrgsQuery(), ct)).ToOkResult())
            .WithName("Organization_ListOrgs").WithSummary("The orgs the caller administers or belongs to (all of them for a platform admin).");

        orgs.MapGet("/{orgId:guid}", async Task<Results<Ok<OrgDto>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new GetOrgQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_GetOrg");

        orgs.MapPost("/", async Task<Results<Created<OrgDto>, ProblemHttpResult>> (CreateOrgRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateOrgCommand(request), ct)).ToCreatedResult(o => $"orgs/{o.Id}"))
            .WithName("Organization_CreateOrg").WithSummary("A new customer with its root unit, the default unit types and positions (platform admins).");

        orgs.MapPut("/{orgId:guid}", async Task<Results<Ok<OrgDto>, ProblemHttpResult>> (Guid orgId, UpdateOrgRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateOrgCommand(orgId, request), ct)).ToOkResult())
            .WithName("Organization_UpdateOrg");

        orgs.MapPut("/{orgId:guid}/admins", async Task<Results<Ok<OrgDto>, ProblemHttpResult>> (Guid orgId, SetOrgAdminsRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new SetOrgAdminsCommand(orgId, request), ct)).ToOkResult())
            .WithName("Organization_SetOrgAdmins").WithSummary("Replaces the org's customer admins.");
    }
}
