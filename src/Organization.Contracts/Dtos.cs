using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft.square.net.Organization.Contracts;

// The central management API's resources, as it returns them. Ids are GUIDs, except a tenant seed's, which is the
// text id the host's tenant stays bound to. Names come in English and Arabic.

/// <summary>A unit either owns a tenant (through one of the org's tenant seeds) or lives in the tenant of the nearest ancestor that owns one.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TenantBindingKind>))]
public enum TenantBindingKind { Inherit, OwnTenant }

public sealed record TenantBindingDto(TenantBindingKind Kind, string? SeedId);

/// <summary>A customer admin of an org: an identity (token issuer and subject).</summary>
public sealed record OrgAdminDto(string Issuer, string Subject);

/// <summary>An organization (a customer of the central service). Created with its root unit, of the root unit type.</summary>
public sealed record OrgDto(
    Guid Id, string Key, string NameEn, string NameAr, Guid RootUnitId, IReadOnlyList<OrgAdminDto> Admins, int Version, DateTimeOffset CreatedAt);

/// <summary>
/// A kind of unit in one org. <see cref="AllowedParents"/> are the keys of the types it may sit under (empty: a root
/// type). <see cref="HoldsCategories"/>: units of this type own a list of post categories, at least one (Section).
/// </summary>
public sealed record UnitTypeDto(
    Guid Id, Guid OrgId, string Key, string NameEn, string NameAr, IReadOnlyList<string> AllowedParents, bool HoldsCategories, int Order);

/// <summary>
/// One unit of an org's tree. <see cref="Path"/> lists the ids from the root to this unit (see <see cref="UnitPaths"/>).
/// <see cref="Categories"/> are Content post category ids (units whose type holds categories only).
/// </summary>
public sealed record OrgUnitDto(
    Guid Id, Guid OrgId, Guid? ParentId, string TypeKey, string Path, string NameEn, string NameAr, IReadOnlyList<string> Tags,
    string? TimeZone, IReadOnlyList<string> Categories, TenantBindingDto TenantBinding, int Version, DateTimeOffset UpdatedAt);

/// <summary>The tenant a unit lives in: its own seed's, or the nearest ancestor's that owns one; none when no unit on its path owns a tenant.</summary>
public sealed record UnitTenantDto(Guid UnitId, string? SeedId, Guid? OwnerUnitId, string? DeploymentId, string? TenantId);

/// <summary>HR data of a person who is an employee (optional: a member needs only an identity).</summary>
public sealed record EmployeeProfileDto(string? EmployeeNumber, string? JobTitle, DateOnly? HiredOn);

/// <summary>A person of an org: an identity (token issuer and subject), with an optional employee profile.</summary>
public sealed record PersonDto(
    Guid Id, Guid OrgId, string Issuer, string Subject, string DisplayName, string? Email, EmployeeProfileDto? Employee, int Version);

/// <summary>A person's place in a unit, with a position, between <see cref="From"/> and <see cref="To"/> (open when null).</summary>
public sealed record MembershipDto(
    Guid Id, Guid OrgId, Guid PersonId, Guid UnitId, Guid? PositionId, string? PositionKey, DateTimeOffset From, DateTimeOffset? To, bool Primary);

/// <summary>A position of an org (Manager, Unit admin, ...) and the permissions it gives on the unit's scope, from the platform's permission catalog.</summary>
public sealed record PositionDto(Guid Id, Guid OrgId, string Key, string NameEn, string NameAr, IReadOnlyList<string> Permissions, int Version);

/// <summary>A tenant seed's state as the host reports it (the same names as the platform's <c>SeedStatus</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeedState>))]
public enum SeedState { Requested, Provisioning, Provisioned, Adjusted, Rejected, Failed }

public sealed record PlacementDto(string DatabaseServer, string DatabaseName);

/// <summary>One report of a host about a seed version (or the central service's own Requested entry).</summary>
public sealed record SeedStatusEntryDto(DateTimeOffset At, long Version, SeedState Status, string? Reason, PlacementDto? ActualPlacement);

/// <summary>
/// A tenant a deployment should have, owned by the unit <see cref="RootUnitId"/> (which is bound to it). The database
/// placement is a request: the host may adjust it or reject the seed, and reports back.
/// <see cref="AdminPersonIds"/> become unit admins of the root unit, and the host grants them <c>access.grants.manage</c>.
/// </summary>
public sealed record TenantSeedDto(
    string Id, Guid OrgId, Guid RootUnitId, long Version, string DeploymentId, string TenantId, string Name, IReadOnlyList<string> Hosts,
    string DatabaseServer, string DatabaseName, IReadOnlyList<string> Modules, IReadOnlyList<Guid> AdminPersonIds,
    SeedState Status, string? Reason, long? ReportedVersion, PlacementDto? ActualPlacement, IReadOnlyList<SeedStatusEntryDto> History);

/// <summary>
/// A seed as a deployment's connector pulls it: the platform's <c>TenantSeed</c> shape, with the admins as token
/// subjects, plus the org and root unit the tenant belongs to.
/// </summary>
public sealed record DeploymentSeedDto(
    string Id, long Version, string DeploymentId, string TenantId, string Name, IReadOnlyList<string> Hosts, string DatabaseServer,
    string DatabaseName, IReadOnlyList<string> Modules, IReadOnlyList<string> Admins, Guid OrgId, Guid RootUnitId);

/// <summary>States of a link between sibling tenants: Requested → Active | Declined, Active → Revoked.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TenantLinkState>))]
public enum TenantLinkState { Requested, Active, Declined, Revoked }

/// <summary>A link between two tenants of an org (skeleton: states only; no capabilities yet).</summary>
public sealed record TenantLinkDto(
    Guid Id, Guid OrgId, string FromSeedId, Guid FromUnitId, string ToSeedId, Guid ToUnitId, TenantLinkState State,
    DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt);

/// <summary>One change of an org, in the order the central service wrote it. <see cref="Data"/> is the event's v1 record (see <see cref="OrgEvents"/>).</summary>
public sealed record FeedEntryDto(long Sequence, Guid OrgId, string Type, string Subject, string? UnitPath, DateTimeOffset OccurredAt, JsonElement Data);

/// <summary>A page of an org's feed. Read the next page with <c>after = Next</c>; <see cref="More"/> says whether it has entries now.</summary>
public sealed record FeedPage(IReadOnlyList<FeedEntryDto> Entries, long Next, bool More);

// Requests of the management API.

public sealed record CreateOrgRequest(string Key, string NameEn, string NameAr, IReadOnlyList<OrgAdminDto>? Admins = null);

public sealed record UpdateOrgRequest(int Version, string NameEn, string NameAr);

public sealed record SetOrgAdminsRequest(IReadOnlyList<OrgAdminDto> Admins);

public sealed record CreateUnitTypeRequest(string Key, string NameEn, string NameAr, IReadOnlyList<string> AllowedParents, bool HoldsCategories, int Order = 0);

public sealed record CreateUnitRequest(
    Guid ParentId, string TypeKey, string NameEn, string NameAr, IReadOnlyList<string>? Tags = null, string? TimeZone = null,
    IReadOnlyList<string>? Categories = null);

public sealed record UpdateUnitRequest(
    int Version, string NameEn, string NameAr, IReadOnlyList<string>? Tags = null, string? TimeZone = null, IReadOnlyList<string>? Categories = null);

public sealed record MoveUnitRequest(int Version, Guid NewParentId);

public sealed record CreatePersonRequest(string Issuer, string Subject, string DisplayName, string? Email = null, EmployeeProfileDto? Employee = null);

public sealed record UpdatePersonRequest(int Version, string DisplayName, string? Email = null, EmployeeProfileDto? Employee = null);

public sealed record CreateMembershipRequest(Guid PersonId, Guid UnitId, Guid? PositionId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, bool Primary = false);

public sealed record UpdateMembershipRequest(Guid? PositionId, DateTimeOffset From, DateTimeOffset? To, bool Primary);

public sealed record CreatePositionRequest(string Key, string NameEn, string NameAr, IReadOnlyList<string> Permissions);

public sealed record UpdatePositionRequest(int Version, string NameEn, string NameAr, IReadOnlyList<string> Permissions);

public sealed record CreateSeedRequest(
    Guid RootUnitId, string DeploymentId, string TenantId, string Name, IReadOnlyList<string> Hosts, string DatabaseServer, string DatabaseName,
    IReadOnlyList<string>? Modules = null, IReadOnlyList<Guid>? AdminPersonIds = null);

public sealed record UpdateSeedRequest(
    long Version, string Name, IReadOnlyList<string> Hosts, string DatabaseServer, string DatabaseName, IReadOnlyList<string>? Modules = null,
    IReadOnlyList<Guid>? AdminPersonIds = null);

/// <summary>A host's outcome for one version of a seed (sent by the deployment's connector).</summary>
public sealed record SeedReportRequest(long Version, SeedState Status, PlacementDto? ActualPlacement = null, string? Reason = null);
