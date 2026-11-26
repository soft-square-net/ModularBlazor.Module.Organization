namespace soft.square.net.Organization.Contracts;

/// <summary>
/// The live topic of an org, <c>organization:org:{orgId:N}</c> (the platform's module topic form
/// <c>{moduleId}:{kind}:{key}</c>), declared by the Organization module with the permission <c>org.read</c>. It is a
/// nudge: each message says that something of the org changed, and the subscriber re-reads what it shows.
/// <list type="bullet">
/// <item>The central service publishes after each committed change of the org (units, unit types, persons,
/// memberships, positions, seeds, links), in the tenant of the request that made it.</item>
/// <item>Each deployment's connector publishes after it applies a page of the org's feed to a tenant's replica, in
/// that tenant.</item>
/// </list>
/// Messages have the type <see cref="ChangedMessage"/>, the payload <see cref="OrgChange"/> and no sequence (the feed's
/// ids are not contiguous per org topic, so a client's gap check would fire on nearly every message).
/// <para>A subscriber needs <c>org.read</c> for the scope <c>org:{orgId:N}</c> or for the org's root unit
/// <c>unit:{rootUnitId:N}</c>.</para>
/// </summary>
public static class OrgLiveTopic
{
    public const string ModuleId = "organization";
    public const string Kind = "org";

    /// <summary>The message type: something of the org changed.</summary>
    public const string ChangedMessage = "org.changed";

    /// <summary><see cref="OrgChange.Type"/> when one message stands for several changes: the id is the org's.</summary>
    public const string ManyChanges = "org";

    public static string Key(Guid orgId) => UnitPaths.Id(orgId);

    public static string Topic(Guid orgId) => $"{ModuleId}:{Kind}:{Key(orgId)}";

    /// <summary>
    /// The payload for a set of changes of one org: the one changed item (<c>unit</c>, <c>person</c>...), or
    /// <see cref="ManyChanges"/> and the org id when there were several.
    /// </summary>
    public static OrgChange Summarize(Guid orgId, IReadOnlyCollection<OrgChange> changes) =>
        changes.Distinct().Take(2).ToArray() is [var one] ? one : new OrgChange(ManyChanges, Key(orgId));

    /// <summary>The change a feed entry's subject (<c>unit:{id}</c>, <c>seed:{id}</c>...) names.</summary>
    public static OrgChange FromSubject(string subject) =>
        subject.IndexOf(':') is var colon and > 0 ? new OrgChange(subject[..colon], subject[(colon + 1)..]) : new OrgChange(subject, "");
}

/// <summary>What an <see cref="OrgLiveTopic"/> message names: the kind of item (<c>unit</c>, <c>person</c>,
/// <c>membership</c>, <c>position</c>, <c>seed</c>, <c>link</c>, <c>unit-type</c>, <c>org</c>) and its id.</summary>
public sealed record OrgChange(string Type, string Id);
