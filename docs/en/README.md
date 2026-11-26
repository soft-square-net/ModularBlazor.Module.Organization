# Organization: operator docs

[العربية](../ar/README.md)

Organization is one bundle that runs in two modes. One deployment runs the **central service**
(`Organization:Mode=Central`): it holds every customer's org tree, people, positions, tenant seeds and links. Every
deployment, the central one included, runs the **connector**: it pulls its seeds and the orgs' changes from the central
service into a replica in each tenant's database.

```
                 identity service (one issuer)
                   ▲ client credentials        ▲ users sign in
                   │                           │
Deployment A (central)                         Deployment B (a connector)
  core pool: Organization, Mode=Central  ◄──── gateway: /api/organization/*        → A (management API)
    organization database (platform level)       /api/organization/local/*  → B's core pool
    connector → its own tenants                  everything else            → B's core pool
                                          ◄──── B's connector: seeds, feed, status reports
                                                 B's seed reconciler creates the tenants, B's replicas serve them
```

| Page | What |
| --- | --- |
| [Setup](setup.md) | the central deployment, a connector deployment, the identity client, the gateway, the Blazor host |
| [Settings reference](settings.md) | every setting, where it goes and its default |
| [Tenant seeds](seeds.md) | how a unit gets its own tenant in a deployment, and the status it reports |
| [Tenant links](links.md) | the links skeleton between sibling tenants |
| [Troubleshooting](troubleshooting.md) | symptoms, causes and fixes, including two known gaps of this preview |

The repo's [README](../../README.md) covers the API, building, the playground and the release steps.
