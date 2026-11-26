namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>Which half of the bundle runs in this deployment.</summary>
public enum OrganizationMode
{
    /// <summary>Every deployment: pulls seeds and the org feed from the central service into per-tenant replicas (P1-2).</summary>
    Connector,

    /// <summary>One deployment: owns every customer's org data, serves the management API, the seeds and the feed.</summary>
    Central
}

/// <summary>
/// Module settings (section <c>Organization</c>; defaults in the manifest, changed on the Host Modules page). Module
/// settings are flat strings, so lists are comma-separated.
/// </summary>
public sealed class OrganizationOptions
{
    public OrganizationMode Mode { get; set; } = OrganizationMode.Connector;

    /// <summary>
    /// Central mode: token issuers (<c>iss</c>) whose callers the central service accepts, comma-separated. Empty: any
    /// issuer the host itself accepts. One shared identity provider for now.
    /// </summary>
    public string TrustedIssuers { get; set; } = "";

    /// <summary>
    /// Central mode: the client ids (the <c>sub</c> of their client-credentials tokens) of the deployments' connectors,
    /// comma-separated. They may read seeds and feeds and report seed status.
    /// </summary>
    public string ServiceClients { get; set; } = "";

    /// <summary>Central mode: the database server (a connection string name the host knows) of the platform-level database.</summary>
    public string DatabaseServer { get; set; } = "TenantStore";

    /// <summary>Central mode: the platform-level database on that server (created when missing). Not a tenant database.</summary>
    public string DatabaseName { get; set; } = "organization";

    /// <summary>The largest page of the feed a call returns.</summary>
    public int MaxFeedPage { get; set; } = 500;

    public IReadOnlyList<string> TrustedIssuerList => Split(TrustedIssuers);

    public IReadOnlyList<string> ServiceClientList => Split(ServiceClients);

    private static string[] Split(string? value) =>
        (value ?? "").Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
