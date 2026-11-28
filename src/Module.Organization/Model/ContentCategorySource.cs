using System.Net;
using System.Text.Json;
using soft.square.net.ModularBlazor.Abstractions.Lookups;

namespace soft.square.net.Module.Organization.Model;

/// <summary>
/// What the picker loaded. <see cref="Truncated"/>: Content has more categories than the picker reads.
/// <see cref="SelectionChecked"/>: a saved id that is not in <see cref="Items"/> is one Content does not list (false only
/// when a truncated list's saved ids could not be looked up, so nothing may be said about them).
/// </summary>
public sealed record ContentCategoryList(IReadOnlyList<ContentCategory> Items, bool Truncated = false, bool SelectionChecked = true);

/// <summary>A readable error: an English text (a translation key) and its arguments.</summary>
public sealed record CategoryError(string Text, params object[] Args);

/// <summary>
/// Reads Content's categories for the picker: the lookup <see cref="ContentCategories.Lookup"/> page by page, else
/// Content's REST list (which is not paged).
/// </summary>
public static class ContentCategorySource
{
    /// <summary>The largest page the platform's lookups serve (they refuse more with a 400).</summary>
    public const int PageSize = 100;

    /// <summary>The most pages read: 5000 categories.</summary>
    public const int MaxPages = 50;

    /// <summary>The most keys one resolve request takes.</summary>
    private const int ResolveSize = 100;

    /// <param name="lookups">The host's lookups; null when it offers none.</param>
    /// <param name="gateway">The gateway client, for the REST list.</param>
    /// <param name="selected">The saved ids, so their names show even when the list is cut short.</param>
    public static async Task<ContentCategoryList> LoadAsync(ILookupService? lookups, Func<HttpClient> gateway,
        IReadOnlyCollection<string> selected, CancellationToken ct = default)
    {
        if (lookups is not null && await lookups.FindAsync(ContentCategories.Lookup, ct) is not null)
            return await FromLookupAsync(lookups, selected, ct);

        using var response = await gateway().GetAsync(ContentCategories.Url, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        return new ContentCategoryList(ContentCategories.Parse(await response.Content.ReadAsStringAsync(ct)));
    }

    private static async Task<ContentCategoryList> FromLookupAsync(ILookupService lookups, IReadOnlyCollection<string> selected, CancellationToken ct)
    {
        var items = new List<ContentCategory>();
        var more = true;
        for (var page = 0; more && page < MaxPages; page++)
        {
            var result = await lookups.SearchAsync(ContentCategories.Lookup, new LookupQuery { Skip = items.Count, Take = PageSize }, ct);
            items.AddRange(result.Items.Select(ToCategory));
            more = result.HasMore && result.Items.Count > 0;
        }
        if (!more) return new ContentCategoryList(ContentCategories.Sort(items));

        var checkedAll = true;
        var missing = ContentCategories.Unknown(selected.Where(id => id != ContentCategories.All), items);
        try
        {
            foreach (var keys in missing.Chunk(ResolveSize))
                items.AddRange((await lookups.ResolveAsync(ContentCategories.Lookup, keys, ct: ct)).Select(ToCategory));
        }
        catch (Exception e) when (e is HttpRequestException or ArgumentException or InvalidOperationException or JsonException)
        {
            checkedAll = false; // the list is still usable: those ids show without a name
        }
        return new ContentCategoryList(ContentCategories.Sort(items), Truncated: true, SelectionChecked: checkedAll);
    }

    private static ContentCategory ToCategory(LookupItem item) =>
        new(item.Key, item.Text, item.ParentKey ?? item.Extra?.GetValueOrDefault("pressId"));

    /// <summary>
    /// What to show when loading failed. HTTP errors are told by their status: in the browser the exception's own message
    /// can be a bare resource name ("net_http_message_not_success_statuscode_reason, 403, Forbidden").
    /// </summary>
    public static CategoryError Describe(Exception error) => error switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => new(
            "The sign-in was not accepted when reading the Content categories (401). Sign in again, then try again."),
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => new(
            "You can't read Content's categories here (403). They belong to a tenant: sign in to a tenant you have access to, or choose All categories, or type category ids."),
        HttpRequestException { StatusCode: { } status } => new(
            "The Content categories could not be read: the server answered {0}. Type category ids instead, or try again.", (int)status),
        HttpRequestException or OperationCanceledException => new(
            "The Content categories could not be read: the server can't be reached. Type category ids instead, or try again."),
        JsonException or NotSupportedException => new(
            "The Content categories could not be read: the server's answer was not understood. Type category ids instead, or try again."),
        _ => new("The Content categories could not be read ({0}). Type category ids instead, or try again.", error.Message),
    };
}
