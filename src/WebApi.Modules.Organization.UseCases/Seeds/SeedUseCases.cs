using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.MembershipAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.TenantSeedAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;

public sealed record ListSeedsQuery(Guid OrgId) : IQuery<Result<IReadOnlyList<TenantSeedDto>>>;

public sealed class ListSeedsHandler(IOrgAccess access, IReadRepository<TenantSeed> seeds) : IQueryHandler<ListSeedsQuery, Result<IReadOnlyList<TenantSeedDto>>>
{
    public async ValueTask<Result<IReadOnlyList<TenantSeedDto>>> Handle(ListSeedsQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<TenantSeedDto>>();
        if (!rights.Value.Administers && !rights.Value.IsServiceClient && !rights.Value.CanSomewhere(OrgPermissions.SeedsManage))
            return Refusals.Forbidden<IReadOnlyList<TenantSeedDto>>("read this org's tenant seeds");
        return (await seeds.ListAsync(new SeedsOfOrgSpec(query.OrgId), ct)).Select(s => s.ToDto()).ToList();
    }
}

/// <summary>
/// A tenant for a unit (customer admins). The unit becomes the tenant's root (<c>OwnTenant</c>), and the seed's admins
/// become unit admins of it. The host of the seed's deployment picks it up on its next reconciliation.
/// </summary>
public sealed record CreateSeedCommand(Guid OrgId, CreateSeedRequest Request) : ICommand<Result<TenantSeedDto>>;

public sealed class CreateSeedHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<TenantSeed> seeds, SeedAdmins admins, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<CreateSeedCommand, Result<TenantSeedDto>>
{
    public async ValueTask<Result<TenantSeedDto>> Handle(CreateSeedCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<TenantSeedDto>();
        if (!rights.Value.Administers) return Refusals.Forbidden<TenantSeedDto>("create tenants for this org");
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, r.RootUnitId), ct);
        if (unit is null) return Result<TenantSeedDto>.NotFound($"There is no unit {r.RootUnitId}.");
        if (unit.Binding == TenantBindingKind.OwnTenant || await seeds.AnyAsync(new SeedOfUnitSpec(unit.Id), ct))
            return Result<TenantSeedDto>.Conflict("The unit already owns a tenant.");

        var now = clock.GetUtcNow();
        var seed = TenantSeed.Create(command.OrgId, unit.Id, r.DeploymentId, r.TenantId, r.Name, r.Hosts, r.DatabaseServer, r.DatabaseName, r.Modules, r.AdminPersonIds, now);
        if (await seeds.AnyAsync(new SeedByTenantSpec(seed.DeploymentId, seed.TenantId), ct))
            return Result<TenantSeedDto>.Conflict($"Deployment '{seed.DeploymentId}' already has a tenant '{seed.TenantId}'.");
        var made = await admins.EnsureUnitAdminsAsync(seed, unit, now, ct);
        if (!made.IsSuccess) return made.Fail<TenantSeedDto>();

        unit.BindOwnTenant(seed.Id, now);
        store.Add(seed);
        feed.UnitUpdated(unit);
        feed.SeedChanged(seed, unit.Path);
        await store.SaveAsync(ct);
        return seed.ToDto();
    }
}

/// <summary>Edits a seed: a new version the host reconciles again (customer admins, or <c>org.seeds.manage</c> on its unit).</summary>
public sealed record UpdateSeedCommand(Guid OrgId, string SeedId, UpdateSeedRequest Request) : ICommand<Result<TenantSeedDto>>;

public sealed class UpdateSeedHandler(IOrgAccess access, IReadRepository<OrgUnit> units, IReadRepository<TenantSeed> seeds, SeedAdmins admins, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<UpdateSeedCommand, Result<TenantSeedDto>>
{
    public async ValueTask<Result<TenantSeedDto>> Handle(UpdateSeedCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<TenantSeedDto>();
        var seed = await seeds.FirstOrDefaultAsync(new SeedByIdSpec(command.SeedId), ct);
        if (seed is null || seed.OrgId != command.OrgId) return Result<TenantSeedDto>.NotFound($"There is no seed {command.SeedId}.");
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(command.OrgId, seed.RootUnitId), ct);
        if (unit is null) return Result<TenantSeedDto>.NotFound($"There is no unit {seed.RootUnitId}.");
        if (!rights.Value.Can(OrgPermissions.SeedsManage, unit.Path)) return Refusals.Forbidden<TenantSeedDto>("change this tenant seed");

        var now = clock.GetUtcNow();
        seed.Edit(r.Version, r.Name, r.Hosts, r.DatabaseServer, r.DatabaseName, r.Modules, r.AdminPersonIds, now);
        var made = await admins.EnsureUnitAdminsAsync(seed, unit, now, ct);
        if (!made.IsSuccess) return made.Fail<TenantSeedDto>();
        feed.SeedChanged(seed, unit.Path);
        await store.SaveAsync(ct);
        return seed.ToDto();
    }
}

/// <summary>The seeds of a deployment, as its connector pulls them (service clients and platform admins).</summary>
public sealed record ListDeploymentSeedsQuery(string DeploymentId) : IQuery<Result<IReadOnlyList<DeploymentSeedDto>>>;

public sealed class ListDeploymentSeedsHandler(IOrgAccess access, IReadRepository<TenantSeed> seeds, IReadRepository<Person> persons)
    : IQueryHandler<ListDeploymentSeedsQuery, Result<IReadOnlyList<DeploymentSeedDto>>>
{
    public async ValueTask<Result<IReadOnlyList<DeploymentSeedDto>>> Handle(ListDeploymentSeedsQuery query, CancellationToken ct)
    {
        if (!access.IsSignedIn) return Result<IReadOnlyList<DeploymentSeedDto>>.Unauthorized("Sign in first.");
        if (!access.Caller.IsServiceClient && !access.Caller.IsPlatformAdmin) return Refusals.Forbidden<IReadOnlyList<DeploymentSeedDto>>("read deployment seeds");

        var list = await seeds.ListAsync(new SeedsOfDeploymentSpec(query.DeploymentId?.Trim() ?? ""), ct);
        var subjects = new Dictionary<Guid, string>();
        foreach (var org in list.GroupBy(s => s.OrgId))
        {
            var ids = org.SelectMany(s => s.AdminPersonIds).Distinct().ToList();
            if (ids.Count == 0) continue;
            foreach (var person in await persons.ListAsync(new PersonsByIdsSpec(org.Key, ids), ct)) subjects[person.Id] = person.Subject;
        }
        return list.Select(s => new DeploymentSeedDto(s.Id, s.Version, s.DeploymentId, s.TenantId, s.Name, [.. s.Hosts], s.DatabaseServer, s.DatabaseName,
            [.. s.Modules], [.. s.AdminPersonIds.Where(subjects.ContainsKey).Select(id => subjects[id])], s.OrgId, s.RootUnitId)).ToList();
    }
}

/// <summary>A host's outcome for one version of a seed (service clients and platform admins).</summary>
public sealed record ReportSeedCommand(string SeedId, SeedReportRequest Report) : ICommand<Result>;

public sealed class ReportSeedHandler(IOrgAccess access, IReadRepository<TenantSeed> seeds, IReadRepository<OrgUnit> units, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<ReportSeedCommand, Result>
{
    public async ValueTask<Result> Handle(ReportSeedCommand command, CancellationToken ct)
    {
        if (!access.IsSignedIn) return Result.Unauthorized("Sign in first.");
        if (!access.Caller.IsServiceClient && !access.Caller.IsPlatformAdmin) return Refusals.Forbidden("report seed status");
        var seed = await seeds.FirstOrDefaultAsync(new SeedByIdSpec(command.SeedId), ct);
        if (seed is null) return Result.NotFound($"There is no seed {command.SeedId}.");

        var r = command.Report;
        seed.Report(r.Version, r.Status, r.ActualPlacement?.DatabaseServer, r.ActualPlacement?.DatabaseName, r.Reason, clock.GetUtcNow());
        var unit = await units.FirstOrDefaultAsync(new UnitByIdSpec(seed.OrgId, seed.RootUnitId), ct);
        feed.SeedChanged(seed, unit?.Path);
        await store.SaveAsync(ct);
        return Result.Success();
    }
}

/// <summary>A seed's admins are unit admins of its root unit: each one without such a membership gets one.</summary>
public sealed class SeedAdmins(IReadRepository<Person> persons, IReadRepository<PositionDefinition> positions, IReadRepository<Membership> memberships, IOrgStore store, OrgFeed feed)
{
    public async Task<Result> EnsureUnitAdminsAsync(TenantSeed seed, OrgUnit root, DateTimeOffset now, CancellationToken ct)
    {
        if (seed.AdminPersonIds.Count == 0) return Result.Success();
        var admins = await persons.ListAsync(new PersonsByIdsSpec(seed.OrgId, [.. seed.AdminPersonIds]), ct);
        var missing = seed.AdminPersonIds.Except(admins.Select(p => p.Id)).ToList();
        if (missing.Count > 0) return Result.Invalid(new ValidationError("AdminPersonIds", $"These are not persons of the org: {string.Join(", ", missing)}."));

        var position = await positions.FirstOrDefaultAsync(new PositionByKeySpec(seed.OrgId, PositionDefinition.UnitAdminKey), ct);
        if (position is null) return Result.Conflict($"The org has no '{PositionDefinition.UnitAdminKey}' position for the seed's admins.");
        var current = await memberships.ListAsync(new MembershipsSpec(seed.OrgId, unitId: root.Id), ct);
        foreach (var person in admins.Where(p => !current.Any(m => m.PersonId == p.Id && m.PositionId == position.Id && m.IsActiveAt(now))))
        {
            var membership = Membership.Create(person, root, position, now, null, primary: false, now);
            store.Add(membership);
            feed.MembershipChanged(membership, person, root, position);
        }
        return Result.Success();
    }
}
