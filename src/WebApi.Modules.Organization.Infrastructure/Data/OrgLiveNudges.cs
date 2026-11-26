using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;
using TenantSeed = soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate.TenantSeed;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;
using OrgEntity = soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate.Org;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;

/// <summary>
/// The central service's <see cref="OrgLiveTopic"/> messages: before a save, which items of which orgs it changes
/// (from the change tracker, so no use case can forget one); after the commit, one message per org through the host's
/// <see cref="ILiveNotifier"/>, in the tenant of the request. Best effort: without a notifier or a tenant (a request
/// outside any tenant), or when the publish fails, nothing is sent and the save stands. The connectors of the
/// deployments publish the same topic in their own tenants once they apply the change.
/// </summary>
public static class OrgLiveNudges
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The items the pending save adds, changes or removes, by org.</summary>
    public static IReadOnlyDictionary<Guid, List<OrgChange>> Pending(ChangeTracker tracker)
    {
        var changes = new Dictionary<Guid, List<OrgChange>>();
        foreach (var entry in tracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (Describe(entry.Entity) is not var (orgId, change)) continue;
            if (!changes.TryGetValue(orgId, out var list)) changes[orgId] = list = [];
            if (!list.Contains(change)) list.Add(change);
        }
        return changes;
    }

    /// <summary>The org and item of a central row; null for rows that are not an org's item (the feed itself, owned parts).</summary>
    public static (Guid OrgId, OrgChange Change)? Describe(object entity) => entity switch
    {
        OrgEntity o => (o.Id, new OrgChange("org", UnitPaths.Id(o.Id))),
        UnitType t => (t.OrgId, new OrgChange("unit-type", UnitPaths.Id(t.Id))),
        OrgUnit u => (u.OrgId, new OrgChange("unit", UnitPaths.Id(u.Id))),
        Person p => (p.OrgId, new OrgChange("person", UnitPaths.Id(p.Id))),
        Membership m => (m.OrgId, new OrgChange("membership", UnitPaths.Id(m.Id))),
        PositionDefinition p => (p.OrgId, new OrgChange("position", UnitPaths.Id(p.Id))),
        TenantSeed s => (s.OrgId, new OrgChange("seed", s.Id)),
        TenantLink l => (l.OrgId, new OrgChange("link", UnitPaths.Id(l.Id))),
        _ => ((Guid, OrgChange)?)null
    };

    /// <summary>Publishes one message per org, after the commit.</summary>
    public static async Task PublishAsync(IServiceProvider services, IReadOnlyDictionary<Guid, List<OrgChange>> changes, CancellationToken ct)
    {
        if (changes.Count == 0 || services.GetService<ILiveNotifier>() is not { } live) return;
        if (services.GetService<ITenantContext>()?.Tenant is null) return; // the notifier sends in the current tenant
        foreach (var (orgId, list) in changes)
        {
            try
            {
                var payload = JsonSerializer.SerializeToElement(OrgLiveTopic.Summarize(orgId, list), Json);
                await live.PublishAsync(OrgLiveTopic.Topic(orgId), OrgLiveTopic.ChangedMessage, payload, sequence: null, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                services.GetService<ILoggerFactory>()?.CreateLogger("soft.square.net.WebApi.Modules.Organization.Infrastructure")
                    .LogDebug(e, "Live message for org {OrgId} was not sent.", orgId);
            }
        }
    }
}
