using soft.square.net.ModularBlazor.Abstractions;

// Default settings of this client module: read by the WebApi's client-modules core module when the file is uploaded.
// Open to every signed-in user: what each one may do is decided by the central Organization service from its own data
// (platform admins, customer admins, unit admins and managers), and the page shows its answer. No tenant is needed.
[assembly: ClientModule(Name = "Organization", Layout = "default", RoutePrefixes = new[] { "organization" },
    Dependencies = new[] { "WebApi.Modules.Organization.Client" }, Styles = new[] { "organization.css" },
    NavTitle = "Organization", NavHref = "organization", NavOrder = 50)]
