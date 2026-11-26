using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using OrgEntity = soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate.Org;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;
using TenantSeed = soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate.TenantSeed;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data.Config;

// The central model. Short lists (tags, permissions, hosts, ...) are one JSON text column each; child rows that are
// queried or grow (org admins, unit categories, seed history) are tables of their own, not owned types: the platform
// prefixes the tables of every non-owned entity on MySQL and Oracle. Enums are stored as text. Strings are bounded,
// so every engine stores them as ordinary string columns, except the JSON lists and the feed data.

public sealed class OrgConfiguration : IEntityTypeConfiguration<OrgEntity>
{
    public void Configure(EntityTypeBuilder<OrgEntity> org)
    {
        org.ToTable("orgs");
        org.Property(o => o.Id).ValueGeneratedNever();
        org.Property(o => o.Key).HasMaxLength(Rules.KeyMaxLength);
        org.HasIndex(o => o.Key).IsUnique();
        org.Names();
        org.Property(o => o.Version).IsConcurrencyToken();
        org.HasMany(o => o.Admins).WithOne().HasForeignKey("OrgId").OnDelete(DeleteBehavior.Cascade);
        org.Navigation(o => o.Admins).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class OrgAdminConfiguration : IEntityTypeConfiguration<OrgAdmin>
{
    public void Configure(EntityTypeBuilder<OrgAdmin> admin)
    {
        admin.ToTable("org_admins");
        admin.Property<long>("Id");
        admin.HasKey("Id");
        admin.Property(a => a.Issuer).HasMaxLength(Rules.IdentityMaxLength);
        admin.Property(a => a.Subject).HasMaxLength(Rules.SubjectMaxLength);
        admin.HasIndex("OrgId");
        admin.HasIndex(a => a.Subject);
    }
}

public sealed class UnitTypeConfiguration : IEntityTypeConfiguration<UnitType>
{
    public void Configure(EntityTypeBuilder<UnitType> type)
    {
        type.ToTable("unit_types");
        type.Property(t => t.Id).ValueGeneratedNever();
        type.Property(t => t.Key).HasMaxLength(Rules.KeyMaxLength);
        type.HasIndex(t => new { t.OrgId, t.Key }).IsUnique();
        type.Names();
        type.Ignore(t => t.AllowedParents);
        type.Ignore(t => t.IsRoot);
        type.StringList("_allowedParents", "AllowedParents");
    }
}

public sealed class OrgUnitConfiguration : IEntityTypeConfiguration<OrgUnit>
{
    public void Configure(EntityTypeBuilder<OrgUnit> unit)
    {
        unit.ToTable("units");
        unit.Property(u => u.Id).ValueGeneratedNever();
        unit.Property(u => u.TypeKey).HasMaxLength(Rules.KeyMaxLength);
        unit.Property(u => u.Path).HasMaxLength(OrgUnit.PathMaxLength);
        unit.HasIndex(u => new { u.OrgId, u.Path });
        unit.HasIndex(u => new { u.OrgId, u.ParentId });
        unit.Names();
        unit.Property(u => u.TimeZone).HasMaxLength(Rules.ItemMaxLength);
        unit.Property(u => u.Binding).HasConversion<string>().HasMaxLength(20);
        unit.Property(u => u.SeedId).HasMaxLength(TenantSeed.IdMaxLength);
        unit.Property(u => u.Version).IsConcurrencyToken();
        unit.Ignore(u => u.Tags);
        unit.Ignore(u => u.CategoryIds);
        unit.StringList("_tags", "Tags");
        unit.HasMany(u => u.Categories).WithOne().HasForeignKey("UnitId").OnDelete(DeleteBehavior.Cascade);
        unit.Navigation(u => u.Categories).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class UnitCategoryConfiguration : IEntityTypeConfiguration<UnitCategory>
{
    public void Configure(EntityTypeBuilder<UnitCategory> category)
    {
        category.ToTable("unit_categories");
        category.Property(c => c.CategoryId).HasMaxLength(Rules.ItemMaxLength);
        category.HasKey("UnitId", nameof(UnitCategory.CategoryId));
        category.HasIndex(c => c.CategoryId);
    }
}

public sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> person)
    {
        person.ToTable("persons");
        person.Property(p => p.Id).ValueGeneratedNever();
        person.Property(p => p.Issuer).HasMaxLength(Rules.IdentityMaxLength);
        person.Property(p => p.Subject).HasMaxLength(Rules.SubjectMaxLength);
        person.HasIndex(p => new { p.OrgId, p.Subject, p.Issuer }).IsUnique();
        person.HasIndex(p => p.Subject);
        person.Property(p => p.DisplayName).HasMaxLength(Rules.NameMaxLength);
        person.Property(p => p.Email).HasMaxLength(Rules.IdentityMaxLength);
        person.Property(p => p.EmployeeNumber).HasMaxLength(Rules.ItemMaxLength);
        person.Property(p => p.JobTitle).HasMaxLength(Rules.TextMaxLength);
        person.Property(p => p.Version).IsConcurrencyToken();
        person.Ignore(p => p.Employee);
    }
}

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> membership)
    {
        membership.ToTable("memberships");
        membership.Property(m => m.Id).ValueGeneratedNever();
        membership.HasIndex(m => new { m.OrgId, m.UnitId });
        membership.HasIndex(m => m.PersonId);
        membership.HasIndex(m => m.PositionId);
        membership.Property(m => m.SeedId).HasMaxLength(TenantSeed.IdMaxLength);
        membership.HasIndex(m => m.SeedId);
    }
}

public sealed class PositionConfiguration : IEntityTypeConfiguration<PositionDefinition>
{
    public void Configure(EntityTypeBuilder<PositionDefinition> position)
    {
        position.ToTable("positions");
        position.Property(p => p.Id).ValueGeneratedNever();
        position.Property(p => p.Key).HasMaxLength(Rules.KeyMaxLength);
        position.HasIndex(p => new { p.OrgId, p.Key }).IsUnique();
        position.Names();
        position.Property(p => p.Version).IsConcurrencyToken();
        position.Ignore(p => p.Permissions);
        position.StringList("_permissions", "Permissions");
    }
}

public sealed class TenantSeedConfiguration : IEntityTypeConfiguration<TenantSeed>
{
    public void Configure(EntityTypeBuilder<TenantSeed> seed)
    {
        seed.ToTable("tenant_seeds");
        seed.Property(s => s.Id).HasMaxLength(TenantSeed.IdMaxLength).ValueGeneratedNever();
        seed.HasIndex(s => s.OrgId);
        seed.HasIndex(s => s.RootUnitId).IsUnique();
        seed.Property(s => s.DeploymentId).HasMaxLength(Rules.ItemMaxLength);
        seed.Property(s => s.TenantId).HasMaxLength(Rules.KeyMaxLength);
        seed.HasIndex(s => new { s.DeploymentId, s.TenantId }).IsUnique();
        seed.Property(s => s.Name).HasMaxLength(Rules.NameMaxLength);
        seed.Property(s => s.DatabaseServer).HasMaxLength(Rules.ItemMaxLength);
        seed.Property(s => s.DatabaseName).HasMaxLength(Rules.ItemMaxLength);
        seed.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        seed.Property(s => s.Reason).HasMaxLength(1000);
        seed.Property(s => s.ActualDatabaseServer).HasMaxLength(Rules.ItemMaxLength);
        seed.Property(s => s.ActualDatabaseName).HasMaxLength(Rules.ItemMaxLength);
        seed.Property(s => s.Revision).IsConcurrencyToken();
        seed.Ignore(s => s.Hosts);
        seed.Ignore(s => s.Modules);
        seed.Ignore(s => s.AdminPersonIds);
        seed.StringList("_hosts", "Hosts");
        seed.StringList("_modules", "Modules");
        seed.Property<List<Guid>>("_adminPersonIds").HasColumnName("AdminPersonIds")
            .HasConversion(JsonLists.Converter<Guid>(), JsonLists.Comparer<Guid>());
        seed.HasMany(s => s.History).WithOne().HasForeignKey("SeedId").OnDelete(DeleteBehavior.Cascade);
        seed.Navigation(s => s.History).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public sealed class SeedStatusEntryConfiguration : IEntityTypeConfiguration<SeedStatusEntry>
{
    public void Configure(EntityTypeBuilder<SeedStatusEntry> entry)
    {
        entry.ToTable("tenant_seed_history");
        entry.Property<long>("Id");
        entry.HasKey("Id");
        entry.Property<string>("SeedId").HasMaxLength(TenantSeed.IdMaxLength);
        entry.HasIndex("SeedId");
        entry.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        entry.Property(e => e.Reason).HasMaxLength(1000);
        entry.Property(e => e.ActualDatabaseServer).HasMaxLength(Rules.ItemMaxLength);
        entry.Property(e => e.ActualDatabaseName).HasMaxLength(Rules.ItemMaxLength);
    }
}

public sealed class TenantLinkConfiguration : IEntityTypeConfiguration<TenantLink>
{
    public void Configure(EntityTypeBuilder<TenantLink> link)
    {
        link.ToTable("tenant_links");
        link.Property(l => l.Id).ValueGeneratedNever();
        link.HasIndex(l => l.OrgId);
        link.Property(l => l.FromSeedId).HasMaxLength(TenantSeed.IdMaxLength);
        link.Property(l => l.ToSeedId).HasMaxLength(TenantSeed.IdMaxLength);
        link.Property(l => l.State).HasConversion<string>().HasMaxLength(20);
    }
}

public sealed class FeedEntryConfiguration : IEntityTypeConfiguration<FeedEntry>
{
    public void Configure(EntityTypeBuilder<FeedEntry> entry)
    {
        entry.ToTable("feed");
        entry.Property(e => e.Id).ValueGeneratedOnAdd();
        entry.HasIndex(e => new { e.OrgId, e.Id });
        entry.Property(e => e.Type).HasMaxLength(FeedEntry.TypeMaxLength);
        entry.Property(e => e.Subject).HasMaxLength(FeedEntry.SubjectMaxLength);
        entry.Property(e => e.UnitPath).HasMaxLength(OrgUnit.PathMaxLength);
        // Unbounded: a unit snapshot with its lists can be long (text, nvarchar(max), longtext, NCLOB).
        entry.Property(e => e.Data);
    }
}

internal static class ConfigurationExtensions
{
    public static void Names<T>(this EntityTypeBuilder<T> builder) where T : class
    {
        builder.Property<string>("NameEn").HasMaxLength(Rules.NameMaxLength);
        builder.Property<string>("NameAr").HasMaxLength(Rules.NameMaxLength);
    }

    /// <summary>A <c>List&lt;string&gt;</c> field stored as one JSON text column.</summary>
    public static void StringList<T>(this EntityTypeBuilder<T> builder, string field, string column) where T : class =>
        builder.Property<List<string>>(field).HasColumnName(column).HasConversion(JsonLists.Converter<string>(), JsonLists.Comparer<string>());
}

/// <summary>Short lists as JSON text: the same on every engine, no primitive-collection support needed.</summary>
internal static class JsonLists
{
    public static ValueConverter<List<T>, string> Converter<T>() => new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v) ? new List<T>() : JsonSerializer.Deserialize<List<T>>(v, (JsonSerializerOptions?)null) ?? new List<T>());

    public static ValueComparer<List<T>> Comparer<T>() => new(
        (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
        v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        v => v.ToList());
}
