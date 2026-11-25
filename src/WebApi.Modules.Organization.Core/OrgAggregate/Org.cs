namespace soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate;

/// <summary>
/// An organization: one customer of the central service. Every other row carries its id. It is created with its root
/// unit (<see cref="RootUnitId"/>, of the root unit type) and its customer admins, who manage everything in it.
/// </summary>
public sealed class Org : AggregateRoot<Guid>
{
    private readonly List<OrgAdmin> _admins = [];

    private Org() { } // EF Core

    public string Key { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public Guid RootUnitId { get; private set; }
    public IReadOnlyList<OrgAdmin> Admins => _admins;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Bumped by every change; a concurrency token.</summary>
    public int Version { get; private set; }

    public static Org Create(Guid id, Guid rootUnitId, string key, string? nameEn, string? nameAr, IEnumerable<(string Issuer, string Subject)>? admins, DateTimeOffset now)
    {
        var org = new Org { Id = id, RootUnitId = rootUnitId, Key = Rules.Key(key, "An org's key"), CreatedAt = now, UpdatedAt = now };
        (org.NameEn, org.NameAr) = Rules.Names(nameEn, nameAr, "An org");
        org.ReplaceAdmins(admins ?? []);
        return org;
    }

    public void Rename(string? nameEn, string? nameAr, DateTimeOffset now)
    {
        (NameEn, NameAr) = Rules.Names(nameEn, nameAr, "An org");
        Touch(now);
    }

    public void SetAdmins(IEnumerable<(string Issuer, string Subject)> admins, DateTimeOffset now)
    {
        ReplaceAdmins(admins);
        Touch(now);
    }

    public bool IsAdmin(string issuer, string subject) =>
        _admins.Any(a => string.Equals(a.Issuer, issuer, StringComparison.Ordinal) && string.Equals(a.Subject, subject, StringComparison.Ordinal));

    private void ReplaceAdmins(IEnumerable<(string Issuer, string Subject)> admins)
    {
        var list = admins.Select(a => OrgAdmin.Create(a.Issuer, a.Subject)).DistinctBy(a => (a.Issuer, a.Subject)).ToList();
        if (list.Count > Rules.ListMaxItems) throw new DomainException($"An org has at most {Rules.ListMaxItems} admins.");
        _admins.Clear();
        _admins.AddRange(list);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>A customer admin: an identity (the token's issuer and subject).</summary>
public sealed class OrgAdmin
{
    private OrgAdmin() { } // EF Core

    public string Issuer { get; private set; } = "";
    public string Subject { get; private set; } = "";

    public static OrgAdmin Create(string? issuer, string? subject) => new()
    {
        Issuer = Rules.Required(issuer, "An admin's issuer", Rules.IdentityMaxLength),
        Subject = Rules.Required(subject, "An admin's subject", Rules.SubjectMaxLength)
    };
}
