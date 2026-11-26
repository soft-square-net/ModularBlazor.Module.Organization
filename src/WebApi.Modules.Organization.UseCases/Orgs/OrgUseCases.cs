using soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.PositionAggregate;
using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;

/// <summary>The orgs the caller can see: all of them for a platform admin, otherwise those they administer or belong to.</summary>
public sealed record ListOrgsQuery : IQuery<Result<IReadOnlyList<OrgDto>>>;

public sealed class ListOrgsHandler(IOrgAccess access, IReadRepository<Org> orgs, IReadRepository<Person> persons)
    : IQueryHandler<ListOrgsQuery, Result<IReadOnlyList<OrgDto>>>
{
    public async ValueTask<Result<IReadOnlyList<OrgDto>>> Handle(ListOrgsQuery query, CancellationToken ct)
    {
        if (!access.IsSignedIn) return Result<IReadOnlyList<OrgDto>>.Unauthorized("Sign in first.");
        var caller = access.Caller;
        if (caller.IsPlatformAdmin || caller.IsServiceClient) return (await orgs.ListAsync(new OrgsSpec(), ct)).Select(o => o.ToDto()).ToList();

        var memberOf = (await persons.ListAsync(new PersonsOfIdentitySpec(caller.Issuer!, caller.Subject!), ct)).Select(p => p.OrgId);
        var administered = (await orgs.ListAsync(new OrgsAdministeredBySpec(caller.Issuer!, caller.Subject!), ct)).Select(o => o.Id);
        var ids = memberOf.Concat(administered).Distinct().ToList();
        if (ids.Count == 0) return Result<IReadOnlyList<OrgDto>>.Success([]);
        return (await orgs.ListAsync(new OrgsSpec(ids), ct)).Select(o => o.ToDto()).ToList();
    }
}

public sealed record GetOrgQuery(Guid OrgId) : IQuery<Result<OrgDto>>;

public sealed class GetOrgHandler(IOrgAccess access) : IQueryHandler<GetOrgQuery, Result<OrgDto>>
{
    public async ValueTask<Result<OrgDto>> Handle(GetOrgQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgDto>();
        return rights.Value.CanRead ? rights.Value.Org.ToDto() : Refusals.Forbidden<OrgDto>("read this org");
    }
}

/// <summary>
/// A new customer (platform admins only), with its root unit (named like the org), the default unit types
/// (Org &gt; Sector &gt; Department &gt; Section) and the default positions (Unit admin, Manager).
/// </summary>
public sealed record CreateOrgCommand(CreateOrgRequest Request) : ICommand<Result<OrgDto>>;

public sealed class CreateOrgHandler(IOrgAccess access, IReadRepository<Org> orgs, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<CreateOrgCommand, Result<OrgDto>>
{
    public async ValueTask<Result<OrgDto>> Handle(CreateOrgCommand command, CancellationToken ct)
    {
        if (!access.IsSignedIn) return Result<OrgDto>.Unauthorized("Sign in first.");
        if (!access.Caller.IsPlatformAdmin) return Refusals.Forbidden<OrgDto>("create orgs (platform admins only)");
        var r = command.Request;
        var now = clock.GetUtcNow();

        var orgId = Guid.CreateVersion7();
        var rootId = Guid.CreateVersion7();
        var org = Org.Create(orgId, rootId, r.Key, r.NameEn, r.NameAr, r.Admins?.Select(a => (a.Issuer, a.Subject)), now);
        if (await orgs.AnyAsync(new OrgByKeySpec(org.Key), ct)) return Result<OrgDto>.Conflict($"There is already an org '{org.Key}'.");

        var types = UnitType.Defaults(orgId);
        var root = OrgUnit.CreateRoot(rootId, orgId, types.Single(t => t.IsRoot), org.NameEn, org.NameAr, now);
        var positions = PositionDefinition.Defaults(orgId, now);

        store.Add(org);
        foreach (var type in types) store.Add(type);
        foreach (var position in positions)
        {
            store.Add(position);
            feed.PositionChanged(position);
        }
        store.Add(root);
        feed.UnitCreated(root);
        await store.SaveAsync(ct);
        return org.ToDto();
    }
}

public sealed record UpdateOrgCommand(Guid OrgId, UpdateOrgRequest Request) : ICommand<Result<OrgDto>>;

public sealed class UpdateOrgHandler(IOrgAccess access, IOrgStore store, TimeProvider clock) : ICommandHandler<UpdateOrgCommand, Result<OrgDto>>
{
    public async ValueTask<Result<OrgDto>> Handle(UpdateOrgCommand command, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgDto>();
        if (!rights.Value.Administers) return Refusals.Forbidden<OrgDto>("change this org");
        var org = rights.Value.Org;
        if (org.Version != command.Request.Version) return Result<OrgDto>.Conflict($"The org is at version {org.Version}; reload it and try again.");
        org.Rename(command.Request.NameEn, command.Request.NameAr, clock.GetUtcNow());
        await store.SaveAsync(ct);
        return org.ToDto();
    }
}

/// <summary>Replaces the org's customer admins (platform admins and the org's admins).</summary>
public sealed record SetOrgAdminsCommand(Guid OrgId, SetOrgAdminsRequest Request) : ICommand<Result<OrgDto>>;

public sealed class SetOrgAdminsHandler(IOrgAccess access, IOrgStore store, TimeProvider clock) : ICommandHandler<SetOrgAdminsCommand, Result<OrgDto>>
{
    public async ValueTask<Result<OrgDto>> Handle(SetOrgAdminsCommand command, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<OrgDto>();
        if (!rights.Value.Administers) return Refusals.Forbidden<OrgDto>("change this org's admins");
        var org = rights.Value.Org;
        org.SetAdmins(command.Request.Admins.Select(a => (a.Issuer, a.Subject)), clock.GetUtcNow());
        await store.SaveAsync(ct);
        return org.ToDto();
    }
}
