using soft.square.net.WebApi.Modules.Organization.Client;

namespace soft.square.net.Module.Organization.Model;

/// <summary>
/// The unit editor's form: a new unit under a parent, or an existing unit. Categories belong to units whose type holds
/// them (Section): at least one there, none anywhere else (the API refuses both, so the form says so first).
/// </summary>
public sealed class UnitForm
{
    /// <summary>Null for a new unit.</summary>
    public Guid? Id { get; init; }

    public int Version { get; init; }

    /// <summary>A new unit's parent (null when editing).</summary>
    public Guid? ParentId { get; init; }

    public string TypeKey { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string NameAr { get; set; } = "";

    /// <summary>Comma-separated.</summary>
    public string Tags { get; set; } = "";

    public string TimeZone { get; set; } = "";

    /// <summary>Content post category ids, in the order picked.</summary>
    public List<string> Categories { get; set; } = [];

    public bool IsNew => Id is null;

    public static UnitForm New(OrgUnitDto parent, string typeKey) => new() { ParentId = parent.Id, TypeKey = typeKey };

    public static UnitForm Edit(OrgUnitDto unit) => new()
    {
        Id = unit.Id,
        Version = unit.Version,
        TypeKey = unit.TypeKey,
        NameEn = unit.NameEn,
        NameAr = unit.NameAr,
        Tags = string.Join(", ", unit.Tags),
        TimeZone = unit.TimeZone ?? "",
        Categories = [.. unit.Categories],
    };

    /// <summary>The problems to fix before saving, as English texts (keys of the module's texts); empty when it may be saved.</summary>
    public IReadOnlyList<string> Validate(UnitTypeDto? type)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(TypeKey)) problems.Add("Choose the unit's type.");
        if (string.IsNullOrWhiteSpace(NameEn)) problems.Add("Enter the English name.");
        if (string.IsNullOrWhiteSpace(NameAr)) problems.Add("Enter the Arabic name.");
        var holds = type?.HoldsCategories ?? false;
        if (holds && Clean(Categories).Count == 0) problems.Add("A section needs at least one post category.");
        if (!holds && Clean(Categories).Count > 0) problems.Add("Only sections hold post categories.");
        if (ContentCategories.IsAll(Clean(Categories)) && Clean(Categories).Count > 1)
            problems.Add("All categories can't be combined with other categories.");
        return problems;
    }

    public CreateUnitRequest ToCreate(UnitTypeDto? type) => new()
    {
        ParentId = ParentId ?? throw new InvalidOperationException("A new unit needs a parent."),
        TypeKey = TypeKey,
        NameEn = NameEn.Trim(),
        NameAr = NameAr.Trim(),
        Tags = Lists.Split(Tags),
        TimeZone = string.IsNullOrWhiteSpace(TimeZone) ? null : TimeZone.Trim(),
        Categories = type?.HoldsCategories == true ? Clean(Categories) : null,
    };

    public UpdateUnitRequest ToUpdate(UnitTypeDto? type) => new()
    {
        Version = Version,
        NameEn = NameEn.Trim(),
        NameAr = NameAr.Trim(),
        Tags = Lists.Split(Tags),
        TimeZone = string.IsNullOrWhiteSpace(TimeZone) ? null : TimeZone.Trim(),
        Categories = type?.HoldsCategories == true ? Clean(Categories) : [],
    };

    private static List<string> Clean(IEnumerable<string> ids) =>
        [.. ids.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal)];
}

/// <summary>Comma-separated lists in forms (tags, hosts, modules).</summary>
public static class Lists
{
    public static List<string> Split(string? text) =>
        [.. (text ?? "").Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];

    public static string Join(IEnumerable<string>? items) => string.Join(", ", items ?? []);
}

/// <summary>A name in the user's language: Arabic for an Arabic UI (when it has one), English otherwise.</summary>
public static class Names
{
    public static string Pick(string? en, string? ar, string culture) =>
        culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(ar) ? ar! : en ?? ar ?? "";
}
