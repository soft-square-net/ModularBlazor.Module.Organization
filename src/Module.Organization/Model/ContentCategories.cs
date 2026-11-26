using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft.square.net.Module.Organization.Model;

/// <summary>A Content post category as the picker shows it: its id (what a Section stores), name and press.</summary>
public sealed record ContentCategory(string Id, string Name, string? PressId);

/// <summary>
/// Where the Section editor gets post categories from, without a reference to the Content module: its lookup
/// <c>content.categories</c> when the lookup catalog has one, else its REST list <c>GET api/content/categories</c>
/// (CategoryDto: id, pressId, name) through the gateway, in the user's current tenant.
/// </summary>
public static class ContentCategories
{
    public const string Lookup = "content.categories";

    /// <summary>
    /// The wildcard a Section stores to serve every category (Multimedia, SEO): <c>["*"]</c>, never with other ids
    /// (<c>UnitCategories.All</c> in Organization.Contracts).
    /// </summary>
    public const string All = "*";

    /// <summary>The selection is the wildcard.</summary>
    public static bool IsAll(IEnumerable<string> selected) => selected.Contains(All, StringComparer.Ordinal);
    public const string Url = "api/content/categories";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>The categories from Content's list endpoint, by press then name.</summary>
    public static IReadOnlyList<ContentCategory> Parse(string json) =>
        Sort((JsonSerializer.Deserialize<List<Wire>>(json, Web) ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .Select(c => new ContentCategory(c.Id!, string.IsNullOrWhiteSpace(c.Name) ? c.Id! : c.Name!, c.PressId)));

    public static IReadOnlyList<ContentCategory> Sort(IEnumerable<ContentCategory> categories) =>
        [.. categories.DistinctBy(c => c.Id).OrderBy(c => c.PressId ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The saved ids that the list does not have (removed in Content, or another tenant's).</summary>
    public static IReadOnlyList<string> Unknown(IEnumerable<string> selected, IEnumerable<ContentCategory> known)
    {
        var ids = known.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        return [.. selected.Where(id => !ids.Contains(id)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Toggles one id in the selection (kept in the order picked). The wildcard replaces the selection, and picking a
    /// category replaces the wildcard: <c>*</c> always stands alone.
    /// </summary>
    public static List<string> Toggle(IReadOnlyList<string> selected, string id)
    {
        if (selected.Contains(id, StringComparer.Ordinal)) return [.. selected.Where(s => s != id)];
        if (id == All) return [All];
        return [.. selected.Where(s => s != All), id];
    }

    private sealed class Wire
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("pressId")] public string? PressId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
