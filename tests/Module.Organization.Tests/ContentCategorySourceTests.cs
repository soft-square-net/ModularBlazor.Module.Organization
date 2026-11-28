using System.Net;
using System.Text;
using System.Text.Json;
using soft.square.net.ModularBlazor.Abstractions.Lookups;

namespace soft.square.net.Module.Organization.Tests;

/// <summary>
/// How the picker reads Content's categories: the lookup in pages of at most 100, saved names beyond the pages it reads,
/// and a readable error for every way a read can fail.
/// </summary>
public class ContentCategorySourceTests
{
    private static HttpClient NoRest() => throw new InvalidOperationException("The REST list was not expected.");

    /// <summary>A lookup like the platform's: it refuses a take outside 1 to 100.</summary>
    private static ILookupService Lookups(int count, List<LookupQuery>? asked = null)
    {
        var lookups = Substitute.For<ILookupService>();
        lookups.FindAsync(ContentCategories.Lookup, Arg.Any<CancellationToken>()).Returns(Descriptor());
        lookups.SearchAsync(ContentCategories.Lookup, Arg.Any<LookupQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var query = call.Arg<LookupQuery>();
            asked?.Add(query);
            if (query.Take is < 1 or > 100) throw new ArgumentException("take is 1 to 100.");
            var items = Enumerable.Range(0, count).Skip(query.Skip).Take(query.Take).Select(Item).ToList();
            return new LookupPage(items, query.Skip + items.Count < count);
        });
        lookups.ResolveAsync(ContentCategories.Lookup, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyList<LookupFilter>?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IReadOnlyCollection<string>>()
                .Select(k => int.TryParse(k.AsSpan(1), out var n) && n < count ? Item(n) : null).OfType<LookupItem>().ToList());
        return lookups;
    }

    private static LookupItem Item(int n) => new($"c{n}", $"Category {n:00000}") { ParentKey = "daily" };

    private static LookupDescriptor Descriptor() =>
        new(ContentCategories.Lookup, "content", "categories", "Categories", null, null, [], [], [], "PressId", "dependent", "/api/content/lookups/categories");

    [Theory]
    [InlineData(0, 1)]
    [InlineData(100, 1)]
    [InlineData(101, 2)]
    [InlineData(250, 3)]
    public async Task The_lookup_is_read_in_pages_of_at_most_100(int count, int requests)
    {
        var asked = new List<LookupQuery>();

        var list = await ContentCategorySource.LoadAsync(Lookups(count, asked), NoRest, [], TestContext.Current.CancellationToken);

        list.Items.Count.ShouldBe(count);
        list.Truncated.ShouldBeFalse();
        asked.Count.ShouldBe(requests);
        asked.ShouldAllBe(q => q.Take == 100);
        asked.Select(q => q.Skip).ShouldBe(Enumerable.Range(0, requests).Select(i => i * 100));
    }

    [Fact]
    public async Task Saved_categories_beyond_the_pages_read_are_looked_up_by_key()
    {
        var list = await ContentCategorySource.LoadAsync(Lookups(6000), NoRest, ["c3", "c5500", "gone"], TestContext.Current.CancellationToken);

        list.Truncated.ShouldBeTrue();
        list.SelectionChecked.ShouldBeTrue();
        list.Items.Count.ShouldBe(ContentCategorySource.PageSize * ContentCategorySource.MaxPages + 1);
        list.Items.Single(c => c.Id == "c5500").Name.ShouldBe("Category 05500");
        ContentCategories.Unknown(["c3", "c5500", "gone"], list.Items).ShouldBe(["gone"]);
    }

    [Fact]
    public async Task A_failed_key_lookup_keeps_the_list_and_calls_nothing_unknown()
    {
        var lookups = Lookups(6000);
        lookups.ResolveAsync(ContentCategories.Lookup, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<IReadOnlyList<LookupFilter>?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<LookupItem>>(_ => throw new ArgumentException("At most 200 keys."));

        var list = await ContentCategorySource.LoadAsync(lookups, NoRest, ["c5500"], TestContext.Current.CancellationToken);

        list.Items.Count.ShouldBe(5000);
        list.SelectionChecked.ShouldBeFalse();
    }

    [Fact]
    public async Task A_page_that_is_empty_but_claims_more_ends_the_reading()
    {
        var lookups = Lookups(0);
        lookups.SearchAsync(ContentCategories.Lookup, Arg.Any<LookupQuery>(), Arg.Any<CancellationToken>()).Returns(new LookupPage([], true));

        var list = await ContentCategorySource.LoadAsync(lookups, NoRest, [], TestContext.Current.CancellationToken);

        list.Items.ShouldBeEmpty();
        await lookups.Received(1).SearchAsync(ContentCategories.Lookup, Arg.Any<LookupQuery>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_the_lookup_the_rest_list_is_read(bool hostHasLookups)
    {
        var lookups = hostHasLookups ? Substitute.For<ILookupService>() : null; // FindAsync gives null

        var list = await ContentCategorySource.LoadAsync(lookups,
            Rest(HttpStatusCode.OK, """[ { "id": "local", "pressId": "daily", "name": "Local" } ]"""), [], TestContext.Current.CancellationToken);

        list.Items.ShouldBe([new ContentCategory("local", "Local", "daily")]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "(401)")]
    [InlineData(HttpStatusCode.Forbidden, "(403)")]
    [InlineData(HttpStatusCode.BadRequest, "the server answered {0}")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "the server answered {0}")]
    public async Task A_refused_rest_list_is_told_by_its_status(HttpStatusCode status, string text)
    {
        var error = await FailureOf(null, Rest(status, ""));

        error.Text.ShouldContain(text);
        if (text.Contains("{0}")) error.Args.ShouldBe([(int)status]);
        else error.Args.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_403_from_the_lookup_catalog_does_not_show_the_exceptions_raw_message()
    {
        var lookups = Substitute.For<ILookupService>();
        lookups.FindAsync(ContentCategories.Lookup, Arg.Any<CancellationToken>()).Returns<LookupDescriptor?>(_ =>
            throw new HttpRequestException("net_http_message_not_success_statuscode_reason, 403, Forbidden", null, HttpStatusCode.Forbidden));

        var error = await FailureOf(lookups, NoRest);

        error.Text.ShouldStartWith("You can't read Content's categories here (403).");
        error.Args.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_lookup_400_shows_the_servers_reason()
    {
        // The platform's LookupService turns a 400 into an ArgumentException with the problem's detail.
        var error = await FailureOf(Failing(new ArgumentException("take is 1 to 100.")), NoRest);

        error.Text.ShouldContain("({0})");
        error.Args.ShouldBe(["take is 1 to 100."]);
    }

    [Fact]
    public async Task A_missing_list_shows_the_services_message()
    {
        var error = await FailureOf(Failing(new InvalidOperationException("The list 'content.categories' is not available.")), NoRest);

        error.Args.ShouldBe(["The list 'content.categories' is not available."]);
    }

    [Fact]
    public async Task An_unreachable_server_or_a_timeout_says_so()
    {
        foreach (var failure in new Exception[] { new HttpRequestException("TypeError: Failed to fetch"), new TaskCanceledException() })
        {
            (await FailureOf(Failing(failure), NoRest)).Text.ShouldContain("can't be reached");
            (await FailureOf(null, () => new HttpClient(new Handler(_ => throw failure)) { BaseAddress = new Uri("http://localhost/") }))
                .Text.ShouldContain("can't be reached");
        }
    }

    [Fact]
    public async Task An_answer_that_is_not_json_says_so()
    {
        var error = await FailureOf(null, Rest(HttpStatusCode.OK, "<html>"));

        error.Text.ShouldContain("was not understood");
    }

    [Fact]
    public void Every_error_text_has_an_arabic_translation_with_the_same_placeholders()
    {
        using var stream = typeof(OrganizationPageBase).Assembly.GetManifestResourceStream("i18n/ar.json")!;
        var arabic = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        Exception[] failures =
        [
            new HttpRequestException("", null, HttpStatusCode.Unauthorized), new HttpRequestException("", null, HttpStatusCode.Forbidden),
            new HttpRequestException("", null, HttpStatusCode.BadRequest), new HttpRequestException(""), new JsonException(), new ArgumentException("x"),
        ];

        foreach (var error in failures.Select(ContentCategorySource.Describe))
        {
            arabic.ShouldContainKey(error.Text);
            arabic[error.Text].Contains("{0}").ShouldBe(error.Text.Contains("{0}"), error.Text);
        }
    }

    private static ILookupService Failing(Exception failure)
    {
        var lookups = Lookups(1);
        lookups.SearchAsync(ContentCategories.Lookup, Arg.Any<LookupQuery>(), Arg.Any<CancellationToken>()).Returns<LookupPage>(_ => throw failure);
        return lookups;
    }

    private static async Task<CategoryError> FailureOf(ILookupService? lookups, Func<HttpClient> gateway)
    {
        try
        {
            await ContentCategorySource.LoadAsync(lookups, gateway, [], TestContext.Current.CancellationToken);
        }
        catch (Exception e)
        {
            return ContentCategorySource.Describe(e);
        }
        throw new InvalidOperationException("The read did not fail.");
    }

    private static Func<HttpClient> Rest(HttpStatusCode status, string body) => () => new HttpClient(new Handler(request =>
    {
        request.RequestUri!.AbsolutePath.ShouldBe("/api/content/categories");
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    })) { BaseAddress = new Uri("http://localhost/") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
