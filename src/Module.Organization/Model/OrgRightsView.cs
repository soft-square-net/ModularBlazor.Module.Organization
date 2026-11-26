using soft.square.net.WebApi.Modules.Organization.Client;

namespace soft.square.net.Module.Organization.Model;

/// <summary>The Organization permissions (the same names as the module's manifest and the central service).</summary>
public static class OrgPermissionNames
{
    public const string Read = "org.read";
    public const string UnitsManage = "org.units.manage";
    public const string MembersManage = "org.members.manage";
    public const string SeedsManage = "org.seeds.manage";
}

/// <summary>Who the signed-in user is for the central service: the token's issuer and subject, and the platform-admin role.</summary>
public sealed record OrgCaller(string? Issuer, string? Subject, bool IsPlatformAdmin)
{
    public static OrgCaller Anonymous { get; } = new(null, null, false);

    public bool Is(string issuer, string subject) =>
        Subject is { Length: > 0 } && string.Equals(Subject, subject, StringComparison.Ordinal)
        && (Issuer is null || SameIssuer(Issuer, issuer));

    /// <summary>Issuers compare without a trailing slash ("http://id/" and "http://id" are the same).</summary>
    public static bool SameIssuer(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// What the signed-in user may do in one org, worked out the way the central service does it (its OrgRights), from data
/// the user can read: platform admins and the org's customer admins may do everything; a person of the org holds the
/// permissions of the positions of their active memberships on that unit and its whole subtree. It only decides which
/// buttons the pages show: the API checks every call itself.
/// </summary>
public sealed class OrgRightsView
{
    private readonly IReadOnlyList<(string UnitPath, IReadOnlyList<string> Permissions)> _grants;

    private OrgRightsView(bool administers, PersonDto? person, IReadOnlyList<(string, IReadOnlyList<string>)> grants)
    {
        Administers = administers;
        Person = person;
        _grants = grants;
    }

    public static OrgRightsView None { get; } = new(false, null, []);

    /// <summary>Platform admin or customer admin of the org.</summary>
    public bool Administers { get; }

    /// <summary>The user as a person of the org, when they are one.</summary>
    public PersonDto? Person { get; }

    public static OrgRightsView For(
        OrgCaller caller, OrgDto org, IEnumerable<PersonDto> persons, IEnumerable<MembershipDto> memberships,
        IEnumerable<PositionDto> positions, IEnumerable<OrgUnitDto> units, DateTimeOffset now)
    {
        var administers = caller.IsPlatformAdmin || org.Admins.Any(a => caller.Is(a.Issuer, a.Subject));
        var person = persons.FirstOrDefault(p => caller.Is(p.Issuer, p.Subject));
        if (person is null || administers) return new OrgRightsView(administers, person, []);

        var positionById = positions.ToDictionary(p => p.Id);
        var unitById = units.ToDictionary(u => u.Id);
        var grants = memberships
            .Where(m => m.PersonId == person.Id && m.PositionId is not null && m.From <= now && (m.To is null || m.To > now))
            .Where(m => positionById.ContainsKey(m.PositionId!.Value) && unitById.ContainsKey(m.UnitId))
            .Select(m => (unitById[m.UnitId].Path, positionById[m.PositionId!.Value].Permissions))
            .ToList();
        return new OrgRightsView(false, person, grants);
    }

    /// <summary>The user holds <paramref name="permission"/> on the unit at <paramref name="unitPath"/> (on it or an ancestor).</summary>
    public bool Can(string permission, string unitPath) =>
        Administers || _grants.Any(g => g.Permissions.Contains(permission, StringComparer.Ordinal) && UnitPath.IsSelfOrBelow(unitPath, g.UnitPath));

    /// <summary>The user holds <paramref name="permission"/> on some unit of the org.</summary>
    public bool CanSomewhere(string permission) =>
        Administers || _grants.Any(g => g.Permissions.Contains(permission, StringComparer.Ordinal));
}
