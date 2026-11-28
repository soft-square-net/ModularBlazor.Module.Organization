using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>
/// Who is calling the central service (scoped; read from the request's token by the Web layer). A caller whose token
/// issuer is not one of <c>Organization:TrustedIssuers</c> has no identity here, and neither does a caller who only
/// names (<c>X-Tenant</c>) a tenant this deployment does not know.
/// </summary>
public interface ICaller
{
    /// <summary>The token's issuer (<c>iss</c>), or null when anonymous or untrusted.</summary>
    string? Issuer { get; }

    /// <summary>The token's subject (<c>sub</c>), or null when anonymous or untrusted.</summary>
    string? Subject { get; }

    /// <summary>The caller has the platform role <c>platform-admin</c>: everything, in every org.</summary>
    bool IsPlatformAdmin { get; }

    /// <summary>The caller is one of <c>Organization:ServiceClients</c> (a deployment's connector).</summary>
    bool IsServiceClient { get; }

    /// <summary>
    /// The tenant of another deployment the caller's token was issued for, when this deployment does not know it (a
    /// user of a branch deployment calling the central service); otherwise null. Such a caller is never a platform
    /// admin or a service client, and reaches only the orgs that have a tenant seed for this tenant
    /// (<see cref="OrgAccess"/>).
    /// </summary>
    string? ForeignTenantId { get; }
}

/// <summary>
/// What the caller may do in one org, from the Organization service's own data (never tenant grants):
/// <list type="bullet">
/// <item>platform admins and the org's customer admins: everything in the org;</item>
/// <item>a person of the org with an active membership whose position gives a permission: that permission on the
/// membership's unit and its whole subtree (unit admins, managers);</item>
/// <item>any person of the org, and the service clients: read.</item>
/// </list>
/// A caller from a foreign tenant (<see cref="ICaller.ForeignTenantId"/>) has none of this in an org without a tenant
/// seed for that tenant.
/// </summary>
public sealed class OrgRights(Org org, bool administers, bool isMember, bool isServiceClient, IReadOnlyList<(string UnitPath, IReadOnlyList<string> Permissions)> grants)
{
    public Org Org { get; } = org;

    /// <summary>Platform admin or customer admin of the org.</summary>
    public bool Administers { get; } = administers;

    public bool IsServiceClient { get; } = isServiceClient;

    public bool CanRead => Administers || isMember || IsServiceClient;

    /// <summary>The caller holds <paramref name="permission"/> on the unit at <paramref name="unitPath"/> (on it or an ancestor).</summary>
    public bool Can(string permission, string unitPath) =>
        Administers || grants.Any(g => g.Permissions.Contains(permission, StringComparer.Ordinal) && UnitPaths.IsSelfOrBelow(unitPath, g.UnitPath));

    /// <summary>The caller holds <paramref name="permission"/> on some unit of the org.</summary>
    public bool CanSomewhere(string permission) =>
        Administers || grants.Any(g => g.Permissions.Contains(permission, StringComparer.Ordinal));
}

/// <summary>The caller and their rights per org (scoped).</summary>
public interface IOrgAccess
{
    ICaller Caller { get; }

    bool IsSignedIn { get; }

    /// <summary>The caller's rights in an org; NotFound when there is no such org, Unauthorized when anonymous.</summary>
    Task<Result<OrgRights>> ForOrgAsync(Guid orgId, CancellationToken ct);
}

/// <summary>Builds the caller's <see cref="OrgRights"/> from the central data.</summary>
public sealed class OrgAccess(
    ICaller caller,
    IReadRepository<Org> orgs,
    IReadRepository<Person> persons,
    IReadRepository<Membership> memberships,
    IReadRepository<PositionDefinition> positions,
    IReadRepository<OrgUnit> units,
    IReadRepository<TenantSeed> seeds,
    TimeProvider clock) : IOrgAccess
{
    public ICaller Caller => caller;

    public bool IsSignedIn => caller.Subject is { Length: > 0 } && caller.Issuer is { Length: > 0 };

    public async Task<Result<OrgRights>> ForOrgAsync(Guid orgId, CancellationToken ct)
    {
        if (!IsSignedIn) return Result<OrgRights>.Unauthorized("Sign in first.");
        var org = await orgs.FirstOrDefaultAsync(new OrgByIdSpec(orgId), ct);
        if (org is null) return Result<OrgRights>.NotFound($"There is no org {orgId}.");

        // A foreign tenant says which deployment's tenant the caller signed in to: it opens only the orgs that tenant
        // belongs to. Inside them, the caller's own rows decide, as for anyone else.
        if (caller.ForeignTenantId is { } foreign && !await seeds.AnyAsync(new SeedOfOrgForTenantSpec(orgId, foreign), ct))
            return new OrgRights(org, false, false, false, []);

        var issuer = caller.Issuer!;
        var subject = caller.Subject!;
        var administers = caller.IsPlatformAdmin || org.IsAdmin(issuer, subject);
        var person = await persons.FirstOrDefaultAsync(new PersonByIdentitySpec(orgId, issuer, subject), ct);
        if (person is null || administers)
            return new OrgRights(org, administers, person is not null, caller.IsServiceClient, []);

        var active = await memberships.ListAsync(new ActivePositionsOfPersonSpec(person.Id, clock.GetUtcNow()), ct);
        if (active.Count == 0) return new OrgRights(org, false, true, caller.IsServiceClient, []);

        var positionById = (await positions.ListAsync(new PositionsByIdsSpec([.. active.Select(m => m.PositionId!.Value).Distinct()]), ct))
            .Where(p => p.OrgId == orgId).ToDictionary(p => p.Id);
        var unitById = (await units.ListAsync(new UnitsByIdsSpec(orgId, [.. active.Select(m => m.UnitId).Distinct()]), ct)).ToDictionary(u => u.Id);
        var grants = active
            .Where(m => positionById.ContainsKey(m.PositionId!.Value) && unitById.ContainsKey(m.UnitId))
            .Select(m => (unitById[m.UnitId].Path, positionById[m.PositionId!.Value].Permissions))
            .ToList();
        return new OrgRights(org, false, true, caller.IsServiceClient, grants);
    }
}

/// <summary>Results for refused calls.</summary>
public static class Refusals
{
    public static Result<T> Forbidden<T>(string what) => Result<T>.Forbidden($"You may not {what}.");

    public static Result Forbidden(string what) => Result.Forbidden($"You may not {what}.");
}
