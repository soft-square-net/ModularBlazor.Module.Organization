namespace soft.square.net.Organization.Contracts;

/// <summary>
/// Unit paths. A path lists the ids of a unit's ancestors and of the unit itself, root first, each one after a slash:
/// <c>/{orgRootId}/{sectorId}/{departmentId}</c>. Ids are GUIDs in the 32-digit form (<c>"N"</c>), so a path is
/// plain ASCII and one unit's path is a prefix of its descendants' paths (with the trailing slash).
/// </summary>
public static class UnitPaths
{
    public const char Separator = '/';

    /// <summary>The text of a unit id in a path and in a scope.</summary>
    public static string Id(Guid unitId) => unitId.ToString("N");

    /// <summary>The path of a root unit.</summary>
    public static string Root(Guid unitId) => Separator + Id(unitId);

    /// <summary>The path of a child of the unit at <paramref name="parentPath"/>.</summary>
    public static string Child(string parentPath, Guid unitId) => parentPath + Separator + Id(unitId);

    /// <summary>The unit ids on a path, root first (the last one is the unit itself).</summary>
    public static IReadOnlyList<string> Ids(string? path) =>
        string.IsNullOrEmpty(path) ? [] : path.Split(Separator, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>True when <paramref name="path"/> is <paramref name="ancestorPath"/> itself or below it.</summary>
    public static bool IsSelfOrBelow(string path, string ancestorPath) =>
        string.Equals(path, ancestorPath, StringComparison.Ordinal)
        || path.StartsWith(ancestorPath + Separator, StringComparison.Ordinal);

    /// <summary>
    /// The path of a unit after a move: the part of <paramref name="path"/> under <paramref name="oldPrefix"/> moved
    /// under <paramref name="newPrefix"/>. Used by every holder of a replica when it applies <c>org.unit-moved.v1</c>.
    /// </summary>
    public static string Rebase(string path, string oldPrefix, string newPrefix)
    {
        if (!IsSelfOrBelow(path, oldPrefix))
            throw new ArgumentException($"'{path}' is not under '{oldPrefix}'.", nameof(path));
        return newPrefix + path[oldPrefix.Length..];
    }
}

/// <summary>
/// The grant scopes of a unit. A grant on <c>unit:{id}</c> covers that unit and every unit below it, because a
/// resource of a unit carries the scopes of the unit and of all its ancestors (<see cref="For"/>), and
/// <c>IAccessScopes.HasPermissionAsync(permission, resourceScopes)</c> matches any one of them. So a Department
/// manager's <c>shifts.manage</c> on <c>unit:{department}</c> covers the shifts of its Sections.
/// </summary>
public static class UnitScopes
{
    public const string Kind = "unit";

    /// <summary>The scope of one unit: <c>unit:{id}</c>.</summary>
    public static string Of(Guid unitId) => Of(UnitPaths.Id(unitId));

    /// <summary>The scope of one unit id as it appears in a path.</summary>
    public static string Of(string unitId) => $"{Kind}:{unitId}";

    /// <summary>The scopes of a unit and all its ancestors, from a path: <c>["unit:{root}", ..., "unit:{self}"]</c>.</summary>
    public static IReadOnlyList<string> For(string? path) => [.. UnitPaths.Ids(path).Select(Of)];
}
