namespace soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;

/// <summary>
/// A position of an org (Manager, Unit admin, a planning desk's Moderator, ...) and the permissions it gives, from
/// the platform's permission catalog. A membership with a position gives its person those permissions on the unit's
/// scope, which covers the unit's subtree: in each tenant as standing grants (written by the connector), and on the
/// central service itself for the <c>org.*</c> permissions.
/// </summary>
public sealed class PositionDefinition : AggregateRoot<Guid>
{
    public const string UnitAdminKey = "unit-admin";
    public const string ManagerKey = "manager";

    private List<string> _permissions = [];

    private PositionDefinition() { } // EF Core

    public Guid OrgId { get; private set; }
    public string Key { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public IReadOnlyList<string> Permissions => _permissions;
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public static PositionDefinition Create(Guid orgId, string key, string? nameEn, string? nameAr, IEnumerable<string>? permissions, DateTimeOffset now)
    {
        var position = new PositionDefinition { Id = Guid.CreateVersion7(), OrgId = orgId, Key = Rules.Key(key, "A position's key"), UpdatedAt = now };
        position.Set(nameEn, nameAr, permissions);
        return position;
    }

    public void Update(string? nameEn, string? nameAr, IEnumerable<string>? permissions, DateTimeOffset now)
    {
        Set(nameEn, nameAr, permissions);
        UpdatedAt = now;
        Version++;
    }

    public bool Gives(string permission) => _permissions.Contains(permission, StringComparer.Ordinal);

    /// <summary>
    /// The positions of a new org: Unit admin (manages the units, members and seeds of its subtree; a seed's admins
    /// get it on the seed's root unit) and Manager (manages the subtree's units and shifts).
    /// </summary>
    public static IReadOnlyList<PositionDefinition> Defaults(Guid orgId, DateTimeOffset now) =>
    [
        Create(orgId, UnitAdminKey, "Unit admin", "مسؤول الوحدة",
            [OrgPermissions.UnitsManage, OrgPermissions.MembersManage, OrgPermissions.SeedsManage], now),
        Create(orgId, ManagerKey, "Manager", "مدير", [OrgPermissions.UnitsManage, "shifts.manage"], now)
    ];

    private void Set(string? nameEn, string? nameAr, IEnumerable<string>? permissions)
    {
        (NameEn, NameAr) = Rules.Names(nameEn, nameAr, "A position");
        _permissions = Rules.Permissions(permissions);
    }
}
