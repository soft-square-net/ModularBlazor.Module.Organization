using soft.square.net.WebApi.Modules.Organization.UseCases.Feed;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;

namespace soft.square.net.WebApi.Modules.Organization.Web.Endpoints;

/// <summary>Tenant seeds: /orgs/{orgId}/seeds for the org's admins, /seeds for the deployments' connectors.</summary>
public static class SeedEndpoints
{
    public static void MapOrg(IEndpointRouteBuilder seeds)
    {
        seeds.MapGet("/", async Task<Results<Ok<IReadOnlyList<TenantSeedDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListSeedsQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_ListSeeds").WithSummary("The org's tenant seeds with their status as the hosts report it.");

        seeds.MapPost("/", async Task<Results<Created<TenantSeedDto>, ProblemHttpResult>> (Guid orgId, CreateSeedRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new CreateSeedCommand(orgId, request), ct)).ToCreatedResult(s => $"seeds/{s.Id}"))
            .WithName("Organization_CreateSeed").WithSummary("A tenant for a unit; its admins become unit admins of it (customer admins).");

        seeds.MapPut("/{seedId}", async Task<Results<Ok<TenantSeedDto>, ProblemHttpResult>> (Guid orgId, string seedId, UpdateSeedRequest request, ISender sender, CancellationToken ct) =>
                (await sender.Send(new UpdateSeedCommand(orgId, seedId, request), ct)).ToOkResult())
            .WithName("Organization_UpdateSeed").WithSummary("A new version of the seed, which the host reconciles again.");
    }

    public static void MapDeployment(IEndpointRouteBuilder seeds)
    {
        seeds.MapGet("/", async Task<Results<Ok<IReadOnlyList<DeploymentSeedDto>>, ProblemHttpResult>> (string deployment, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListDeploymentSeedsQuery(deployment), ct)).ToOkResult())
            .WithName("Organization_ListDeploymentSeeds").WithSummary("The seeds of a deployment, as its connector pulls them (service clients).");

        seeds.MapPost("/{seedId}/reports", async Task<Results<NoContent, ProblemHttpResult>> (string seedId, SeedReportRequest report, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ReportSeedCommand(seedId, report), ct)).ToNoContentResult())
            .WithName("Organization_ReportSeed").WithSummary("The host's outcome for one version of a seed (service clients).");
    }
}

/// <summary>/orgs/{orgId}/events (the feed) and /orgs/{orgId}/links.</summary>
public static class FeedEndpoints
{
    public static void Map(IEndpointRouteBuilder org)
    {
        org.MapGet("/events", async Task<Results<Ok<FeedPage>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct, long after = 0, int take = 200) =>
                (await sender.Send(new ReadFeedQuery(orgId, after, take), ct)).ToOkResult())
            .WithName("Organization_ReadFeed").WithSummary("The org's org.* events after a cursor, in order (connectors and customer admins).");

        org.MapGet("/links", async Task<Results<Ok<IReadOnlyList<TenantLinkDto>>, ProblemHttpResult>> (Guid orgId, ISender sender, CancellationToken ct) =>
                (await sender.Send(new ListLinksQuery(orgId), ct)).ToOkResult())
            .WithName("Organization_ListLinks").WithSummary("The org's links between sibling tenants (skeleton: states only).");
    }
}
