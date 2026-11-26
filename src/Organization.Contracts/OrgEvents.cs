namespace soft.square.net.Organization.Contracts;

/// <summary>
/// The org.* integration events (v1). The central service writes each one to the org's feed
/// (<c>GET /orgs/{org}/events?after=</c>) in the same save as the change; each deployment's connector applies the feed to
/// its replicas and republishes the events locally, with the unit path, for Shifts, NewsRoom and the others.
/// <para>The data of a published version never changes: a new shape is a <c>v2</c> next to it. A subscriber may copy
/// these records instead of referencing this package.</para>
/// </summary>
public static class OrgEvents
{
    public const string UnitCreated = "org.unit-created.v1";
    public const string UnitUpdated = "org.unit-updated.v1";
    /// <summary>A unit and its whole subtree moved: every path under <see cref="UnitMoved.OldPath"/> is rebased (see <see cref="UnitPaths.Rebase"/>).</summary>
    public const string UnitMoved = "org.unit-moved.v1";
    public const string UnitRemoved = "org.unit-removed.v1";
    public const string MembershipChanged = "org.membership-changed.v1";
    public const string PersonChanged = "org.person-changed.v1";
    public const string PositionChanged = "org.position-changed.v1";
    public const string TenantSeedChanged = "org.tenant-seed-changed.v1";
    public const string TenantLinkChanged = "org.tenant-link-changed.v1";

    /// <summary>Every unit event.</summary>
    public const string AnyUnitEvent = "org.unit-*.v1";

    /// <summary>Every org event, any version.</summary>
    public const string Any = "org.#";

    public static IReadOnlyList<string> All { get; } =
        [UnitCreated, UnitUpdated, UnitMoved, UnitRemoved, MembershipChanged, PersonChanged, PositionChanged, TenantSeedChanged, TenantLinkChanged];

    public static string UnitSubject(Guid id) => $"unit:{UnitPaths.Id(id)}";
    public static string MembershipSubject(Guid id) => $"membership:{UnitPaths.Id(id)}";
    public static string PersonSubject(Guid id) => $"person:{UnitPaths.Id(id)}";
    public static string PositionSubject(Guid id) => $"position:{UnitPaths.Id(id)}";
    public static string SeedSubject(string id) => $"seed:{id}";
    public static string LinkSubject(Guid id) => $"link:{UnitPaths.Id(id)}";
}

/// <summary>
/// A unit as the unit events carry it. <see cref="Categories"/>: Content post category ids, or <c>["*"]</c>
/// (<see cref="UnitCategories.All"/>) for a Section that serves every category.
/// </summary>
public sealed record OrgUnitSnapshot(
    Guid UnitId, Guid OrgId, Guid? ParentId, string TypeKey, string Path, string NameEn, string NameAr, IReadOnlyList<string> Tags,
    string? TimeZone, IReadOnlyList<string> Categories, TenantBindingKind Binding, string? SeedId, int Version);

/// <summary>Data of <see cref="OrgEvents.UnitCreated"/>.</summary>
public sealed record UnitCreated(OrgUnitSnapshot Unit);

/// <summary>Data of <see cref="OrgEvents.UnitUpdated"/>: names, tags, time zone, categories or tenant binding changed.</summary>
public sealed record UnitUpdated(OrgUnitSnapshot Unit);

/// <summary>Data of <see cref="OrgEvents.UnitMoved"/>.</summary>
public sealed record UnitMoved(Guid UnitId, Guid OrgId, Guid? OldParentId, Guid NewParentId, string OldPath, string NewPath, int Version);

/// <summary>Data of <see cref="OrgEvents.UnitRemoved"/> (a unit is removed only once it has no children and no memberships).</summary>
public sealed record UnitRemoved(Guid UnitId, Guid OrgId, string Path);

/// <summary>
/// Data of <see cref="OrgEvents.MembershipChanged"/>: the membership as it is now (<see cref="Removed"/>: it no longer
/// exists). <see cref="Permissions"/> are its position's, which the connector turns into standing grants on <c>unit:{UnitId}</c>.
/// </summary>
public sealed record MembershipChanged(
    Guid MembershipId, Guid OrgId, Guid PersonId, string Issuer, string Subject, Guid UnitId, string UnitPath, Guid? PositionId,
    string? PositionKey, IReadOnlyList<string> Permissions, DateTimeOffset From, DateTimeOffset? To, bool Primary, bool Removed);

/// <summary>Data of <see cref="OrgEvents.PersonChanged"/>.</summary>
public sealed record PersonChanged(Guid PersonId, Guid OrgId, string Issuer, string Subject, string DisplayName, string? Email, bool IsEmployee);

/// <summary>Data of <see cref="OrgEvents.PositionChanged"/>: the grants of every membership with this position follow its permissions.</summary>
public sealed record PositionChanged(Guid PositionId, Guid OrgId, string Key, string NameEn, string NameAr, IReadOnlyList<string> Permissions);

/// <summary>Data of <see cref="OrgEvents.TenantSeedChanged"/>: a seed was created, edited (new version) or reported on.</summary>
public sealed record TenantSeedChanged(
    string SeedId, Guid OrgId, Guid RootUnitId, long Version, string DeploymentId, string TenantId, SeedState Status, long? ReportedVersion);

/// <summary>Data of <see cref="OrgEvents.TenantLinkChanged"/>.</summary>
public sealed record TenantLinkChanged(
    Guid LinkId, Guid OrgId, string FromSeedId, Guid FromUnitId, string ToSeedId, Guid ToUnitId, TenantLinkState State);
