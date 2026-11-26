# Tenant seeds

[العربية](../ar/seeds.md) · [Setup](setup.md)

A **tenant seed** says that a unit of an org gets a tenant of its own in a deployment. The unit becomes the tenant's
**root unit**: it and the units below it live in that tenant, unless one of them owns a tenant itself. The central
service keeps the seeds; each deployment's host turns its seeds into tenants and reports back.

## Fields

| Field | What |
| --- | --- |
| `rootUnitId` | the unit that owns the tenant (a unit owns at most one) |
| `deploymentId` | the deployment that creates it: its `Platform:DeploymentId` |
| `tenantId` | the tenant's id there: a slug (`newsdesk`), unique per deployment |
| `name`, `hosts` | the tenant's display name and host names (`newsdesk.example`); a host can belong to one tenant only |
| `databaseServer`, `databaseName` | the requested placement: a server the deployment knows and a database name. The host may adjust it |
| `modules` | modules for the tenant (passed to the host) |
| `adminPersonIds` | persons of the org who administer the tenant (below) |

The seed's id is generated; `version` starts at 1 and grows with every edit.

## Who may

- **Create** a seed: platform admins and the org's admins (`POST /orgs/{orgId}/seeds`, or the **Tenant seeds** page).
- **Edit** a seed: the same, or `org.seeds.manage` on its root unit or above (the `unit-admin` position has it).
  Every edit is a new version, which the host reconciles again.
- **Read** a deployment's seeds and **report** status: the connectors' service clients only
  (`GET /seeds?deployment=`, `POST /seeds/{seedId}/reports`).

## What happens

```
central: seed v1 (Requested) ──► B's connector polls GET /seeds?deployment=branch
                                    └► tenancy.seed-changed.v1 → B's reconciler runs a cycle
B's reconciler: placement policy → creates tenant "newsdesk" (database, migrations) → reports Provisioning, Provisioned
B's connector: first sync of the tenant's replica (its slice of the org) → grants of the members' positions
```

1. The connector pulls the seeds of its deployment every `PollInterval`. A new seed or a new version publishes
   `tenancy.seed-changed.v1`, so the host reconciles at once instead of at its next `Tenancy:Seeds:Interval`.
2. The host's reconciler (one leader per deployment) applies its placement policy, creates the tenant and its
   database, waits for the modules to initialize it, and reports the outcome through the connector.
3. The connector syncs the tenant's replica before the module serves the tenant: every unit of the org, the org's
   persons and positions, the memberships of the tenant's units, and the links that name the seed.
4. Each membership with a position becomes standing grants of the position's permissions on `unit:{id}`, under the
   grant source `org:{unitId:N}`.

**Admins.** Each person in `adminPersonIds` gets a `unit-admin` membership on the root unit (created by the seed;
removed when the person leaves the list), so `org.units.manage`, `org.members.manage` and `org.seeds.manage` there.
The host also gives each admin's identity a standing `access.grants.manage` on the tenant (source `seed:{seedId}`), so
they can manage the tenant's other grants.

## Status

| Status | Meaning |
| --- | --- |
| `Requested` | the central service's own entry for each new version: no host has answered yet |
| `Provisioning` | the host is creating the tenant |
| `Provisioned` | the tenant exists with this version |
| `Adjusted` | the tenant exists, with a different placement than requested (`actualPlacement`); a seed never moves an existing tenant's database |
| `Rejected` | the host refused this version (`reason`): the tenant id is not a slug, the tenant exists but was not created by this seed, a host belongs to another tenant, or the database server is unknown |
| `Failed` | creating the tenant failed (`reason`); the host tries again on its next cycle |

`status`, `reason` and `reportedVersion` are the latest report for the **current** version. A report for an older
version only goes into `history`, which keeps the latest 50 entries (time, version, status, reason, placement). The
**Tenant seeds** page shows both.

A version is recorded on the host's tenant only after the Provisioned or Adjusted report reached the central service;
a report that fails is sent again on the next cycle. If the seed stays `Requested`, see
[Troubleshooting](troubleshooting.md#a-seed-stays-requested).
