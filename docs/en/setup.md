# Setup: Central and Connector

[العربية](../ar/setup.md) · [Settings reference](settings.md)

The same bundle (`organization-<version>.zip`) goes on every deployment. A setting picks what it does:

| Deployment | `Organization:Mode` | `Organization:CentralUrl` | What runs |
| --- | --- | --- | --- |
| The central one (exactly one) | `Central` | its own module route | the management API and database, plus its connector for its own tenants |
| Every other one | `Connector` (default) | the central service's module route | the connector only; the management API answers 404 |

Install it as any module: upload the server part on the Host Modules page (`/host-modules`) of each deployment and
activate it, then the client parts on the Client Modules page (`/client-modules`), the API client first. Load it on the
**core pool** of every deployment: the live hub runs there and asks the module for a topic's scopes.

## 1. Register the connector's service client

Every connector calls the central service with a client-credentials token. Register one confidential client at the
identity service (on the identity admin's **Applications** page): grant type client credentials, scope `api` (the
platform's WebApi audience, `modular-webapi`). Note its client id (for example `organization-connector`) and keep the
secret in your secret store. One client for all deployments is enough; one per deployment lets you revoke one.

The token's `sub` is the client id. The central service accepts it only if it is listed in
`Organization:ServiceClients`.

## 2. The central deployment

Module settings (Host Modules page, or host overrides `Modules:Settings:organization:Organization:<Key>`):

```
Organization:Mode              = Central
Organization:TrustedIssuers    = https://id.example/              (the identity service's issuer; empty: any issuer the host trusts)
Organization:ServiceClients    = organization-connector           (comma-separated client ids)
Organization:DatabaseServer    = TenantStore                      (a server the host knows; default)
Organization:DatabaseName      = organization                     (created on first start; default)
```

and the connector, pointed at itself (its own tenants get replicas like anyone else's):

```
Organization:CentralUrl        = https://central.example/api/organization/
Organization:TokenEndpoint     = https://id.example/connect/token
Organization:ClientId          = organization-connector
Organization:ClientScope       = api
```

Host configuration (environment, secret store), never in the module catalog:

```
Modules__Settings__organization__Organization__ClientSecret = <the client's secret>
Platform__DeploymentId                                      = central
```

On start the central service creates the `organization` database (schema `organization`) and applies its migrations,
retrying every 30 seconds while the server is down. The management API is under `/api/organization`.

## 3. A connector deployment

Module settings:

```
Organization:Mode              = Connector                        (default)
Organization:CentralUrl        = https://central.example/api/organization/
Organization:TokenEndpoint     = https://id.example/connect/token
Organization:ClientId          = organization-connector
Organization:ClientScope       = api
```

Host configuration:

```
Modules__Settings__organization__Organization__ClientSecret = <the client's secret>
Platform__DeploymentId                                      = branch      (this deployment's id, unique)
```

`Platform:DeploymentId` switches the host's seed reconciler on: it pulls the seeds for this id through the connector,
creates the tenants and reports their status back. The connector uses the same id (or `Organization:DeploymentId` when
set). Without it nothing is provisioned.

## 4. The gateway of a connector deployment

The client of a connector deployment calls the management API (orgs, units, people, seeds) through its own gateway,
so the gateway sends it to the central service. The module's local routes, which read this deployment's replica, and
everything else stay in the deployment. With YARP (the order matters: the more specific route first):

```csharp
yarp.AddRoute("/api/organization/local/{**catch-all}", coreCluster);      // this deployment's replica
yarp.AddRoute("/api/organization/{**catch-all}", centralCluster);         // the central service
yarp.AddRoute("/{**catch-all}", coreCluster);                             // everything else, the live hub included
```

`centralCluster` is the central deployment's public address. The central deployment itself needs no extra route: its
catch-all already sends `/api/organization` to its core pool.

Instead of the route, the Blazor host can call the central service directly: set `Organization:BaseUrl` in its
`wwwroot/appsettings.json` (empty: the gateway). Then the central service must allow the client's origin (CORS).

### Users of other deployments

A connector deployment's users call the central service with a token for one of *their* tenants, which the central
deployment does not know. From platform 1.0.0-preview.8 the module's central routes accept it, and the service
authorizes such a *foreign* caller from its own data. The central deployment needs no tenant of the same id.

- The token's `tenant` claim must be that tenant (`X-Tenant` alone is refused).
- The caller reaches only the orgs with a [tenant seed](seeds.md) for that tenant id; inside them, being an org admin,
  a person or a member with a position decides (by the token's issuer and `sub`).
- The `platform-admin` role does not apply. Platform admins manage the central service signed in without a tenant, or
  in a tenant the central deployment has.

Tenant ids are matched by name across deployments, and the identity service that issued the token must be trusted
(`Organization:TrustedIssuers`). A tenant id the central deployment also has is that central tenant, so don't reuse
one for a different tenant.

## 5. Check it

1. Create an org as a platform admin (`POST /api/organization/orgs` on the central service, or the **Organization**
   pages).
2. Build the tree, add a person and a membership with a position.
3. Create a [tenant seed](seeds.md) for the connector deployment's id. Within a reconciler cycle
   (`Tenancy:Seeds:Interval`, default one minute, or at once after the connector's next poll) the seed's status goes
   Requested → Provisioning → Provisioned.
4. In the new tenant, `GET /api/organization/local/units` lists the seed's root unit and the units below it, and the
   members' positions show as grants (`GET /api/identity/grants?source=org:{unitId:N}`).

The repo's playground does all of this on two local deployments: `playground/demo.ps1` (README, "Developing").
