using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Feed;

/// <summary>
/// The org's changes after a cursor, in order (connectors, customer admins, platform admins). A connector keeps the
/// last <see cref="FeedPage.Next"/> per org and asks again from it.
/// </summary>
public sealed record ReadFeedQuery(Guid OrgId, long After, int Take) : IQuery<Result<FeedPage>>;

public sealed class ReadFeedHandler(IOrgAccess access, IReadRepository<FeedEntry> entries, IOptions<OrganizationOptions> options)
    : IQueryHandler<ReadFeedQuery, Result<FeedPage>>
{
    public async ValueTask<Result<FeedPage>> Handle(ReadFeedQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<FeedPage>();
        if (!rights.Value.Administers && !rights.Value.IsServiceClient) return Refusals.Forbidden<FeedPage>("read this org's feed");

        var take = Math.Clamp(query.Take, 1, Math.Max(1, options.Value.MaxFeedPage));
        var after = Math.Max(0, query.After);
        var page = await entries.ListAsync(new FeedAfterSpec(query.OrgId, after, take), ct);
        return new FeedPage([.. page.Select(e => e.ToDto())], page.Count == 0 ? after : page[^1].Id, page.Count == take);
    }
}

/// <summary>The org's links between sibling tenants (a read-only skeleton for now).</summary>
public sealed record ListLinksQuery(Guid OrgId) : IQuery<Result<IReadOnlyList<TenantLinkDto>>>;

public sealed class ListLinksHandler(IOrgAccess access, IReadRepository<TenantLink> links) : IQueryHandler<ListLinksQuery, Result<IReadOnlyList<TenantLinkDto>>>
{
    public async ValueTask<Result<IReadOnlyList<TenantLinkDto>>> Handle(ListLinksQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<TenantLinkDto>>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<IReadOnlyList<TenantLinkDto>>("read this org");
        return (await links.ListAsync(new LinksOfOrgSpec(query.OrgId), ct)).Select(l => l.ToDto()).ToList();
    }
}
