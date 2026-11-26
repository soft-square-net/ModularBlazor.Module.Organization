using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace soft.square.net.WebApi.Modules.Organization.Web.Extensions;

/// <summary>
/// The caller of the central service, from the request's token (validated by the host). A token from an issuer that
/// is not one of <c>Organization:TrustedIssuers</c> (when set) counts as anonymous here.
/// </summary>
public sealed class HttpCaller : ICaller
{
    public HttpCaller(IHttpContextAccessor http, IOptions<OrganizationOptions> options)
    {
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;
        var subject = user.FindFirst(PlatformClaims.Subject);
        if (subject is null || string.IsNullOrEmpty(subject.Value)) return;

        // The JWT's own "iss" claim (inbound claim mapping is off); otherwise the issuer the handler stamped on the claim.
        var issuer = user.FindFirst("iss")?.Value ?? subject.Issuer;
        var trusted = options.Value.TrustedIssuerList;
        if (trusted.Count > 0 && !trusted.Contains(issuer, StringComparer.OrdinalIgnoreCase)) return;

        Issuer = issuer;
        Subject = subject.Value;
        IsPlatformAdmin = user.HasClaim(PlatformClaims.Role, PlatformRoles.Admin);
        var clients = options.Value.ServiceClientList;
        IsServiceClient = clients.Count > 0 && ClientIds(user).Any(id => clients.Contains(id, StringComparer.Ordinal));
    }

    public string? Issuer { get; }
    public string? Subject { get; }
    public bool IsPlatformAdmin { get; }
    public bool IsServiceClient { get; }

    /// <summary>A client-credentials token names its client in <c>sub</c>, <c>client_id</c> or <c>azp</c>, depending on the server.</summary>
    private static IEnumerable<string> ClientIds(ClaimsPrincipal user) =>
        new[] { PlatformClaims.Subject, "client_id", "azp" }.Select(type => user.FindFirst(type)?.Value).OfType<string>();
}

/// <summary>The central endpoints exist in Central mode only; elsewhere they answer 404.</summary>
public sealed class CentralOnlyFilter(IOptions<OrganizationOptions> options) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        options.Value.Mode == OrganizationMode.Central
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Problem("The central Organization service does not run in this deployment (Organization:Mode is not Central).",
                statusCode: StatusCodes.Status404NotFound));
}
