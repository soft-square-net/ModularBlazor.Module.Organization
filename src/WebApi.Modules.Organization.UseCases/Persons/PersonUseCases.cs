using soft.square.net.WebApi.Modules.Organization.Core;
using soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases.Persons;

public sealed record ListPersonsQuery(Guid OrgId) : IQuery<Result<IReadOnlyList<PersonDto>>>;

public sealed class ListPersonsHandler(IOrgAccess access, IReadRepository<Person> persons) : IQueryHandler<ListPersonsQuery, Result<IReadOnlyList<PersonDto>>>
{
    public async ValueTask<Result<IReadOnlyList<PersonDto>>> Handle(ListPersonsQuery query, CancellationToken ct)
    {
        var rights = await access.ForOrgAsync(query.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<IReadOnlyList<PersonDto>>();
        if (!rights.Value.CanRead) return Refusals.Forbidden<IReadOnlyList<PersonDto>>("read this org");
        return (await persons.ListAsync(new PersonsOfOrgSpec(query.OrgId), ct)).Select(p => p.ToDto()).ToList();
    }
}

/// <summary>A new person of the org (customer admins, or anyone who manages members somewhere in it). One per identity.</summary>
public sealed record CreatePersonCommand(Guid OrgId, CreatePersonRequest Request) : ICommand<Result<PersonDto>>;

public sealed class CreatePersonHandler(IOrgAccess access, IReadRepository<Person> persons, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<CreatePersonCommand, Result<PersonDto>>
{
    public async ValueTask<Result<PersonDto>> Handle(CreatePersonCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<PersonDto>();
        if (!rights.Value.CanSomewhere(OrgPermissions.MembersManage)) return Refusals.Forbidden<PersonDto>("add persons to this org");

        var person = Person.Create(command.OrgId, r.Issuer, r.Subject, r.DisplayName, r.Email, r.Employee.ToProfile(), clock.GetUtcNow());
        if (await persons.AnyAsync(new PersonByIdentitySpec(command.OrgId, person.Issuer, person.Subject), ct))
            return Result<PersonDto>.Conflict("That identity is already a person of this org.");
        store.Add(person);
        feed.PersonChanged(person);
        await store.SaveAsync(ct);
        return person.ToDto();
    }
}

public sealed record UpdatePersonCommand(Guid OrgId, Guid PersonId, UpdatePersonRequest Request) : ICommand<Result<PersonDto>>;

public sealed class UpdatePersonHandler(IOrgAccess access, IReadRepository<Person> persons, IOrgStore store, OrgFeed feed, TimeProvider clock)
    : ICommandHandler<UpdatePersonCommand, Result<PersonDto>>
{
    public async ValueTask<Result<PersonDto>> Handle(UpdatePersonCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var rights = await access.ForOrgAsync(command.OrgId, ct);
        if (!rights.IsSuccess) return rights.Fail<PersonDto>();
        if (!rights.Value.CanSomewhere(OrgPermissions.MembersManage)) return Refusals.Forbidden<PersonDto>("change persons of this org");
        var person = await persons.FirstOrDefaultAsync(new PersonByIdSpec(command.OrgId, command.PersonId), ct);
        if (person is null) return Result<PersonDto>.NotFound($"There is no person {command.PersonId}.");
        if (person.Version != r.Version) return Result<PersonDto>.Conflict($"The person is at version {person.Version}; reload and try again.");

        person.Update(r.DisplayName, r.Email, r.Employee.ToProfile(), clock.GetUtcNow());
        feed.PersonChanged(person);
        await store.SaveAsync(ct);
        return person.ToDto();
    }
}
