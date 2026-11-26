using Microsoft.EntityFrameworkCore;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Tenancy;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Config;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;
using CentralSeed = soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate.TenantSeed;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Connector;

/// <summary>
/// The connector's replica in each tenant's database (schema <c>organization</c>), written only from the central feed:
/// <list type="bullet">
/// <item><see cref="Units"/>: every unit of the tenant's org, so moves and ancestors need no call to the central
/// service. The tenant's own units are its root unit and the units below it; the root's ancestors are read-only context.
/// Units outside both are kept for moves, and never served.</item>
/// <item><see cref="Persons"/> and <see cref="Positions"/> of the org.</item>
/// <item><see cref="Memberships"/>: those of the tenant's own units only.</item>
/// <item><see cref="Links"/>: the tenant links that name this tenant's seed.</item>
/// <item><see cref="Sync"/>: one row, the tenant's place in its org's feed.</item>
/// </list>
/// It maps the integration outbox and inbox: the applied events are republished locally from it, in the same save.
/// </summary>
public sealed class ReplicaDbContext(DbContextOptions<ReplicaDbContext> options, ITenantContext tenant) : TenantDbContext(options, tenant)
{
    public const string SchemaName = "organization";

    protected override string Schema => SchemaName;

    protected override bool UsesIntegrationEvents => true;

    public DbSet<UnitReplica> Units => Set<UnitReplica>();
    public DbSet<PersonReplica> Persons => Set<PersonReplica>();
    public DbSet<PositionReplica> Positions => Set<PositionReplica>();
    public DbSet<MembershipReplica> Memberships => Set<MembershipReplica>();
    public DbSet<LinkReplica> Links => Set<LinkReplica>();
    public DbSet<SyncState> Sync => Set<SyncState>();

    /// <summary>Every time is stored as UTC (<see cref="UtcTimeConventions"/>).</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.StoreDateTimeOffsetsAsUtc();
    }

    // Configured here, not with IEntityTypeConfiguration classes: OrganizationDbContext applies every configuration
    // class of this assembly to the central model.
    protected override void ConfigureModel(ModelBuilder model)
    {
        model.Entity<UnitReplica>(unit =>
        {
            unit.ToTable("replica_units");
            unit.Property(u => u.Id).ValueGeneratedNever();
            unit.Property(u => u.TypeKey).HasMaxLength(Rules.KeyMaxLength);
            unit.Property(u => u.Path).HasMaxLength(OrgUnit.PathMaxLength);
            unit.HasIndex(u => u.Path);
            unit.HasIndex(u => u.ParentId);
            unit.Property(u => u.NameEn).HasMaxLength(Rules.NameMaxLength);
            unit.Property(u => u.NameAr).HasMaxLength(Rules.NameMaxLength);
            unit.Property(u => u.TimeZone).HasMaxLength(Rules.ItemMaxLength);
            unit.Property(u => u.Binding).HasConversion<string>().HasMaxLength(20);
            unit.Property(u => u.SeedId).HasMaxLength(CentralSeed.IdMaxLength);
            unit.Property(u => u.Tags).HasConversion(JsonLists.Converter<string>(), JsonLists.Comparer<string>());
            unit.Property(u => u.Categories).HasConversion(JsonLists.Converter<string>(), JsonLists.Comparer<string>());
        });
        model.Entity<PersonReplica>(person =>
        {
            person.ToTable("replica_persons");
            person.Property(p => p.Id).ValueGeneratedNever();
            person.Property(p => p.Issuer).HasMaxLength(Rules.IdentityMaxLength);
            person.Property(p => p.Subject).HasMaxLength(Rules.SubjectMaxLength);
            person.HasIndex(p => p.Subject);
            person.Property(p => p.DisplayName).HasMaxLength(Rules.NameMaxLength);
            person.Property(p => p.Email).HasMaxLength(Rules.IdentityMaxLength);
        });
        model.Entity<PositionReplica>(position =>
        {
            position.ToTable("replica_positions");
            position.Property(p => p.Id).ValueGeneratedNever();
            position.Property(p => p.Key).HasMaxLength(Rules.KeyMaxLength);
            position.Property(p => p.NameEn).HasMaxLength(Rules.NameMaxLength);
            position.Property(p => p.NameAr).HasMaxLength(Rules.NameMaxLength);
            position.Property(p => p.Permissions).HasConversion(JsonLists.Converter<string>(), JsonLists.Comparer<string>());
        });
        model.Entity<MembershipReplica>(membership =>
        {
            membership.ToTable("replica_memberships");
            membership.Property(m => m.Id).ValueGeneratedNever();
            membership.HasIndex(m => m.UnitId);
            membership.HasIndex(m => m.PersonId);
            membership.HasIndex(m => m.PositionId);
            membership.Property(m => m.Issuer).HasMaxLength(Rules.IdentityMaxLength);
            membership.Property(m => m.Subject).HasMaxLength(Rules.SubjectMaxLength);
            membership.Property(m => m.PositionKey).HasMaxLength(Rules.KeyMaxLength);
            membership.Property(m => m.Permissions).HasConversion(JsonLists.Converter<string>(), JsonLists.Comparer<string>());
        });
        model.Entity<LinkReplica>(link =>
        {
            link.ToTable("replica_links");
            link.Property(l => l.Id).ValueGeneratedNever();
            link.Property(l => l.FromSeedId).HasMaxLength(CentralSeed.IdMaxLength);
            link.Property(l => l.ToSeedId).HasMaxLength(CentralSeed.IdMaxLength);
            link.Property(l => l.State).HasConversion<string>().HasMaxLength(20);
        });
        model.Entity<SyncState>(sync =>
        {
            sync.ToTable("replica_sync");
            sync.Property(s => s.Id).ValueGeneratedNever();
            sync.Property(s => s.SeedId).HasMaxLength(CentralSeed.IdMaxLength);
            // Unbounded: the entries applied above the cursor (see FeedWindow).
            sync.Property(s => s.Applied);
            sync.Property(s => s.PendingGrantUnits).HasConversion(JsonLists.Converter<Guid>(), JsonLists.Comparer<Guid>());
            sync.Property(s => s.LastError).HasMaxLength(Rules.TextMaxLength * 2);
        });
    }
}

/// <summary>A unit of the tenant's org, as the last applied unit event left it.</summary>
public sealed class UnitReplica
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid? ParentId { get; set; }
    public string TypeKey { get; set; } = "";
    public string Path { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public string? TimeZone { get; set; }
    public List<string> Categories { get; set; } = [];
    public TenantBindingKind Binding { get; set; }
    public string? SeedId { get; set; }
    public int Version { get; set; }

    public void Set(OrgUnitSnapshot s)
    {
        Id = s.UnitId;
        OrgId = s.OrgId;
        ParentId = s.ParentId;
        TypeKey = s.TypeKey;
        Path = s.Path;
        NameEn = s.NameEn;
        NameAr = s.NameAr;
        Tags = [.. s.Tags];
        TimeZone = s.TimeZone;
        Categories = [.. s.Categories];
        Binding = s.Binding;
        SeedId = s.SeedId;
        Version = s.Version;
    }

    public OrgUnitSnapshot ToSnapshot() =>
        new(Id, OrgId, ParentId, TypeKey, Path, NameEn, NameAr, [.. Tags], TimeZone, [.. Categories], Binding, SeedId, Version);
}

public sealed class PersonReplica
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public bool IsEmployee { get; set; }
}

public sealed class PositionReplica
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Key { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
}

/// <summary>A membership of one of the tenant's units (the position's permissions as the event carried them).</summary>
public sealed class MembershipReplica
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public Guid PersonId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UnitId { get; set; }
    public Guid? PositionId { get; set; }
    public string? PositionKey { get; set; }
    public List<string> Permissions { get; set; } = [];
    public DateTimeOffset From { get; set; }
    public DateTimeOffset? To { get; set; }
    public bool Primary { get; set; }
}

public sealed class LinkReplica
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string FromSeedId { get; set; } = "";
    public Guid FromUnitId { get; set; }
    public string ToSeedId { get; set; } = "";
    public Guid ToUnitId { get; set; }
    public TenantLinkState State { get; set; }
}

/// <summary>The tenant's place in its org's feed (one row, <see cref="SingleId"/>).</summary>
public sealed class SyncState
{
    public const int SingleId = 1;

    public int Id { get; set; } = SingleId;

    /// <summary>The seed the tenant was created from, its org and its root unit (from the deployment's seeds).</summary>
    public string SeedId { get; set; } = "";
    public Guid OrgId { get; set; }
    public Guid RootUnitId { get; set; }

    /// <summary>Every feed entry at or below it is settled (<c>FeedWindow.Cursor</c>).</summary>
    public long Cursor { get; set; }

    /// <summary>The entries applied above the cursor (<c>FeedWindow.Serialize</c>).</summary>
    public string Applied { get; set; } = "";

    /// <summary>
    /// Units whose grants must be written again (set in the save that changed them, cleared once the grants are
    /// written), so a failure between the two is repaired on the next sync. <see cref="Guid.Empty"/> means every unit.
    /// </summary>
    public List<Guid> PendingGrantUnits { get; set; } = [];

    /// <summary>A unit moved into the tenant: its memberships, skipped before, are read again from the start of the feed.</summary>
    public bool RebuildMemberships { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }
    public string? LastError { get; set; }
}
