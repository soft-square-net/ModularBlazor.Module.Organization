namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>Which half of the bundle runs in this deployment.</summary>
public enum OrganizationMode
{
    /// <summary>
    /// Every deployment: pulls seeds and the org feed from the central service (<see cref="OrganizationOptions.CentralUrl"/>)
    /// into per-tenant replicas, serves the seeds to the host and republishes the org.* events locally.
    /// </summary>
    Connector,

    /// <summary>
    /// One deployment: owns every customer's org data, serves the management API, the seeds and the feed. It runs the
    /// connector too (its own tenants have replicas like any other deployment's).
    /// </summary>
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

    /// <summary>
    /// The connector: the central service's module route, absolute (<c>https://central.example/api/organization/</c>).
    /// Empty: the connector does not sync, and the replicas keep what they have.
    /// </summary>
    public string CentralUrl { get; set; } = "";

    /// <summary>The connector: this deployment's id (as the host's <c>Platform:DeploymentId</c>). Empty: the one the host's seed reconciler asks for.</summary>
    public string DeploymentId { get; set; } = "";

    /// <summary>The connector's client-credentials token endpoint (<c>https://id.example/connect/token</c>). Empty: calls carry no token.</summary>
    public string TokenEndpoint { get; set; } = "";

    /// <summary>The connector's client id: one of the central service's <see cref="ServiceClients"/>.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// The connector's client secret. Keep it out of the module catalog: set it in the host's configuration as
    /// <c>Modules:Settings:organization:Organization:ClientSecret</c> (a host override, never written to modules.json).
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>The scopes the connector asks for, space-separated (optional).</summary>
    public string ClientScope { get; set; } = "";

    /// <summary>How often the connector pulls the seeds and the feeds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How far back the connector reads the feed again: a change that took longer than this to commit after its
    /// feed id was taken could be missed. Entries read twice are applied once.
    /// </summary>
    public TimeSpan FeedOverlap { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The feed page the connector asks for.</summary>
    public int FeedPageSize { get; set; } = 200;

    public bool ConnectorEnabled => Uri.TryCreate(CentralUrl, UriKind.Absolute, out _);

    public IReadOnlyList<string> TrustedIssuerList => Split(TrustedIssuers);

    public IReadOnlyList<string> ServiceClientList => Split(ServiceClients);

    private static string[] Split(string? value) =>
        (value ?? "").Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
