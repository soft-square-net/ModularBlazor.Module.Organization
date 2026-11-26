using soft.square.net.WebApi.Modules.Organization.Core.UnitTypeAggregate;

namespace soft.square.net.WebApi.Modules.Organization.Core.OrgUnitAggregate;

/// <summary>
/// One unit of an org's tree.
/// <list type="bullet">
/// <item><see cref="Path"/> is materialized: the ids from the root to this unit (<see cref="UnitPaths"/>), so a subtree is
/// a path prefix and the grant scopes of a unit are <c>UnitScopes.For(Path)</c>.</item>
/// <item>Its type must be allowed under its parent's type.</item>
/// <item>A unit whose type holds categories (a Section) owns at least one post category (Content category ids); other
/// units own none.</item>
/// <item>It either owns a tenant (<see cref="TenantBindingKind.OwnTenant"/>, through a tenant seed) or inherits the tenant
/// of its nearest ancestor that owns one (<see cref="UnitTenancy"/>).</item>
/// </list>
/// </summary>
public sealed class OrgUnit : AggregateRoot<Guid>
{
    public const int PathMaxLength = 1024;

    private List<string> _tags = [];
    private readonly List<UnitCategory> _categories = [];

    private OrgUnit() { } // EF Core

    public Guid OrgId { get; private set; }
    public Guid TypeId { get; private set; }
    public string TypeKey { get; private set; } = "";
    public Guid? ParentId { get; private set; }
    public string Path { get; private set; } = "";
    public int Depth { get; private set; }
    public string NameEn { get; private set; } = "";
    public string NameAr { get; private set; } = "";
    public IReadOnlyList<string> Tags => _tags;
    public string? TimeZone { get; private set; }
    public IReadOnlyList<UnitCategory> Categories => _categories;
    public TenantBindingKind Binding { get; private set; }
    public string? SeedId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Bumped by every change of this unit (a move of an ancestor too); a concurrency token.</summary>
    public int Version { get; private set; }

    public IReadOnlyList<string> CategoryIds => [.. _categories.Select(c => c.CategoryId)];

    /// <summary>The org's root unit, of the root type.</summary>
    public static OrgUnit CreateRoot(Guid id, Guid orgId, UnitType type, string? nameEn, string? nameAr, DateTimeOffset now)
    {
        if (type.OrgId != orgId) throw new DomainException("The unit type belongs to another org.");
        if (!type.IsRoot) throw new DomainException($"A '{type.Key}' unit cannot be a root unit.");
        var unit = New(id, orgId, type, now);
        unit.Path = UnitPaths.Root(id);
        unit.Depth = 0;
        unit.SetDetails(type, nameEn, nameAr, null, null, null);
        return unit;
    }

    /// <summary>A unit under <paramref name="parent"/>: its type must be allowed under the parent's type.</summary>
    public static OrgUnit CreateChild(OrgUnit parent, UnitType parentType, UnitType type, string? nameEn, string? nameAr,
        IEnumerable<string>? tags, string? timeZone, IEnumerable<string>? categories, DateTimeOffset now)
    {
        if (parentType.Id != parent.TypeId) throw new DomainException("That is not the parent unit's type.");
        if (type.OrgId != parent.OrgId) throw new DomainException("The unit type belongs to another org.");
        EnsureAllowedUnder(type, parentType);
        var id = Guid.CreateVersion7();
        var unit = New(id, parent.OrgId, type, now);
        unit.ParentId = parent.Id;
        unit.Path = ChildPath(parent.Path, id);
        unit.Depth = parent.Depth + 1;
        unit.SetDetails(type, nameEn, nameAr, tags, timeZone, categories);
        return unit;
    }

    /// <summary>Changes names, tags, time zone and categories (<paramref name="type"/> is this unit's type).</summary>
    public void Update(UnitType type, string? nameEn, string? nameAr, IEnumerable<string>? tags, string? timeZone, IEnumerable<string>? categories, DateTimeOffset now)
    {
        if (type.Id != TypeId) throw new DomainException("That is not this unit's type.");
        SetDetails(type, nameEn, nameAr, tags, timeZone, categories);
        Touch(now);
    }

    /// <summary>
    /// Moves this unit, with its subtree, under <paramref name="newParent"/>, and rewrites the path of every unit of the
    /// subtree. <paramref name="subtree"/> is every unit below this one (any order). Returns the old path.
    /// </summary>
    public string MoveUnder(OrgUnit newParent, UnitType newParentType, UnitType ownType, IReadOnlyCollection<OrgUnit> subtree, DateTimeOffset now)
    {
        if (ParentId is null) throw new DomainException("The root unit cannot be moved.");
        if (newParent.OrgId != OrgId) throw new DomainException("A unit can only move inside its org.");
        if (newParentType.Id != newParent.TypeId || ownType.Id != TypeId) throw new DomainException("Those are not the units' types.");
        if (UnitPaths.IsSelfOrBelow(newParent.Path, Path)) throw new DomainException("A unit cannot move under itself or one of its descendants.");
        EnsureAllowedUnder(ownType, newParentType);
        if (subtree.Any(u => u.OrgId != OrgId || !UnitPaths.IsSelfOrBelow(u.Path, Path) || u.Id == Id))
            throw new DomainException("The subtree holds a unit that is not below the moved unit.");

        var oldPath = Path;
        var newPath = ChildPath(newParent.Path, Id);
        var depthChange = newParent.Depth + 1 - Depth;
        if (subtree.Any(u => UnitPaths.Rebase(u.Path, oldPath, newPath).Length > PathMaxLength))
            throw new DomainException("The tree would be too deep there.");

        ParentId = newParent.Id;
        Path = newPath;
        Depth += depthChange;
        Touch(now);
        foreach (var unit in subtree)
        {
            unit.Path = UnitPaths.Rebase(unit.Path, oldPath, newPath);
            unit.Depth += depthChange;
            unit.Touch(now);
        }
        return oldPath;
    }

    /// <summary>This unit owns the tenant of the seed <paramref name="seedId"/>.</summary>
    public void BindOwnTenant(string seedId, DateTimeOffset now)
    {
        if (Binding == TenantBindingKind.OwnTenant && SeedId != seedId) throw new DomainException("The unit already owns another tenant.");
        Binding = TenantBindingKind.OwnTenant;
        SeedId = Rules.Required(seedId, "A seed id", Rules.KeyMaxLength * 2);
        Touch(now);
    }

    /// <summary>This unit lives in the tenant of its nearest ancestor that owns one.</summary>
    public void InheritTenant(DateTimeOffset now)
    {
        Binding = TenantBindingKind.Inherit;
        SeedId = null;
        Touch(now);
    }

    private static OrgUnit New(Guid id, Guid orgId, UnitType type, DateTimeOffset now) =>
        new() { Id = id, OrgId = orgId, TypeId = type.Id, TypeKey = type.Key, Binding = TenantBindingKind.Inherit, CreatedAt = now, UpdatedAt = now };

    private static void EnsureAllowedUnder(UnitType type, UnitType parentType)
    {
        if (!type.AllowsUnder(parentType)) throw new DomainException($"A '{type.Key}' unit cannot sit under a '{parentType.Key}' unit.");
    }

    private static string ChildPath(string parentPath, Guid id)
    {
        var path = UnitPaths.Child(parentPath, id);
        if (path.Length > PathMaxLength) throw new DomainException("The tree is too deep there.");
        return path;
    }

    private void SetDetails(UnitType type, string? nameEn, string? nameAr, IEnumerable<string>? tags, string? timeZone, IEnumerable<string>? categories)
    {
        (NameEn, NameAr) = Rules.Names(nameEn, nameAr, "A unit");
        _tags = Rules.List(tags, "Tags", lowercase: true);
        TimeZone = Rules.TimeZone(timeZone);

        var categoryIds = Rules.List(categories, "Categories");
        if (type.HoldsCategories && categoryIds.Count == 0)
            throw new DomainException($"A '{type.Key}' unit needs at least one post category.");
        if (!type.HoldsCategories && categoryIds.Count > 0)
            throw new DomainException($"A '{type.Key}' unit does not hold post categories.");
        _categories.RemoveAll(c => !categoryIds.Contains(c.CategoryId));
        foreach (var id in categoryIds.Where(id => _categories.All(c => c.CategoryId != id)))
            _categories.Add(new UnitCategory(id));
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}

/// <summary>A post category a unit owns: the id of a Content category (the category itself stays in Content).</summary>
public sealed class UnitCategory
{
    private UnitCategory() { } // EF Core

    public UnitCategory(string categoryId) => CategoryId = categoryId;

    public string CategoryId { get; private set; } = "";
}

/// <summary>Which tenant a unit lives in.</summary>
public static class UnitTenancy
{
    /// <summary>
    /// The unit that owns <paramref name="unit"/>'s tenant: the unit itself when it owns one, otherwise its nearest
    /// ancestor that owns one; null when no unit on its path owns a tenant. <paramref name="ancestors"/> are the units on
    /// its path above it (any order; missing ones are skipped).
    /// </summary>
    public static OrgUnit? Owner(OrgUnit unit, IEnumerable<OrgUnit> ancestors)
    {
        if (unit.Binding == TenantBindingKind.OwnTenant) return unit;
        var byId = ancestors.ToDictionary(a => UnitPaths.Id(a.Id));
        return UnitPaths.Ids(unit.Path).Reverse().Skip(1)
            .Select(id => byId.GetValueOrDefault(id))
            .FirstOrDefault(a => a is { Binding: TenantBindingKind.OwnTenant });
    }

    /// <summary>The ids of the units above <paramref name="unit"/>, root first.</summary>
    public static IReadOnlyList<Guid> AncestorIds(OrgUnit unit) =>
        [.. UnitPaths.Ids(unit.Path).SkipLast(1).Select(id => Guid.ParseExact(id, "N"))];
}
