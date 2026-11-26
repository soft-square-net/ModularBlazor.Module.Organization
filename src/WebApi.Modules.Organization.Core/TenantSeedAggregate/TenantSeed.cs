using System.Text.RegularExpressions;

namespace soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;

/// <summary>
/// A tenant a deployment should have, owned by a unit of the org (<see cref="RootUnitId"/>, bound to it with
/// <c>OwnTenant</c>). Hosts pull the seeds of their deployment and report back, per <see cref="Version"/>:
/// Requested → Provisioning → Provisioned | Adjusted (another placement) | Rejected (reason) | Failed (retried).
/// Every edit is a new version, and the host reconciles it again. <see cref="History"/> keeps the latest reports.
/// </summary>
public sealed partial class TenantSeed : AggregateRoot<string>
{
    public const int IdMaxLength = 64;
    public const int HistoryLimit = 50;

    private List<string> _hosts = [];
    private List<string> _modules = [];
    private List<Guid> _adminPersonIds = [];
    private readonly List<SeedStatusEntry> _history = [];

    private TenantSeed() { } // EF Core

    public Guid OrgId { get; private set; }
    public Guid RootUnitId { get; private set; }
    public long Version { get; private set; }
    public string DeploymentId { get; private set; } = "";
    public string TenantId { get; private set; } = "";
    public string Name { get; private set; } = "";
    public IReadOnlyList<string> Hosts => _hosts;
    public string DatabaseServer { get; private set; } = "";
    public string DatabaseName { get; private set; } = "";
    public IReadOnlyList<string> Modules => _modules;
    public IReadOnlyList<Guid> AdminPersonIds => _adminPersonIds;

    public SeedState Status { get; private set; }
    public string? Reason { get; private set; }
    public long? ReportedVersion { get; private set; }
    public string? ActualDatabaseServer { get; private set; }
    public string? ActualDatabaseName { get; private set; }
    public IReadOnlyList<SeedStatusEntry> History => _history;

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Bumped by every change, reports included; a concurrency token (<see cref="Version"/> grows with edits only).</summary>
    public int Revision { get; private set; }

    public static TenantSeed Create(Guid orgId, Guid rootUnitId, string? deploymentId, string? tenantId, string? name, IEnumerable<string>? hosts,
        string? databaseServer, string? databaseName, IEnumerable<string>? modules, IEnumerable<Guid>? adminPersonIds, DateTimeOffset now)
    {
        var seed = new TenantSeed
        {
            Id = Guid.CreateVersion7().ToString("N"),
            OrgId = orgId,
            RootUnitId = rootUnitId,
            DeploymentId = Rules.Required(deploymentId, "A deployment id", Rules.ItemMaxLength),
            TenantId = ValidTenantId(tenantId),
            CreatedAt = now
        };
        seed.Set(name, hosts, databaseServer, databaseName, modules, adminPersonIds);
        seed.NewVersion(now);
        return seed;
    }

    /// <summary>Edits the seed: a new version, Requested again, which the host reconciles on its next cycle.</summary>
    public void Edit(long expectedVersion, string? name, IEnumerable<string>? hosts, string? databaseServer, string? databaseName,
        IEnumerable<string>? modules, IEnumerable<Guid>? adminPersonIds, DateTimeOffset now)
    {
        if (expectedVersion != Version) throw new DomainException($"The seed is at version {Version}, not {expectedVersion}; reload it and try again.");
        Set(name, hosts, databaseServer, databaseName, modules, adminPersonIds);
        NewVersion(now);
    }

    /// <summary>
    /// A host's outcome for one version. A report on an older version is kept in the history only; a report on a
    /// version that does not exist yet is refused.
    /// </summary>
    public void Report(long version, SeedState status, string? actualServer, string? actualDatabase, string? reason, DateTimeOffset now)
    {
        if (version < 1 || version > Version) throw new DomainException($"The seed has no version {version}.");
        if (status == SeedState.Requested) throw new DomainException("A host reports Provisioning, Provisioned, Adjusted, Rejected or Failed.");
        var why = Rules.Optional(reason, "A reason", 1000);
        if (status is SeedState.Rejected or SeedState.Failed && why is null) throw new DomainException($"A {status} report needs a reason.");
        string? server = null, database = null;
        if (status == SeedState.Adjusted)
        {
            server = Rules.Required(actualServer, "The actual database server", Rules.ItemMaxLength);
            database = Rules.Required(actualDatabase, "The actual database name", Rules.ItemMaxLength);
        }

        AddHistory(new SeedStatusEntry(now, version, status, why, server, database));
        Revision++;
        UpdatedAt = now;
        if (version < Version) return;
        Status = status;
        Reason = why;
        ReportedVersion = version;
        if (status == SeedState.Adjusted) (ActualDatabaseServer, ActualDatabaseName) = (server, database);
        else if (status == SeedState.Provisioned) (ActualDatabaseServer, ActualDatabaseName) = (DatabaseServer, DatabaseName);
    }

    private void Set(string? name, IEnumerable<string>? hosts, string? databaseServer, string? databaseName, IEnumerable<string>? modules, IEnumerable<Guid>? adminPersonIds)
    {
        Name = Rules.Required(name, "A tenant's name", Rules.NameMaxLength);
        _hosts = Rules.List(hosts, "Hosts", lowercase: true);
        DatabaseServer = Rules.Required(databaseServer, "A database server", Rules.ItemMaxLength);
        DatabaseName = Rules.Required(databaseName, "A database name", Rules.ItemMaxLength);
        _modules = Rules.List(modules, "Modules", lowercase: true);
        _adminPersonIds = (adminPersonIds ?? []).Distinct().ToList();
        if (_adminPersonIds.Count > Rules.ListMaxItems) throw new DomainException($"A seed has at most {Rules.ListMaxItems} admins.");
    }

    private void NewVersion(DateTimeOffset now)
    {
        Version++;
        Revision++;
        Status = SeedState.Requested;
        Reason = null;
        UpdatedAt = now;
        AddHistory(new SeedStatusEntry(now, Version, SeedState.Requested, null, null, null));
    }

    private void AddHistory(SeedStatusEntry entry)
    {
        _history.Add(entry);
        if (_history.Count > HistoryLimit) _history.RemoveRange(0, _history.Count - HistoryLimit);
    }

    private static string ValidTenantId(string? tenantId)
    {
        var value = tenantId?.Trim() ?? "";
        if (!TenantIdText().IsMatch(value))
            throw new DomainException("A tenant id is lowercase letters, digits and '-', at most 40 characters.");
        return value;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex TenantIdText();
}

/// <summary>One entry of a seed's status history.</summary>
public sealed class SeedStatusEntry
{
    private SeedStatusEntry() { } // EF Core

    public SeedStatusEntry(DateTimeOffset at, long version, SeedState status, string? reason, string? actualDatabaseServer, string? actualDatabaseName)
    {
        At = at;
        Version = version;
        Status = status;
        Reason = reason;
        ActualDatabaseServer = actualDatabaseServer;
        ActualDatabaseName = actualDatabaseName;
    }

    public DateTimeOffset At { get; private set; }
    public long Version { get; private set; }
    public SeedState Status { get; private set; }
    public string? Reason { get; private set; }
    public string? ActualDatabaseServer { get; private set; }
    public string? ActualDatabaseName { get; private set; }
}
