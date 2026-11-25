using soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantLinkAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>The aggregates as the contract's DTOs and event records.</summary>
public static class Mapping
{
    public static OrgDto ToDto(this Org o) =>
        new(o.Id, o.Key, o.NameEn, o.NameAr, o.RootUnitId, [.. o.Admins.Select(a => new OrgAdminDto(a.Issuer, a.Subject))], o.Version, o.CreatedAt);

    public static UnitTypeDto ToDto(this UnitType t) => new(t.Id, t.OrgId, t.Key, t.NameEn, t.NameAr, [.. t.AllowedParents], t.HoldsCategories, t.Order);

    public static OrgUnitDto ToDto(this OrgUnit u) =>
        new(u.Id, u.OrgId, u.ParentId, u.TypeKey, u.Path, u.NameEn, u.NameAr, [.. u.Tags], u.TimeZone, u.CategoryIds,
            new TenantBindingDto(u.Binding, u.SeedId), u.Version, u.UpdatedAt);

    public static OrgUnitSnapshot ToSnapshot(this OrgUnit u) =>
        new(u.Id, u.OrgId, u.ParentId, u.TypeKey, u.Path, u.NameEn, u.NameAr, [.. u.Tags], u.TimeZone, u.CategoryIds, u.Binding, u.SeedId, u.Version);

    public static PersonDto ToDto(this Person p) =>
        new(p.Id, p.OrgId, p.Issuer, p.Subject, p.DisplayName, p.Email,
            p.Employee is { } e ? new EmployeeProfileDto(e.EmployeeNumber, e.JobTitle, e.HiredOn) : null, p.Version);

    public static EmployeeProfile? ToProfile(this EmployeeProfileDto? e) => e is null ? null : new EmployeeProfile(e.EmployeeNumber, e.JobTitle, e.HiredOn);

    public static MembershipDto ToDto(this Membership m, PositionDefinition? position) =>
        new(m.Id, m.OrgId, m.PersonId, m.UnitId, m.PositionId, position?.Key, m.From, m.To, m.Primary);

    public static PositionDto ToDto(this PositionDefinition p) => new(p.Id, p.OrgId, p.Key, p.NameEn, p.NameAr, [.. p.Permissions], p.Version);

    public static TenantSeedDto ToDto(this TenantSeed s) =>
        new(s.Id, s.OrgId, s.RootUnitId, s.Version, s.DeploymentId, s.TenantId, s.Name, [.. s.Hosts], s.DatabaseServer, s.DatabaseName,
            [.. s.Modules], [.. s.AdminPersonIds], s.Status, s.Reason, s.ReportedVersion, Placement(s.ActualDatabaseServer, s.ActualDatabaseName),
            [.. s.History.Select(h => new SeedStatusEntryDto(h.At, h.Version, h.Status, h.Reason, Placement(h.ActualDatabaseServer, h.ActualDatabaseName)))]);

    public static TenantLinkDto ToDto(this TenantLink l) =>
        new(l.Id, l.OrgId, l.FromSeedId, l.FromUnitId, l.ToSeedId, l.ToUnitId, l.State, l.RequestedAt, l.DecidedAt);

    public static FeedEntryDto ToDto(this FeedEntry e) => new(e.Id, e.OrgId, e.Type, e.Subject, e.UnitPath, e.OccurredAt, OrgFeed.Parse(e.Data));

    private static PlacementDto? Placement(string? server, string? database) =>
        server is null || database is null ? null : new PlacementDto(server, database);
}

/// <summary>The feed entry of each kind of change.</summary>
public static class FeedEvents
{
    public static void UnitCreated(this OrgFeed feed, OrgUnit u) =>
        feed.Append(u.OrgId, OrgEvents.UnitCreated, new UnitCreated(u.ToSnapshot()), OrgEvents.UnitSubject(u.Id), u.Path);

    public static void UnitUpdated(this OrgFeed feed, OrgUnit u) =>
        feed.Append(u.OrgId, OrgEvents.UnitUpdated, new UnitUpdated(u.ToSnapshot()), OrgEvents.UnitSubject(u.Id), u.Path);

    public static void UnitMoved(this OrgFeed feed, OrgUnit u, Guid? oldParentId, string oldPath) =>
        feed.Append(u.OrgId, OrgEvents.UnitMoved, new UnitMoved(u.Id, u.OrgId, oldParentId, u.ParentId!.Value, oldPath, u.Path, u.Version),
            OrgEvents.UnitSubject(u.Id), u.Path);

    public static void UnitRemoved(this OrgFeed feed, OrgUnit u) =>
        feed.Append(u.OrgId, OrgEvents.UnitRemoved, new UnitRemoved(u.Id, u.OrgId, u.Path), OrgEvents.UnitSubject(u.Id), u.Path);

    public static void MembershipChanged(this OrgFeed feed, Membership m, Person person, OrgUnit unit, PositionDefinition? position, bool removed = false) =>
        feed.Append(m.OrgId, OrgEvents.MembershipChanged,
            new MembershipChanged(m.Id, m.OrgId, person.Id, person.Issuer, person.Subject, unit.Id, unit.Path, position?.Id, position?.Key,
                position is null ? [] : [.. position.Permissions], m.From, m.To, m.Primary, removed),
            OrgEvents.MembershipSubject(m.Id), unit.Path);

    public static void PersonChanged(this OrgFeed feed, Person p) =>
        feed.Append(p.OrgId, OrgEvents.PersonChanged, new PersonChanged(p.Id, p.OrgId, p.Issuer, p.Subject, p.DisplayName, p.Email, p.IsEmployee),
            OrgEvents.PersonSubject(p.Id));

    public static void PositionChanged(this OrgFeed feed, PositionDefinition p) =>
        feed.Append(p.OrgId, OrgEvents.PositionChanged, new PositionChanged(p.Id, p.OrgId, p.Key, p.NameEn, p.NameAr, [.. p.Permissions]),
            OrgEvents.PositionSubject(p.Id));

    public static void SeedChanged(this OrgFeed feed, TenantSeed s, string? rootUnitPath) =>
        feed.Append(s.OrgId, OrgEvents.TenantSeedChanged,
            new TenantSeedChanged(s.Id, s.OrgId, s.RootUnitId, s.Version, s.DeploymentId, s.TenantId, s.Status, s.ReportedVersion),
            OrgEvents.SeedSubject(s.Id), rootUnitPath);
}
