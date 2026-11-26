using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Contracts.Security;
using soft.square.net.WebApi.Modules.Organization.UseCases;
using soft.square.net.WebApi.Modules.Organization.UseCases.Connector;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;

/// <summary>The tenant a replica is for: its seed, and the org and root unit the seed names.</summary>
public sealed record ReplicaBinding(string SeedId, Guid OrgId, Guid RootUnitId)
{
    public static ReplicaBinding Of(DeploymentSeedDto seed) => new(seed.Id, seed.OrgId, seed.RootUnitId);
}

/// <summary>What one sync did.</summary>
/// <param name="Applied">Feed entries handled (applied or not for this tenant).</param>
/// <param name="Published">org.* events republished locally.</param>
/// <param name="SeedsChanged">The feed said a seed of the org changed: pull the seeds again.</param>
public sealed record SyncOutcome(int Applied, int Published, bool SeedsChanged);

/// <summary>
/// Brings one tenant's replica up to date with its org's central feed (scoped: a tenant scope of the module's container,
/// under the caller's per-tenant lock). Each page is applied in one save, with the cursor and the outbox rows of the
/// events it republishes, so a crash repeats a page and never half of one. After the save it tells the browsers
/// (unit live topics) and writes the standing grants of the units whose memberships or positions changed.
/// <para>Applying is idempotent: units carry a version, memberships, persons, positions and links their whole state,
/// and the <see cref="FeedWindow"/> applies each entry once even when a read overlaps the previous one.</para>
/// </summary>
public sealed class ReplicaSync(
    ReplicaDbContext db,
    IOrganizationClient central,
    IIntegrationEvents events,
    IGrantStore grants,
    IServiceProvider services,
    IOptions<OrganizationOptions> options,
    TimeProvider clock,
    ILogger<ReplicaSync> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The live message type for changes of a unit's members (the unit events keep their own type).</summary>
    public const string MembersChangedMessage = "org.members-changed";

    public async Task<SyncOutcome> SyncAsync(ReplicaBinding binding, CancellationToken ct)
    {
        var state = await PrepareAsync(binding, ct);
        var window = FeedWindow.Deserialize(state.Cursor, state.Applied);
        var take = Math.Clamp(options.Value.FeedPageSize, 1, 1000);
        var after = window.Cursor;
        int applied = 0, published = 0;
        var seedsChanged = false;

        while (true)
        {
            var page = await central.ReadFeedAsync(binding.OrgId, after, take, ct);
            var set = await ReplicaSet.LoadAsync(db, state, ct);
            var changes = new Changes();
            foreach (var entry in page.Entries.OrderBy(e => e.Sequence))
            {
                if (!window.IsNew(entry.Sequence)) continue;
                Apply(set, entry, changes, publish: true);
                window.MarkApplied(entry.Sequence, entry.OccurredAt);
                applied++;
            }
            window.Advance(clock.GetUtcNow(), options.Value.FeedOverlap);
            state.Cursor = window.Cursor;
            state.Applied = window.Serialize();
            state.PendingGrantUnits = [.. state.PendingGrantUnits.Union(changes.GrantUnits)];
            state.RebuildMemberships |= changes.UnitsEntered;
            state.LastSyncedAt = clock.GetUtcNow();
            state.LastError = null;
            await db.SaveChangesAsync(ct);

            published += changes.Published;
            seedsChanged |= changes.SeedsChanged;
            await NotifyAsync(changes, ct);

            if (!page.More || page.Entries.Count == 0 || page.Next <= after) break;
            after = page.Next;
        }

        if (state.RebuildMemberships) await RebuildMembershipsAsync(state, window, ct);
        await WriteGrantsAsync(state, ct);
        return new SyncOutcome(applied, published, seedsChanged);
    }

    /// <summary>Records a failed sync on the replica, for the admin pages (best effort).</summary>
    public async Task RecordErrorAsync(string error, CancellationToken ct)
    {
        try
        {
            db.ChangeTracker.Clear();
            if (await db.Sync.FirstOrDefaultAsync(ct) is not { } state) return;
            state.LastError = error.Length > 400 ? error[..400] : error;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogDebug(e, "Could not record the sync error.");
        }
    }

    private async Task<SyncState> PrepareAsync(ReplicaBinding binding, CancellationToken ct)
    {
        var state = await db.Sync.FirstOrDefaultAsync(ct);
        if (state is null)
        {
            state = new SyncState { OrgId = binding.OrgId, RootUnitId = binding.RootUnitId, PendingGrantUnits = [Guid.Empty] };
            db.Sync.Add(state);
        }
        else if (state.OrgId != binding.OrgId || state.RootUnitId != binding.RootUnitId)
        {
            // The seed now names another org or root unit: start over from the beginning of the new org's feed.
            log.LogWarning("The replica of seed {Seed} moves from unit {Old} to unit {New}: rebuilding it.", binding.SeedId, state.RootUnitId, binding.RootUnitId);
            db.Units.RemoveRange(await db.Units.ToListAsync(ct));
            db.Persons.RemoveRange(await db.Persons.ToListAsync(ct));
            db.Positions.RemoveRange(await db.Positions.ToListAsync(ct));
            db.Memberships.RemoveRange(await db.Memberships.ToListAsync(ct));
            db.Links.RemoveRange(await db.Links.ToListAsync(ct));
            state.OrgId = binding.OrgId;
            state.RootUnitId = binding.RootUnitId;
            state.Cursor = 0;
            state.Applied = "";
            state.RebuildMemberships = false;
            state.PendingGrantUnits = [Guid.Empty];
        }
        state.SeedId = binding.SeedId;
        return state;
    }

    // ---- Applying one entry ------------------------------------------------------------------------------------

    private void Apply(ReplicaSet set, FeedEntryDto entry, Changes changes, bool publish)
    {
        switch (entry.Type)
        {
            case OrgEvents.UnitCreated:
                ApplyUnit(set, entry, Data<UnitCreated>(entry).Unit, changes, publish);
                break;
            case OrgEvents.UnitUpdated:
                ApplyUnit(set, entry, Data<UnitUpdated>(entry).Unit, changes, publish);
                break;
            case OrgEvents.UnitMoved:
                ApplyMove(set, entry, Data<UnitMoved>(entry), changes, publish);
                break;
            case OrgEvents.UnitRemoved:
                ApplyRemove(set, entry, Data<UnitRemoved>(entry), changes, publish);
                break;
            case OrgEvents.MembershipChanged:
                ApplyMembership(set, entry, Data<MembershipChanged>(entry), changes, publish);
                break;
            case OrgEvents.PersonChanged:
                ApplyPerson(set, entry, Data<PersonChanged>(entry), changes, publish);
                break;
            case OrgEvents.PositionChanged:
                ApplyPosition(set, entry, Data<PositionChanged>(entry), changes, publish);
                break;
            case OrgEvents.TenantLinkChanged:
                ApplyLink(set, entry, Data<TenantLinkChanged>(entry), changes, publish);
                break;
            case OrgEvents.TenantSeedChanged:
                changes.SeedsChanged = true;
                break;
            // Event types this connector does not know yet (a newer central service): skipped, never fatal.
        }
    }

    private void ApplyUnit(ReplicaSet set, FeedEntryDto entry, OrgUnitSnapshot s, Changes changes, bool publish)
    {
        var unit = set.Units.GetValueOrDefault(s.UnitId);
        var wasVisible = unit is not null && set.IsVisible(unit);
        if (unit is null)
        {
            unit = new UnitReplica();
            unit.Set(s);
            set.Add(unit);
        }
        else if (s.Version > unit.Version)
        {
            var before = set.InTenantIds();
            unit.Set(s);
            set.RootPathChanged();
            if (!before.SetEquals(set.InTenantIds())) changes.UnitsEntered = true; // a path the replica had not seen moved yet
        }
        else return; // older than what the replica has

        if (!publish) return;
        if (wasVisible || set.IsVisible(unit))
        {
            if (unit.Id == set.State.RootUnitId && !wasVisible) PublishAncestors(set, unit, entry, changes);
            Publish(entry, changes, unit.Path);
            changes.Live(unit.Path, entry);
        }
    }

    /// <summary>
    /// The tenant's root unit just arrived: its ancestors came earlier in the feed, when they were not yet the
    /// tenant's. Subscribers learn them now, as <c>org.unit-created.v1</c> with their current state.
    /// </summary>
    private void PublishAncestors(ReplicaSet set, UnitReplica root, FeedEntryDto entry, Changes changes)
    {
        foreach (var id in UnitPaths.Ids(root.Path).SkipLast(1))
        {
            if (!Guid.TryParseExact(id, "N", out var ancestorId) || set.Units.GetValueOrDefault(ancestorId) is not { } ancestor) continue;
            var created = entry with
            {
                Type = OrgEvents.UnitCreated,
                Subject = OrgEvents.UnitSubject(ancestor.Id),
                UnitPath = ancestor.Path,
                Data = JsonSerializer.SerializeToElement(new UnitCreated(ancestor.ToSnapshot()), Json)
            };
            Publish(created, changes, ancestor.Path);
        }
    }

    private void ApplyMove(ReplicaSet set, FeedEntryDto entry, UnitMoved m, Changes changes, bool publish)
    {
        var before = set.InTenantIds();
        var unit = set.Units.GetValueOrDefault(m.UnitId);
        var wasVisible = unit is not null && set.IsVisible(unit);
        foreach (var u in set.Units.Values.Where(u => UnitPaths.IsSelfOrBelow(u.Path, m.OldPath)))
            u.Path = UnitPaths.Rebase(u.Path, m.OldPath, m.NewPath);
        if (unit is not null && m.Version > unit.Version)
        {
            unit.ParentId = m.NewParentId;
            unit.Version = m.Version;
        }
        set.RootPathChanged();
        var after = set.InTenantIds();

        // Units that left the tenant lose their memberships (and grants); units that came in get theirs from the feed.
        foreach (var left in before.Except(after))
        {
            foreach (var membership in set.MembershipsOf(left).ToList()) set.Remove(membership);
            changes.GrantUnits.Add(left);
        }
        if (after.Except(before).Any()) changes.UnitsEntered = true;

        if (!publish) return;
        if (wasVisible || (unit is not null && set.IsVisible(unit)))
        {
            Publish(entry, changes, m.NewPath);
            changes.Live(m.OldPath, entry);
            changes.Live(m.NewPath, entry);
        }
    }

    private void ApplyRemove(ReplicaSet set, FeedEntryDto entry, UnitRemoved r, Changes changes, bool publish)
    {
        if (set.Units.GetValueOrDefault(r.UnitId) is not { } unit) return;
        var visible = set.IsVisible(unit);
        if (set.InTenant(unit)) changes.GrantUnits.Add(unit.Id);
        foreach (var membership in set.MembershipsOf(unit.Id).ToList()) set.Remove(membership);
        set.Remove(unit);
        if (publish && visible)
        {
            Publish(entry, changes, r.Path);
            changes.Live(r.Path, entry);
        }
    }

    private void ApplyMembership(ReplicaSet set, FeedEntryDto entry, MembershipChanged m, Changes changes, bool publish)
    {
        var unit = set.Units.GetValueOrDefault(m.UnitId);
        var inTenant = unit is not null ? set.InTenant(unit) : set.RootPath is { } root && UnitPaths.IsSelfOrBelow(m.UnitPath, root);
        var existing = set.Memberships.GetValueOrDefault(m.MembershipId);
        if (!inTenant && existing is null) return; // another tenant's member

        if (m.Removed || !inTenant)
        {
            if (existing is not null) set.Remove(existing);
        }
        else
        {
            if (existing is null)
            {
                existing = new MembershipReplica { Id = m.MembershipId };
                set.Add(existing);
            }
            existing.OrgId = m.OrgId;
            existing.PersonId = m.PersonId;
            existing.Issuer = m.Issuer;
            existing.Subject = m.Subject;
            existing.UnitId = m.UnitId;
            existing.PositionId = m.PositionId;
            existing.PositionKey = m.PositionKey;
            existing.Permissions = m.PositionId is { } p && set.Positions.GetValueOrDefault(p) is { } position ? [.. position.Permissions] : [.. m.Permissions];
            existing.From = m.From;
            existing.To = m.To;
            existing.Primary = m.Primary;
        }
        changes.GrantUnits.Add(m.UnitId);

        if (!publish) return;
        var path = unit?.Path ?? m.UnitPath;
        Publish(entry, changes, path);
        changes.Live(path, entry, MembersChangedMessage);
    }

    private void ApplyPerson(ReplicaSet set, FeedEntryDto entry, PersonChanged p, Changes changes, bool publish)
    {
        var person = set.Persons.GetValueOrDefault(p.PersonId);
        if (person is null)
        {
            person = new PersonReplica { Id = p.PersonId };
            set.Add(person);
        }
        person.OrgId = p.OrgId;
        person.Issuer = p.Issuer;
        person.Subject = p.Subject;
        person.DisplayName = p.DisplayName;
        person.Email = p.Email;
        person.IsEmployee = p.IsEmployee;

        if (publish && set.Memberships.Values.Any(m => m.PersonId == p.PersonId)) Publish(entry, changes, null);
    }

    private void ApplyPosition(ReplicaSet set, FeedEntryDto entry, PositionChanged p, Changes changes, bool publish)
    {
        var position = set.Positions.GetValueOrDefault(p.PositionId);
        if (position is null)
        {
            position = new PositionReplica { Id = p.PositionId };
            set.Add(position);
        }
        position.OrgId = p.OrgId;
        position.Key = p.Key;
        position.NameEn = p.NameEn;
        position.NameAr = p.NameAr;
        position.Permissions = [.. p.Permissions];

        // The grants of every membership with this position follow its permissions.
        foreach (var membership in set.Memberships.Values.Where(m => m.PositionId == p.PositionId))
        {
            membership.Permissions = [.. p.Permissions];
            membership.PositionKey = p.Key;
            changes.GrantUnits.Add(membership.UnitId);
        }
        if (publish) Publish(entry, changes, null);
    }

    private void ApplyLink(ReplicaSet set, FeedEntryDto entry, TenantLinkChanged l, Changes changes, bool publish)
    {
        if (l.FromSeedId != set.State.SeedId && l.ToSeedId != set.State.SeedId) return;
        var link = set.Links.GetValueOrDefault(l.LinkId);
        if (link is null)
        {
            link = new LinkReplica { Id = l.LinkId };
            set.Add(link);
        }
        link.OrgId = l.OrgId;
        link.FromSeedId = l.FromSeedId;
        link.FromUnitId = l.FromUnitId;
        link.ToSeedId = l.ToSeedId;
        link.ToUnitId = l.ToUnitId;
        link.State = l.State;
        if (publish) Publish(entry, changes, entry.UnitPath);
    }

    /// <summary>Republishes the central event locally, as it is (the same type and data), through the replica's outbox.</summary>
    private void Publish(FeedEntryDto entry, Changes changes, string? unitPath)
    {
        events.Publish(entry.Type, entry.Data, entry.Subject, unitPath, entry.OrgId.ToString());
        changes.Published++;
    }

    private static T Data<T>(FeedEntryDto entry) =>
        entry.Data.Deserialize<T>(Json) ?? throw new JsonException($"Feed entry {entry.Sequence} ({entry.Type}) has no data.");

    // ---- After the save ----------------------------------------------------------------------------------------

    /// <summary>
    /// A unit moved into the tenant: the memberships of its subtree were skipped while it was outside. Reads the
    /// feed again from the start up to what was applied, applying only membership changes (not republished).
    /// </summary>
    private async Task RebuildMembershipsAsync(SyncState state, FeedWindow window, CancellationToken ct)
    {
        var until = window.Applied.Count > 0 ? window.Applied[^1].Sequence : window.Cursor;
        var set = await ReplicaSet.LoadAsync(db, state, ct);
        var changes = new Changes();
        long after = 0;
        while (after < until)
        {
            var page = await central.ReadFeedAsync(state.OrgId, after, Math.Clamp(options.Value.FeedPageSize, 1, 1000), ct);
            foreach (var entry in page.Entries.Where(e => e.Sequence <= until && e.Type == OrgEvents.MembershipChanged).OrderBy(e => e.Sequence))
                Apply(set, entry, changes, publish: false);
            if (!page.More || page.Entries.Count == 0 || page.Next <= after) break;
            after = page.Next;
        }
        state.RebuildMemberships = false;
        state.PendingGrantUnits = [Guid.Empty];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Replaces the grant source <c>org:{unit}</c> of each unit whose memberships or positions changed.</summary>
    private async Task WriteGrantsAsync(SyncState state, CancellationToken ct)
    {
        if (state.PendingGrantUnits.Count == 0) return;
        var memberships = await db.Memberships.AsNoTracking().ToListAsync(ct);
        var units = state.PendingGrantUnits.Where(id => id != Guid.Empty).ToHashSet();
        if (state.PendingGrantUnits.Contains(Guid.Empty))
        {
            units.UnionWith(memberships.Select(m => m.UnitId));
            units.UnionWith((await grants.ListAsync(ct: ct)).Select(g => OrgGrants.UnitOf(g.Source)).OfType<Guid>());
        }

        var now = clock.GetUtcNow();
        foreach (var unitId in units)
        {
            var rows = OrgGrants.For(unitId, memberships.Where(m => m.UnitId == unitId)
                .Select(m => new GrantingMembership(m.Subject, m.Permissions, m.From, m.To)), now);
            await grants.ReplaceSourceAsync(OrgGrants.Source(unitId), rows, ct);
        }
        state.PendingGrantUnits = [];
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Tells the browsers watching the changed units (and their ancestors); best effort, after the commit.</summary>
    private async Task NotifyAsync(Changes changes, CancellationToken ct)
    {
        if (changes.LiveMessages.Count == 0 || services.GetService<ILiveNotifier>() is not { } live) return;
        foreach (var (path, type, entry) in changes.LiveMessages)
        {
            try
            {
                var payload = JsonSerializer.SerializeToElement(new { entry.Type, entry.Subject, entry.Sequence }, Json);
                await live.PublishToUnitAsync(path, type, payload, entry.Sequence, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogDebug(e, "Live message {Type} for {Path} was not sent.", type, path);
            }
        }
    }

    private sealed class Changes
    {
        public HashSet<Guid> GrantUnits { get; } = [];
        public List<(string Path, string Type, FeedEntryDto Entry)> LiveMessages { get; } = [];
        public int Published { get; set; }
        public bool SeedsChanged { get; set; }
        public bool UnitsEntered { get; set; }

        public void Live(string path, FeedEntryDto entry, string? type = null) => LiveMessages.Add((path, type ?? entry.Type, entry));
    }
}

/// <summary>The replica's rows of one sync, tracked and indexed in memory (an org's tree is small).</summary>
internal sealed class ReplicaSet
{
    private readonly ReplicaDbContext _db;
    private string? _rootPath;
    private bool _rootKnown;

    private ReplicaSet(ReplicaDbContext db, SyncState state)
    {
        _db = db;
        State = state;
    }

    public SyncState State { get; }
    public Dictionary<Guid, UnitReplica> Units { get; private set; } = [];
    public Dictionary<Guid, PersonReplica> Persons { get; private set; } = [];
    public Dictionary<Guid, PositionReplica> Positions { get; private set; } = [];
    public Dictionary<Guid, MembershipReplica> Memberships { get; private set; } = [];
    public Dictionary<Guid, LinkReplica> Links { get; private set; } = [];

    public static async Task<ReplicaSet> LoadAsync(ReplicaDbContext db, SyncState state, CancellationToken ct) => new(db, state)
    {
        Units = await db.Units.ToDictionaryAsync(u => u.Id, ct),
        Persons = await db.Persons.ToDictionaryAsync(p => p.Id, ct),
        Positions = await db.Positions.ToDictionaryAsync(p => p.Id, ct),
        Memberships = await db.Memberships.ToDictionaryAsync(m => m.Id, ct),
        Links = await db.Links.ToDictionaryAsync(l => l.Id, ct)
    };

    /// <summary>The path of the tenant's root unit, or null while the replica does not have it.</summary>
    public string? RootPath
    {
        get
        {
            if (!_rootKnown)
            {
                _rootPath = Units.GetValueOrDefault(State.RootUnitId)?.Path;
                _rootKnown = _rootPath is not null;
            }
            return _rootPath;
        }
    }

    public void RootPathChanged() => _rootKnown = false;

    /// <summary>The unit is the tenant's root unit or below it.</summary>
    public bool InTenant(UnitReplica unit) => RootPath is { } root && UnitPaths.IsSelfOrBelow(unit.Path, root);

    /// <summary>The unit is the tenant's, or one of the root's ancestors.</summary>
    public bool IsVisible(UnitReplica unit) => RootPath is { } root && (UnitPaths.IsSelfOrBelow(unit.Path, root) || UnitPaths.IsSelfOrBelow(root, unit.Path));

    public HashSet<Guid> InTenantIds() => [.. Units.Values.Where(InTenant).Select(u => u.Id)];

    public IEnumerable<MembershipReplica> MembershipsOf(Guid unitId) => Memberships.Values.Where(m => m.UnitId == unitId);

    public void Add(UnitReplica unit)
    {
        Units[unit.Id] = unit;
        _db.Units.Add(unit);
        if (unit.Id == State.RootUnitId) RootPathChanged();
    }

    public void Add(PersonReplica person) { Persons[person.Id] = person; _db.Persons.Add(person); }
    public void Add(PositionReplica position) { Positions[position.Id] = position; _db.Positions.Add(position); }
    public void Add(MembershipReplica membership) { Memberships[membership.Id] = membership; _db.Memberships.Add(membership); }
    public void Add(LinkReplica link) { Links[link.Id] = link; _db.Links.Add(link); }

    public void Remove(UnitReplica unit)
    {
        Units.Remove(unit.Id);
        _db.Units.Remove(unit);
        if (unit.Id == State.RootUnitId) RootPathChanged();
    }

    public void Remove(MembershipReplica membership)
    {
        Memberships.Remove(membership.Id);
        _db.Memberships.Remove(membership);
    }
}
