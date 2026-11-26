# Settings reference

[العربية](../ar/settings.md) · [Setup](setup.md)

## Module settings

Section `Organization`. Defaults come from the module's manifest; change them on the Host Modules page, or override
them in the host's configuration as `Modules:Settings:organization:Organization:<Key>` (environment:
`Modules__Settings__organization__Organization__<Key>`). A host override wins and is never written to the module
catalog: use it for secrets. Lists are comma-separated (module settings are flat strings). Times are `hh:mm:ss`.

| Key | Mode | Default | What |
| --- | --- | --- | --- |
| `Mode` | both | `Connector` | `Central` on exactly one deployment: the management API, the platform-level database, seeds and feeds. `Connector` everywhere else (the management API answers 404). |
| `TrustedIssuers` | Central | empty | Token issuers (`iss`) the central service accepts, comma-separated, e.g. `https://id.example/`. Empty: any issuer the host itself accepts. |
| `ServiceClients` | Central | empty | Client ids of the connectors (matched against the token's `sub`, `client_id` or `azp`). They may read a deployment's seeds and the org feeds, and report seed status; nothing else. |
| `DatabaseServer` | Central | `TenantStore` | The server of the platform-level database: a connection string name the host knows (`ConnectionStrings:<name>`, provider `DatabaseServers:<name>:Provider`). |
| `DatabaseName` | Central | `organization` | The platform-level database on that server, created when missing. Not a tenant database. |
| `MaxFeedPage` | Central | `500` | The largest feed page one call returns. |
| `CentralUrl` | both | empty | The central service's module route, absolute, ending in `/`: `https://central.example/api/organization/`. Empty: the connector does not sync and the replicas keep what they have. The central deployment points it at itself. |
| `DeploymentId` | both | empty | This deployment's id for the connector. Empty: the id the host's seed reconciler asks for (`Platform:DeploymentId`). Set it only when the two must differ. |
| `TokenEndpoint` | both | empty | The identity service's token endpoint: `https://id.example/connect/token`. Empty: the connector's calls carry no token (the central service then refuses them). |
| `ClientId` | both | empty | The connector's client id; one of the central service's `ServiceClients`. |
| `ClientSecret` | both | empty | The connector's client secret. **Set it only as a host override** (`Modules:Settings:organization:Organization:ClientSecret`), from a secret store. |
| `ClientScope` | both | empty | Scopes the connector asks for, space-separated. On the platform's identity service: `api`. |
| `PollInterval` | both | `00:00:30` | How often the connector pulls the seeds and the feeds. An edit on the central service reaches a replica within about this time. |
| `FeedOverlap` | both | `00:01:00` | How far back the connector reads the feed again (feed ids can commit out of order). A change that takes longer than this to commit could be missed. |
| `FeedPageSize` | both | `200` | The feed page the connector asks for. |

## Host settings

Platform settings the module depends on, in each deployment's WebApi configuration:

| Key | Default | What |
| --- | --- | --- |
| `Platform:DeploymentId` | empty | This deployment's id (unique). Switches the seed reconciler on; seeds whose `deploymentId` differs are ignored. Without it nothing is provisioned and the connector waits ("waits for the deployment id" in the log). |
| `Tenancy:Seeds:Interval` | `00:01:00` | How often the reconciler's leader pulls the seeds. A new seed version also triggers a cycle at once (`tenancy.seed-changed.v1`, published by the connector). |
| `Tenancy:Seeds:LockConnectionName` | `redis` | The Redis connection of the leader lock; without Redis the tenant store's database lock is used. |
| `ConnectionStrings:TenantStore` | | The tenant store. Needed for seeds; on the central deployment also the default server of the `organization` database. |
| `ConnectionStrings:<server>` + `DatabaseServers:<server>:Provider` | | Every database server a seed may name (`databaseServer`). An unknown server is a Rejected seed. |
| `Auth:Authority`, `Auth:Audience` | | The identity service and `modular-webapi`, as for every module. |

## Identity service

| What | Value |
| --- | --- |
| The connector's client | confidential, grant type client credentials, scope `api`; its id in `ServiceClients` and `ClientId`, its secret in `ClientSecret` |
| Users of a seeded tenant | accounts with an active membership in the tenant (the seed creates the tenant in the deployment's tenant store, not memberships at the identity service) |

## Blazor host

| Key (in `wwwroot/appsettings.json`) | Default | What |
| --- | --- | --- |
| `Organization:BaseUrl` | empty | Where the Organization pages call the central service. Empty: the gateway (`{WebApi:BaseUrl}api/organization/`), which needs the gateway route of [Setup](setup.md#4-the-gateway-of-a-connector-deployment). An absolute URL: the central service directly; it must allow this site's origin (CORS) and trust this identity service (`TrustedIssuers`). |
