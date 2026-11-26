# Tenant links (skeleton)

[العربية](../ar/links.md)

A **tenant link** joins two sibling tenants of the same org: a unit of one tenant (`fromSeedId`, `fromUnitId`)
connects to a unit of another (`toSeedId`, `toUnitId`). Linked tenants will be able to follow each other's actions
through a future Webhooks module. In this preview the link is a **skeleton**: the data and its states exist, nothing
acts on them.

## States

```
Requested ──accept──► Active ──revoke──► Revoked
    └──────decline──► Declined
```

| State | Meaning |
| --- | --- |
| `Requested` | one tenant asked (`requestedAt`) |
| `Active` | the other accepted (`decidedAt`) |
| `Declined` | the other declined (`decidedAt`) |
| `Revoked` | an active link was ended |

Only a requested link can be accepted or declined, and only an active one revoked. A link always joins two different
tenants.

## What exists

- `GET /api/organization/orgs/{orgId}/links` on the central service, and the **Tenant links** page, list an org's
  links.
- Each change is an `org.tenant-link-changed.v1` event in the org's feed. A deployment's connector keeps the links that
  name its tenant's seed in the replica and republishes the event in that tenant.

## Not yet

- No endpoints to request, accept, decline or revoke a link: links are not created through the API yet.
- No capabilities: an active link grants nothing and carries no actions.
