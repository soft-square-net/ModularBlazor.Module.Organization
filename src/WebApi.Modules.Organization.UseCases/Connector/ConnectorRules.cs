using System.Text.Json;
using soft.square.net.WebApi.Contracts.Tenancy;
using PlatformSeed = soft.square.net.WebApi.Contracts.Tenancy.TenantSeed;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Connector;

/// <summary>A feed entry the connector has applied above its cursor: its id and when the central service wrote it.</summary>
public readonly record struct AppliedEntry(long Sequence, DateTimeOffset OccurredAt);

/// <summary>
/// Where a connector is in an org's feed. The central feed's ids come from an identity column, so ids are taken in
/// one order and committed in another: an entry can become visible after a higher id was already read. So the
/// connector does not trust the highest id it read. It keeps a <see cref="Cursor"/> below which everything is
/// settled, and the entries it applied above it (<see cref="Applied"/>). Each read starts at the cursor again, and the
/// entries applied before are skipped (<see cref="IsNew"/>), so an entry that commits late is still applied, once.
/// <para>The cursor moves past an applied entry once that entry is older than the overlap, by the connector's clock
/// against the central service's <see cref="FeedEntryDto.OccurredAt"/>. A transaction that takes longer than the
/// overlap to commit (less the clock skew between the two services) could still be missed, so keep the overlap well
/// above both.</para>
/// </summary>
public sealed class FeedWindow
{
    private readonly SortedDictionary<long, DateTimeOffset> _applied = [];

    public FeedWindow(long cursor = 0, IEnumerable<AppliedEntry>? applied = null)
    {
        Cursor = Math.Max(0, cursor);
        foreach (var entry in applied ?? []) if (entry.Sequence > Cursor) _applied[entry.Sequence] = entry.OccurredAt;
    }

    /// <summary>Every entry at or below it is settled: applied, or not for this connector.</summary>
    public long Cursor { get; private set; }

    /// <summary>The entries applied above the cursor, in order.</summary>
    public IReadOnlyList<AppliedEntry> Applied => [.. _applied.Select(p => new AppliedEntry(p.Key, p.Value))];

    /// <summary>The entry has not been handled yet: above the cursor and not applied.</summary>
    public bool IsNew(long sequence) => sequence > Cursor && !_applied.ContainsKey(sequence);

    /// <summary>The entry was handled (applied, or skipped because it does not concern this tenant).</summary>
    public void MarkApplied(long sequence, DateTimeOffset occurredAt)
    {
        if (sequence > Cursor) _applied[sequence] = occurredAt;
    }

    /// <summary>Moves the cursor past the applied entries older than <paramref name="overlap"/>.</summary>
    public void Advance(DateTimeOffset now, TimeSpan overlap)
    {
        if (_applied.Count == 0) return;
        var threshold = now - overlap;
        var settled = _applied.Where(p => p.Value <= threshold || overlap <= TimeSpan.Zero).Select(p => p.Key).ToList();
        if (settled.Count == 0) return;
        Cursor = settled.Max();
        foreach (var sequence in _applied.Keys.Where(s => s <= Cursor).ToList()) _applied.Remove(sequence);
    }

    /// <summary>The applied entries as stored with the cursor: <c>[[sequence, unixMilliseconds], ...]</c>.</summary>
    public string Serialize() => JsonSerializer.Serialize(_applied.Select(p => new[] { p.Key, p.Value.ToUnixTimeMilliseconds() }));

    public static FeedWindow Deserialize(long cursor, string? applied)
    {
        if (string.IsNullOrEmpty(applied)) return new FeedWindow(cursor);
        var pairs = JsonSerializer.Deserialize<long[][]>(applied) ?? [];
        return new FeedWindow(cursor, pairs.Where(p => p.Length == 2).Select(p => new AppliedEntry(p[0], DateTimeOffset.FromUnixTimeMilliseconds(p[1]))));
    }
}

/// <summary>A membership as grants need it.</summary>
public sealed record GrantingMembership(string Subject, IReadOnlyList<string> Permissions, DateTimeOffset From, DateTimeOffset? To);

/// <summary>
/// The standing grants the connector writes for the memberships of the tenant's units: each membership's position
/// permissions on <c>unit:{id}</c>, valid between the membership's dates. One grant source per unit
/// (<see cref="Source"/>), replaced whenever the unit's memberships or positions change, so a Department manager's
/// <c>shifts.manage</c> covers its Sections (their resources carry the Department's scope too).
/// </summary>
public static class OrgGrants
{
    public const string SourcePrefix = "org:";

    public static string Source(Guid unitId) => SourcePrefix + UnitPaths.Id(unitId);

    /// <summary>The unit id of one of this connector's grant sources, or null for another source.</summary>
    public static Guid? UnitOf(string source) =>
        source.StartsWith(SourcePrefix, StringComparison.Ordinal) && Guid.TryParseExact(source[SourcePrefix.Length..], "N", out var id) ? id : null;

    /// <summary>
    /// The grants of one unit's memberships at <paramref name="now"/>: ended memberships give none, and platform
    /// permissions (which come from platform roles) are left out, except <c>access.grants.manage</c>.
    /// </summary>
    public static IReadOnlyList<NewGrant> For(Guid unitId, IEnumerable<GrantingMembership> memberships, DateTimeOffset now)
    {
        var scope = UnitScopes.Of(unitId);
        return memberships
            .Where(m => !string.IsNullOrEmpty(m.Subject) && (m.To is null || m.To > now))
            .SelectMany(m => m.Permissions
                .Where(p => !string.IsNullOrWhiteSpace(p) && (!PlatformPermissions.IsPlatform(p) || p == PlatformPermissions.GrantsManage))
                .Select(p => new NewGrant(m.Subject, p, scope, m.From, m.To)))
            .Distinct()
            .ToList();
    }
}

/// <summary>What the connector adds to a platform seed's <c>Extra</c>: the org and the unit the tenant is for.</summary>
public sealed record SeedExtra(Guid OrgId, Guid RootUnitId);

/// <summary>The central service's deployment seeds as the platform's <see cref="PlatformSeed"/>, and the statuses back.</summary>
public static class SeedMapping
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static PlatformSeed ToPlatform(DeploymentSeedDto s) =>
        new(s.Id, s.Version, s.DeploymentId, s.TenantId, s.Name, [.. s.Hosts], s.DatabaseServer, s.DatabaseName, [.. s.Modules], [.. s.Admins],
            JsonSerializer.SerializeToElement(new SeedExtra(s.OrgId, s.RootUnitId), Json));

    public static SeedState ToState(SeedStatus status) => status switch
    {
        SeedStatus.Requested => SeedState.Requested,
        SeedStatus.Provisioning => SeedState.Provisioning,
        SeedStatus.Provisioned => SeedState.Provisioned,
        SeedStatus.Adjusted => SeedState.Adjusted,
        SeedStatus.Rejected => SeedState.Rejected,
        SeedStatus.Failed => SeedState.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static SeedReportRequest ToReport(long version, SeedStatus status, TenantPlacement? placement, string? reason) =>
        new(version, ToState(status), placement is null ? null : new PlacementDto(placement.DatabaseServer, placement.DatabaseName), reason);

    /// <summary>The seeds whose version is new or higher than in <paramref name="known"/> (seed id → version).</summary>
    public static IReadOnlyList<DeploymentSeedDto> Changed(IReadOnlyDictionary<string, long> known, IEnumerable<DeploymentSeedDto> seeds) =>
        [.. seeds.Where(s => !known.TryGetValue(s.Id, out var version) || s.Version > version)];
}
