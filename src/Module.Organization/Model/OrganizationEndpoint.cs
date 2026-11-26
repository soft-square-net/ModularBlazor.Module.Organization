namespace soft.square.net.Module.Organization.Model;

/// <summary>
/// Where the central Organization service is, from the setting <c>Organization:BaseUrl</c> of the Blazor host's
/// configuration (its wwwroot/appsettings.json, read through IConfiguration like the host's own WebApi:BaseUrl):
/// <list type="bullet">
/// <item>empty (the default): the gateway's route <c>/api/organization</c>, on the host's "WebApi" HttpClient, which
/// already carries the access token;</item>
/// <item>an absolute http(s) URL: a central service in another deployment. The pages call it with the user's access
/// token, so that service must trust this identity service (its <c>Organization:TrustedIssuers</c>) and allow this
/// site's origin (CORS).</item>
/// </list>
/// </summary>
public sealed record OrganizationEndpoint(Uri? BaseAddress, string? Problem)
{
    public const string SettingKey = "Organization:BaseUrl";

    /// <summary>The gateway (no setting).</summary>
    public static OrganizationEndpoint Gateway { get; } = new(null, null);

    public bool UsesGateway => BaseAddress is null;

    public static OrganizationEndpoint From(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting)) return Gateway;
        var text = setting.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return new(null, $"{SettingKey} '{text}' is not an absolute http(s) URL; the gateway is used instead.");
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return new(null, $"{SettingKey} '{text}' must not have a query or a fragment; the gateway is used instead.");

        // The generated client's routes are relative ("api/organization/..."): the base must end with a slash, or the
        // last segment of its path would be replaced.
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        return new(builder.Uri, null);
    }
}
