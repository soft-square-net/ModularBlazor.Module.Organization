namespace soft.square.net.WebApi.Modules.Organization.Core.PersonAggregate;

/// <summary>
/// A person of an org: an identity account (the token's issuer and subject; one issuer today, both stored so more
/// issuers need no migration), with an optional employee profile. "Employee" is a profile, not a kind of membership:
/// anyone who can be a member needs only the identity.
/// </summary>
public sealed class Person : AggregateRoot<Guid>
{
    private Person() { } // EF Core

    public Guid OrgId { get; private set; }
    public string Issuer { get; private set; } = "";
    public string Subject { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string? Email { get; private set; }

    public bool IsEmployee { get; private set; }
    public string? EmployeeNumber { get; private set; }
    public string? JobTitle { get; private set; }
    public DateOnly? HiredOn { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int Version { get; private set; }

    public EmployeeProfile? Employee => IsEmployee ? new EmployeeProfile(EmployeeNumber, JobTitle, HiredOn) : null;

    public static Person Create(Guid orgId, string? issuer, string? subject, string? displayName, string? email, EmployeeProfile? employee, DateTimeOffset now)
    {
        var person = new Person
        {
            Id = Guid.CreateVersion7(),
            OrgId = orgId,
            Issuer = Rules.Required(issuer, "A person's issuer", Rules.IdentityMaxLength),
            Subject = Rules.Required(subject, "A person's subject", Rules.SubjectMaxLength),
            CreatedAt = now,
            UpdatedAt = now
        };
        person.SetDetails(displayName, email, employee);
        return person;
    }

    public void Update(string? displayName, string? email, EmployeeProfile? employee, DateTimeOffset now)
    {
        SetDetails(displayName, email, employee);
        UpdatedAt = now;
        Version++;
    }

    public bool Is(string issuer, string subject) =>
        string.Equals(Issuer, issuer, StringComparison.Ordinal) && string.Equals(Subject, subject, StringComparison.Ordinal);

    private void SetDetails(string? displayName, string? email, EmployeeProfile? employee)
    {
        DisplayName = Rules.Required(displayName, "A person's display name", Rules.NameMaxLength);
        Email = Rules.Optional(email, "An email", Rules.IdentityMaxLength);
        if (Email is not null && !Email.Contains('@')) throw new DomainException($"'{Email}' is not an email address.");
        IsEmployee = employee is not null;
        EmployeeNumber = Rules.Optional(employee?.EmployeeNumber, "An employee number", Rules.ItemMaxLength);
        JobTitle = Rules.Optional(employee?.JobTitle, "A job title", Rules.TextMaxLength);
        HiredOn = employee?.HiredOn;
    }
}

/// <summary>HR data of an employee.</summary>
public sealed record EmployeeProfile(string? EmployeeNumber, string? JobTitle, DateOnly? HiredOn);
