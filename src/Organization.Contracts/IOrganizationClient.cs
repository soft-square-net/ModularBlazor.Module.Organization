namespace soft.square.net.Organization.Contracts;

/// <summary>
/// The central Organization service's management API, as a typed client contract. Implemented over HTTP by
/// <c>Organization.Client</c> (P1-3), against <c>Organization:BaseUrl</c> or the gateway route <c>/api/organization</c>.
/// Calls are authorized by the service's own data: platform admins, the org's customer admins, unit admins and
/// managers on their subtree (by membership position), and the configured service clients (connectors) for seeds and
/// feeds. A refused call fails with 403, a missing resource with 404, a broken rule or a stale version with 409.
/// </summary>
public interface IOrganizationClient
{
    // Orgs (customers)
    Task<IReadOnlyList<OrgDto>> ListOrgsAsync(CancellationToken ct = default);
    Task<OrgDto> GetOrgAsync(Guid orgId, CancellationToken ct = default);
    Task<OrgDto> CreateOrgAsync(CreateOrgRequest request, CancellationToken ct = default);
    Task<OrgDto> UpdateOrgAsync(Guid orgId, UpdateOrgRequest request, CancellationToken ct = default);
    Task<OrgDto> SetOrgAdminsAsync(Guid orgId, SetOrgAdminsRequest request, CancellationToken ct = default);

    // Unit types
    Task<IReadOnlyList<UnitTypeDto>> ListUnitTypesAsync(Guid orgId, CancellationToken ct = default);
    Task<UnitTypeDto> CreateUnitTypeAsync(Guid orgId, CreateUnitTypeRequest request, CancellationToken ct = default);

    // Units
    Task<IReadOnlyList<OrgUnitDto>> ListUnitsAsync(Guid orgId, CancellationToken ct = default);
    Task<OrgUnitDto> GetUnitAsync(Guid orgId, Guid unitId, CancellationToken ct = default);
    Task<UnitTenantDto> GetUnitTenantAsync(Guid orgId, Guid unitId, CancellationToken ct = default);
    Task<OrgUnitDto> CreateUnitAsync(Guid orgId, CreateUnitRequest request, CancellationToken ct = default);
    Task<OrgUnitDto> UpdateUnitAsync(Guid orgId, Guid unitId, UpdateUnitRequest request, CancellationToken ct = default);
    Task<OrgUnitDto> MoveUnitAsync(Guid orgId, Guid unitId, MoveUnitRequest request, CancellationToken ct = default);
    Task RemoveUnitAsync(Guid orgId, Guid unitId, CancellationToken ct = default);

    // Persons and memberships
    Task<IReadOnlyList<PersonDto>> ListPersonsAsync(Guid orgId, CancellationToken ct = default);
    Task<PersonDto> CreatePersonAsync(Guid orgId, CreatePersonRequest request, CancellationToken ct = default);
    Task<PersonDto> UpdatePersonAsync(Guid orgId, Guid personId, UpdatePersonRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<MembershipDto>> ListMembershipsAsync(Guid orgId, Guid? unitId = null, Guid? personId = null, CancellationToken ct = default);
    Task<MembershipDto> CreateMembershipAsync(Guid orgId, CreateMembershipRequest request, CancellationToken ct = default);
    Task<MembershipDto> UpdateMembershipAsync(Guid orgId, Guid membershipId, UpdateMembershipRequest request, CancellationToken ct = default);
    Task RemoveMembershipAsync(Guid orgId, Guid membershipId, CancellationToken ct = default);

    // Positions
    Task<IReadOnlyList<PositionDto>> ListPositionsAsync(Guid orgId, CancellationToken ct = default);
    Task<PositionDto> CreatePositionAsync(Guid orgId, CreatePositionRequest request, CancellationToken ct = default);
    Task<PositionDto> UpdatePositionAsync(Guid orgId, Guid positionId, UpdatePositionRequest request, CancellationToken ct = default);

    // Tenant seeds and links
    Task<IReadOnlyList<TenantSeedDto>> ListSeedsAsync(Guid orgId, CancellationToken ct = default);
    Task<TenantSeedDto> CreateSeedAsync(Guid orgId, CreateSeedRequest request, CancellationToken ct = default);
    Task<TenantSeedDto> UpdateSeedAsync(Guid orgId, string seedId, UpdateSeedRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TenantLinkDto>> ListLinksAsync(Guid orgId, CancellationToken ct = default);

    // Connector (service clients)
    Task<IReadOnlyList<DeploymentSeedDto>> ListDeploymentSeedsAsync(string deploymentId, CancellationToken ct = default);
    Task ReportSeedAsync(string seedId, SeedReportRequest report, CancellationToken ct = default);
    Task<FeedPage> ReadFeedAsync(Guid orgId, long after = 0, int take = 200, CancellationToken ct = default);
}
