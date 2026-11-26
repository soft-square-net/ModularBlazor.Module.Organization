# Troubleshooting

[العربية](../ar/troubleshooting.md) · [Settings reference](settings.md)

## Known gaps in this preview

### 403 "The token's tenant … does not exist"

**Symptom.** On a connector deployment, the Organization pages (or any call to `/api/organization/*` routed to the
central service) fail with 403 `The token's tenant 'newsdesk' does not exist`, or 404 `Unknown tenant 'newsdesk'`.
Platform admins without a tenant work.

**Cause.** A user's token is bound to a tenant of their own deployment, and the client sends `X-Tenant`. The platform
host on the central deployment resolves the tenant of every request from its own tenant store and refuses one it does
not know, before the module runs, even on the module's tenant-optional routes. The central service authorizes from its
own data and does not need the tenant.

**Workaround.** Give the central deployment a tenant with the same id (it is never seeded or replicated; it only lets
the host accept the token), for example in the central deployment's host configuration:

```
Modules__Settings__tenancy__Tenancy__Seed__<n>__Id           = newsdesk
Modules__Settings__tenancy__Tenancy__Seed__<n>__DatabaseName = tenant-newsdesk-stub
```

The playground does this (`Playground:CentralKnowsBranchTenants`, on by default). A platform change that accepts such
tokens on tenant-optional routes is the real fix.

### The org page never shows "Live"

**Symptom.** The Units page has no **Live** chip and changes made elsewhere show only after **Refresh**. With
`Logging:LogLevel:soft.square.net.WebApi.Live=Debug`, the hub logs
`Live subscription of … to organization:org:… refused: forbidden`.

**Cause.** The org topic `organization:org:{orgId:N}` needs `org.read` on `org:{orgId:N}` or on the org's **root**
unit, in the subscriber's tenant. No position gives `org.read`; a tenant whose seed is rooted below the org root gets
grants only on its own units; and platform roles hold platform permissions only. So by default nobody in a connector
tenant holds it.

**Workaround.** A tenant admin (the seed's admins hold `access.grants.manage`) grants `org.read` on the root unit to the
users who keep the org page open:

```http
POST /api/identity/grants
X-Tenant: newsdesk

{ "userId": "<subject>", "permission": "org.read", "scope": "unit:<rootUnitId:N>" }
```

The playground's demo script does this for the platform admin. Unit topics (`unit:{id}`) are not affected: a grant on
the unit or any ancestor counts.

## Seeds

### A seed stays Requested

| Check | Fix |
| --- | --- |
| The deployment has `Platform:DeploymentId`, equal to the seed's `deploymentId` | set it on every replica of the deployment; the reconciler ignores other ids |
| The connector's log says "waits for the deployment id" | same: the connector learns the id from the reconciler or `Organization:DeploymentId` |
| The connector can reach the central service | `CentralUrl` is absolute and ends in `/api/organization/`; through a gateway, the route reaches the central deployment |
| The token is accepted: 401 from the central service | `TokenEndpoint`, `ClientId`, `ClientSecret` (host override), `ClientScope=api`; the client is registered for client credentials |
| 403 from `GET /seeds?deployment=` | the client id is in the central service's `ServiceClients`, and its issuer in `TrustedIssuers` (or that list is empty) |
| The module is active on the deployment | the reconciler only asks modules that are loaded and active on the replica that leads |
| Wait | a cycle runs every `Tenancy:Seeds:Interval` (one minute), at once when the connector sees a new version (`PollInterval`, 30 s) |

### A seed is Rejected or Failed

`reason` says why. Rejected: the tenant id is not a slug (lowercase letters, digits, `-`), the tenant exists but was
created by hand or by another seed, a host name belongs to another tenant, or the database server is not configured on
the deployment (`ConnectionStrings:<server>`). Fix the seed (a new version) or the deployment. Failed: the tenant could
not be created (database server down, a module's migration failed); the host tries again on its next cycle, and the
deployment's log has the error.

### Adjusted instead of Provisioned

The host placed the tenant differently than requested (`actualPlacement`), for example because the tenant already
existed on another server: a seed never moves a tenant's database. Nothing to fix unless the placement matters.

## Replica and grants

### The tenant's local units are empty

`GET /api/organization/local/units` returns `[]` when the tenant was not created from a seed (a hand-made tenant has no
replica), or before its first sync. The connector syncs a new tenant before the module serves it; afterwards every
`PollInterval`. The replica keeps serving while the central service is down.

### An edit on the central service does not show in a deployment

It arrives within `PollInterval` (30 s by default) after it commits. If it never does, check the connector's log for
token or network errors. A change whose commit took longer than `FeedOverlap` (1 minute) after its feed id was taken can
be missed; raise `FeedOverlap` if very long transactions are normal.

### A member's grants are missing

Grants come only from memberships in the tenant's own units (the seed's root unit and below) that have a position, with
that position's permissions, on `unit:{id}`, source `org:{unitId:N}`. Platform permissions in a position are left out
(they come from platform roles), except `access.grants.manage`. An ended membership gives none; the others are valid
from the membership's `from` to its `until`.

### Unit topics count only the exact unit

The module is not loaded on the pool that runs the live hub (the core pool). Load it there: without it the hub cannot
ask for a unit's ancestors.

## API

| Answer | Cause |
| --- | --- |
| 404 on `/api/organization/orgs…` on a connector deployment | the management API runs on the central deployment only: add the gateway route ([Setup](setup.md#4-the-gateway-of-a-connector-deployment)) or set `Organization:BaseUrl` |
| 400 "This endpoint needs a tenant" on `/api/organization/local/…` | the local routes read a tenant's replica: send `X-Tenant` or use the tenant's host name |
| 409 with a message | the item changed since you read it (send the current `version`), a duplicate key, or a reference to a removed item |
| 500 "Not saved" | the database refused the change for another reason; the central service's log has the error |
