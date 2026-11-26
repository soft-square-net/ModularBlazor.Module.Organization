using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this client module: read by the WebApi's client-modules core module when the file is uploaded.
// Open to every signed-in user: what each one may do is decided by the central Organization service from its own data
// (platform admins, customer admins, unit admins and managers), not by tenant grants, so the menu links carry no
// Permissions (the host would check them against the tenant's grants and hide the pages from customer admins). The
// pages work out the same rights from the org's data to show only the buttons that will work, and show the API's
// answer otherwise. No tenant is needed, except for the Content categories of a Section (read in the current tenant).
[assembly: ClientModule(Name = "Organization", Layout = "default", RoutePrefixes = new[] { "organization" },
    Dependencies = new[] { "WebApi.Modules.Organization.Client" }, Styles = new[] { "organization.css" },
    NavTitle = "Organization", NavHref = "organization", NavOrder = 50)]

// The menu (it replaces the NavTitle entry): one group with the org list first and a link per page. The per-org pages
// open on the org used last in the session, or the only one, else they ask for one.
[assembly: MenuGroup("organization", "Organization", Icon = "network", Order = 50)]
[assembly: MenuLink("orgs", "Organizations", "organization", Parent = "organization", Icon = "building-2", Order = 10)]
[assembly: MenuLink("units", "Units", "organization/units", Parent = "organization", Icon = "git-fork", Order = 20)]
[assembly: MenuLink("people", "People", "organization/people", Parent = "organization", Icon = "users", Order = 30)]
[assembly: MenuLink("positions", "Positions", "organization/positions", Parent = "organization", Icon = "badge-check", Order = 40)]
[assembly: MenuLink("seeds", "Tenant seeds", "organization/seeds", Parent = "organization", Icon = "sprout", Order = 50)]
[assembly: MenuLink("links", "Tenant links", "organization/links", Parent = "organization", Icon = "link", Order = 60)]
[assembly: MenuLink("settings", "Settings", "organization/settings", Parent = "organization", Icon = "settings", Order = 70)]
