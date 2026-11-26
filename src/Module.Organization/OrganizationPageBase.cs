using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using soft.square.net.Module.Organization.Model;
using soft.square.net.ModularBlazor.Abstractions;
using soft.square.net.ModularBlazor.Abstractions.Localization;
using soft.square.net.ModularBlazor.Abstractions.MudTail;
using soft.square.net.WebApi.Modules.Organization.Client;

namespace soft.square.net.Module.Organization;

/// <summary>
/// What every page of this module shares: the central service's client (the gateway, or <c>Organization:BaseUrl</c>),
/// the module's texts, the signed-in user as the central service knows them, one-call-at-a-time state and readable
/// error messages.
/// </summary>
public abstract class OrganizationPageBase : ComponentBase, IDisposable
{
    [Inject] protected IHttpClientFactory HttpClients { get; set; } = default!;
    [Inject] protected MudTailRuntime MudTail { get; set; } = default!;
    [Inject] protected NavigationManager Nav { get; set; } = default!;
    [Inject] protected IStringLocalizerFactory Texts { get; set; } = default!;
    [Inject] protected ILocaleService Locale { get; set; } = default!;
    [Inject] protected IConfiguration Configuration { get; set; } = default!;
    [Inject] protected AuthenticationStateProvider Auth { get; set; } = default!;
    [Inject] protected IAccessTokenProvider Tokens { get; set; } = default!;

    private IStringLocalizer? _l;
    private OrganizationClient? _api;
    private HttpClient? _remote;
    private OrganizationEndpoint? _endpoint;

    /// <summary>This module's texts (its embedded i18n/{culture}.json); the keys are the English texts.</summary>
    protected IStringLocalizer L => _l ??= Texts.Create(typeof(OrganizationPageBase));

    /// <summary>Where the central service is (<see cref="OrganizationEndpoint"/>).</summary>
    protected OrganizationEndpoint Endpoint => _endpoint ??= OrganizationEndpoint.From(Configuration[OrganizationEndpoint.SettingKey]);

    /// <summary>The central service's management API (/api/organization/...).</summary>
    protected OrganizationClient Api => _api ??= new OrganizationClient(OrganizationHttp());

    /// <summary>The gateway with the access token, for the platform's and other modules' endpoints (catalog, categories).</summary>
    protected HttpClient Gateway => HttpClients.CreateClient(HttpClientNames.WebApi);

    protected bool Busy { get; private set; }
    protected string? Error { get; set; }

    /// <summary>The signed-in user as the central service knows them (token issuer and subject, platform-admin role).</summary>
    protected OrgCaller Caller { get; private set; } = OrgCaller.Anonymous;

    protected bool IsRtl => Locale.IsRightToLeft;

    /// <summary>A name in the UI's language.</summary>
    protected string Name(string? en, string? ar) => Names.Pick(en, ar, Locale.Culture);

    protected override async Task OnInitializedAsync()
    {
        Locale.Changed += OnLocaleChanged;
        var user = (await Auth.GetAuthenticationStateAsync()).User;
        Caller = CallerOf(user, Configuration["Oidc:Authority"]);
    }

    /// <summary>
    /// The caller from the sign-in's claims: <c>sub</c>, <c>iss</c> (else the configured authority, which issued the
    /// token) and the <c>role</c> platform-admin.
    /// </summary>
    public static OrgCaller CallerOf(ClaimsPrincipal user, string? authority)
    {
        if (user.Identity?.IsAuthenticated != true) return OrgCaller.Anonymous;
        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var issuer = user.FindFirst("iss")?.Value ?? (string.IsNullOrWhiteSpace(authority) ? null : authority);
        var admin = user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role)).Any(c => c.Value == "platform-admin");
        return new OrgCaller(issuer, subject, admin);
    }

    private HttpClient OrganizationHttp()
    {
        if (Endpoint.BaseAddress is not { } baseAddress) return Gateway;
        // A central service in another deployment: the same access token, on a client of our own (the host's "WebApi"
        // client only gives the token to the gateway's origin).
        return _remote ??= new HttpClient(new BearerTokenHandler(Tokens, Nav) { InnerHandler = new HttpClientHandler() }) { BaseAddress = baseAddress };
    }

    private void OnLocaleChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Runs one API call: busy flag, error message and toast, and <paramref name="then"/> when it succeeded.</summary>
    protected async Task<bool> RunAsync(Func<Task> call, Func<Task>? then = null, bool toast = true)
    {
        Busy = true;
        Error = null;
        StateHasChanged();
        try
        {
            await call();
        }
        catch (AccessTokenNotAvailableException ex)
        {
            ex.Redirect(); // the session ended: sign in again and come back
            return false;
        }
        catch (Exception ex)
        {
            Error = Describe(ex);
            if (toast) await MudTail.ToastAsync(Error, "error");
            return false;
        }
        finally
        {
            Busy = false;
            StateHasChanged();
        }

        if (then is not null) await then();
        StateHasChanged();
        return true;
    }

    protected string Describe(Exception ex) => ex switch
    {
        OrganizationApiException { StatusCode: 401 } => L["401: the central service did not accept the access token. Sign in again."],
        OrganizationApiException { StatusCode: 403 } api =>
            $"403: {ProblemDetail(api) ?? L["you may not do this in this organization."]}",
        OrganizationApiException { StatusCode: 404 } api =>
            $"404: {ProblemDetail(api) ?? L["not found (is the central Organization service running here?)."]}",
        OrganizationApiException { StatusCode: 409 } api => $"409: {ProblemDetail(api) ?? L["someone changed it meanwhile. Reload and try again."]}",
        OrganizationApiException api => $"{api.StatusCode}: {ProblemDetail(api) ?? FirstLine(api.Response) ?? FirstLine(api.Message)}",
        HttpRequestException http => L["The central Organization service is not reachable ({0}).", http.Message],
        _ => ex.Message,
    };

    /// <summary>The "detail" (else "title") of a problem-details response body, if it is one.</summary>
    public static string? ProblemDetail(OrganizationApiException api)
    {
        if (string.IsNullOrWhiteSpace(api.Response) || !api.Response.TrimStart().StartsWith('{')) return null;
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(api.Response);
            foreach (var name in new[] { "detail", "title" })
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                    && value.GetString() is { Length: > 0 } text)
                    return text;
        }
        catch (System.Text.Json.JsonException) { }
        return null;
    }

    private static string? FirstLine(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Split('\n', 2)[0].Trim();

    protected static string When(DateTimeOffset? at) => at?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";

    protected static string Day(DateTimeOffset? at) => at?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";

    public virtual void Dispose()
    {
        Locale.Changed -= OnLocaleChanged;
        _remote?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Adds the user's access token and language to calls to a central service in another deployment.</summary>
    private sealed class BearerTokenHandler(IAccessTokenProvider tokens, NavigationManager nav) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var result = await tokens.RequestAccessToken();
            if (!result.TryGetToken(out var token))
                throw new AccessTokenNotAvailableException(nav, result, null);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(System.Globalization.CultureInfo.CurrentUICulture.Name));
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
