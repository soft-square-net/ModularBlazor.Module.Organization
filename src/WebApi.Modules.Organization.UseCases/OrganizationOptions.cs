namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>Module settings (section <c>Organization</c>; defaults in the manifest, changed on the Host Modules page).</summary>
public sealed class OrganizationOptions
{
    /// <summary>The largest page a list or search returns.</summary>
    public int MaxPageSize { get; set; } = 100;
}
