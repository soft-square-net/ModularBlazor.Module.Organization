namespace soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;

/// <summary>An org's feed entries after a cursor, in order, at most <paramref name="take"/>.</summary>
public sealed class FeedAfterSpec : Specification<FeedEntry>
{
    public FeedAfterSpec(Guid orgId, long after, int take) =>
        Query.Where(e => e.OrgId == orgId && e.Id > after).OrderBy(e => e.Id).Take(take);
}
