using System.Security.Claims;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Contracts.Tenancy;

namespace soft.square.net.WebApi.Modules.Organization.Web.Extensions;

/// <summary>
/// The caller of the central service, from the request's token (validated by the host). A token from an issuer that
/// is not one of <c>Organization:TrustedIssuers</c> (when set) counts as anonymous here.
/// <para>A caller from a tenant this deployment does not know (<see cref="ITenantContext.ForeignTenantId"/>) is who the
/// token's <c>sub</c> says, in that tenant, and nothing more: the platform-admin role and the service client list do not
/// apply, as the host gives such a caller no platform-role permissions either. When only the request named the tenant
/// (<see cref="ForeignCallers.IsUnproven"/>), the caller has no identity at all.</para>
/// </summary>
public sealed class HttpCaller : ICaller
{
    public HttpCaller(IHttpContextAccessor http, IOptions<OrganizationOptions> options, ITenantContext tenant)
    {
        if (ForeignCallers.IsUnproven(tenant)) return;
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
        if (tenant.IsForeign)
        {
            ForeignTenantId = tenant.ForeignTenantId;
            return;
        }
        IsPlatformAdmin = user.HasClaim(PlatformClaims.Role, PlatformRoles.Admin);
        var clients = options.Value.ServiceClientList;
        IsServiceClient = clients.Count > 0 && ClientIds(user).Any(id => clients.Contains(id, StringComparer.Ordinal));
    }

    public string? Issuer { get; }
    public string? Subject { get; }
    public bool IsPlatformAdmin { get; }
    public bool IsServiceClient { get; }
    public string? ForeignTenantId { get; }

    /// <summary>A client-credentials token names its client in <c>sub</c>, <c>client_id</c> or <c>azp</c>, depending on the server.</summary>
    private static IEnumerable<string> ClientIds(ClaimsPrincipal user) =>
        new[] { PlatformClaims.Subject, "client_id", "azp" }.Select(type => user.FindFirst(type)?.Value).OfType<string>();
}

/// <summary>Callers from tenants this deployment does not know (platform preview.8, tenant-optional endpoints).</summary>
public static class ForeignCallers
{
    /// <summary>
    /// The tenant is foreign and the token was not issued for it: the request only named it (<c>X-Tenant</c>), which
    /// proves nothing. Anything but "claim" counts.
    /// </summary>
    public static bool IsUnproven(ITenantContext tenant) => tenant.IsForeign && tenant.ResolvedBy != "claim";
}

/// <summary>
/// 403 for a request that names a foreign tenant its token was not issued for (<see cref="ForeignCallers.IsUnproven"/>),
/// whoever signed in.
/// </summary>
public sealed class ForeignTenantFilter(ITenantContext tenant) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        ForeignCallers.IsUnproven(tenant)
            ? ValueTask.FromResult<object?>(Results.Problem(
                "The request names a tenant this deployment does not know, and the token was not issued for it.",
                statusCode: StatusCodes.Status403Forbidden, title: "Tenant"))
            : next(context);
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
