namespace soft.square.net.Organization.Contracts;

/// <summary>
/// The post categories of a unit (Content category ids, see <see cref="OrgUnitDto.Categories"/>,
/// <see cref="OrgUnitSnapshot.Categories"/> and <see cref="IUnitDirectory.CategoriesOfAsync"/>), and the wildcard
/// <see cref="All"/>: a Section that serves every category (Multimedia, SEO) holds <c>["*"]</c>, alone. Consumers that
/// check a post's category against a unit's categories (the Elsa check <c>EnsurePostInUnitCategories</c>, for one)
/// treat <c>*</c> as a match for any category: use <see cref="Covers"/>.
/// </summary>
public static class UnitCategories
{
    /// <summary>The wildcard: every category. Only a Section holds it, and never with other ids.</summary>
    public const string All = "*";

    /// <summary>The list is the wildcard (<c>["*"]</c>).</summary>
    public static bool IsAll(IEnumerable<string> categories) => categories.Contains(All, StringComparer.Ordinal);

    /// <summary>The unit's categories include <paramref name="categoryId"/>: it is listed, or the list is the wildcard.</summary>
    public static bool Covers(IEnumerable<string> categories, string categoryId)
    {
        var list = categories as IReadOnlyCollection<string> ?? [.. categories];
        return list.Contains(All, StringComparer.Ordinal) || list.Contains(categoryId, StringComparer.Ordinal);
    }

    /// <summary>
    /// The categories of several units (a parent's Sections), each once, in order; <c>["*"]</c> when any of them holds
    /// the wildcard.
    /// </summary>
    public static IReadOnlyList<string> Union(IEnumerable<IEnumerable<string>> lists)
    {
        var ids = lists.SelectMany(l => l).Distinct(StringComparer.Ordinal).ToList();
        return ids.Contains(All, StringComparer.Ordinal) ? [All] : ids;
    }
}
