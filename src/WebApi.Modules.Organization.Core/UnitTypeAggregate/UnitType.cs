namespace soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

/// <summary>
/// A kind of unit in one org, with the types it may sit under (<see cref="AllowedParents"/>, by key; none: a root type).
/// Every org starts with <see cref="Defaults"/>: Org &gt; Sector &gt; Department &gt; Section, where a Section holds post
/// categories (<see cref="HoldsCategories"/>).
/// </summary>
public sealed class UnitType : AggregateRoot<Guid>
{
    public const string OrgKey = "org";
    public const string SectorKey = "sector";
    public const string DepartmentKey = "department";
    public const string SectionKey = "section";

    private List<string> _allowedParents = [];

    private UnitType() { } // EF Core

    public Guid OrgId { get; private set; }
    public string Key { get; private set; } = "";
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public IReadOnlyList<string> AllowedParents => _allowedParents;

    /// <summary>Units of this type own a list of post categories (Content category ids), at least one.</summary>
    public bool HoldsCategories { get; private set; }

    /// <summary>Display order among the org's types.</summary>
    public int Order { get; private set; }

    public bool IsRoot => _allowedParents.Count == 0;

    /// <summary>
    /// A new type. <paramref name="existingKeys"/> are the org's type keys: every allowed parent must be one of them
    /// (or this type itself, for a type that nests).
    /// </summary>
    public static UnitType Create(Guid orgId, string key, string? nameEn, string? nameAr, IEnumerable<string>? allowedParents,
        bool holdsCategories, int order, IReadOnlyCollection<string> existingKeys)
    {
        var type = new UnitType { Id = Guid.CreateVersion7(), OrgId = orgId, Key = Rules.Key(key, "A unit type's key"), HoldsCategories = holdsCategories, Order = order };
        if (existingKeys.Contains(type.Key)) throw new DomainException($"The org already has a unit type '{type.Key}'.");
        (type.NameEn, type.NameAr) = Rules.Names(nameEn, nameAr, "A unit type");
        type._allowedParents = Rules.List(allowedParents, "Allowed parents", lowercase: true);
        var unknown = type._allowedParents.FirstOrDefault(p => p != type.Key && !existingKeys.Contains(p));
        if (unknown is not null) throw new DomainException($"The org has no unit type '{unknown}'.");
        if (type.IsRoot && existingKeys.Count > 0) throw new DomainException("An org has one root unit type; name the types this one may sit under.");
        return type;
    }

    /// <summary>True when a unit of this type may sit under a unit of <paramref name="parentType"/>.</summary>
    public bool AllowsUnder(UnitType parentType) =>
        parentType.OrgId == OrgId && _allowedParents.Contains(parentType.Key, StringComparer.Ordinal);

    /// <summary>The default types of a new org: Org (root) &gt; Sector &gt; Department &gt; Section (holds categories).</summary>
    public static IReadOnlyList<UnitType> Defaults(Guid orgId) =>
    [
        Seeded(orgId, OrgKey, "Organization", "المؤسسة", [], false, 0),
        Seeded(orgId, SectorKey, "Sector", "القطاع", [OrgKey], false, 1),
        Seeded(orgId, DepartmentKey, "Department", "الإدارة", [SectorKey], false, 2),
        Seeded(orgId, SectionKey, "Section", "القسم", [DepartmentKey], true, 3)
    ];

    private static UnitType Seeded(Guid orgId, string key, string en, string ar, List<string> parents, bool categories, int order) =>
        new() { Id = Guid.CreateVersion7(), OrgId = orgId, Key = key, NameEn = en, NameAr = ar, _allowedParents = parents, HoldsCategories = categories, Order = order };
}
