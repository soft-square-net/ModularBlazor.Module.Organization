namespace soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;

/// <summary>
/// One change of an org, as an <c>org.*</c> v1 event (the type and its JSON data): the feed's outbox. It is written in
/// the same save as the change, so the feed has every committed change and nothing that rolled back. Deployments'
/// connectors read it in <see cref="EntityBase{TId}.Id"/> order (<c>GET /orgs/{org}/events?after=</c>).
/// </summary>
public sealed class FeedEntry : AggregateRoot<long>
{
    public const int TypeMaxLength = 100;
    public const int SubjectMaxLength = 100;

    private FeedEntry() { } // EF Core

    public Guid OrgId { get; private set; }
    public string Type { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string? UnitPath { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The event's data as JSON (web defaults, camelCase).</summary>
    public string Data { get; private set; } = "";

    public static FeedEntry Create(Guid orgId, string type, string subject, string? unitPath, string data, DateTimeOffset now) => new()
    {
        OrgId = orgId,
        Type = Rules.Required(type, "An event type", TypeMaxLength),
        Subject = Rules.Required(subject, "An event subject", SubjectMaxLength),
        UnitPath = unitPath,
        Data = data,
        OccurredAt = now
    };
}
