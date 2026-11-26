namespace soft.square.net.Module.Organization.Tests;

/// <summary>
/// A small org: Acme (org) > News (sector) > Politics desk (department) > Local (section, categories local, cairo),
/// and News > Sports (department). The default unit types: org > sector > department > section; a section holds
/// categories.
/// </summary>
internal static class Samples
{
    public static readonly Guid OrgId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid Root = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid News = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid Politics = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public static readonly Guid Local = Guid.Parse("40000000-0000-0000-0000-000000000004");
    public static readonly Guid Sports = Guid.Parse("50000000-0000-0000-0000-000000000005");

    public static string P(params Guid[] ids) => string.Concat(ids.Select(i => "/" + i.ToString("N")));

    public static OrgUnitDto Unit(Guid id, Guid? parent, string type, string path, string en, string ar = "", params string[] categories) => new()
    {
        Id = id, OrgId = OrgId, ParentId = parent, TypeKey = type, Path = path, NameEn = en, NameAr = ar,
        Tags = [], Categories = categories, TenantBinding = new TenantBindingDto { Kind = TenantBindingKind.Inherit }, Version = 1,
    };

    public static IReadOnlyList<OrgUnitDto> Units() =>
    [
        // Out of order on purpose: the tree orders parents before children by the path's depth.
        Unit(Local, Politics, "section", P(Root, News, Politics, Local), "Local", "محلي", "local", "cairo"),
        Unit(Root, null, "org", P(Root), "Acme", "أكمي"),
        Unit(Sports, News, "department", P(Root, News, Sports), "Sports", "رياضة"),
        Unit(News, Root, "sector", P(Root, News), "News", "أخبار"),
        Unit(Politics, News, "department", P(Root, News, Politics), "Politics desk", "مكتب السياسة"),
    ];

    public static UnitTypeDto Type(string key, bool holds, int order, params string[] parents) => new()
    {
        Id = Guid.NewGuid(), OrgId = OrgId, Key = key, NameEn = key, NameAr = key, AllowedParents = parents, HoldsCategories = holds, Order = order,
    };

    public static IReadOnlyList<UnitTypeDto> Types() =>
    [
        Type("org", false, 0),
        Type("sector", false, 10, "org"),
        Type("department", false, 20, "sector"),
        Type("section", true, 30, "department"),
    ];

    public static UnitTree Tree() => UnitTree.Build(Units(), Types());
}
