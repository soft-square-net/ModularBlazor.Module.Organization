namespace soft.square.net.WebApi.Modules.Organization.Core;

/// <summary>
/// The Organization permissions. On the central service they come from the service's own data (a membership's
/// position on a unit of the target's path), never from tenant grants. They are also declared in the manifest, so
/// they are in each deployment's permission catalog: positions name them, and the connector writes them as grants.
/// </summary>
public static class OrgPermissions
{
    /// <summary>Create, edit, move and remove the units of a subtree.</summary>
    public const string UnitsManage = "org.units.manage";

    /// <summary>Add, change and end the memberships of a subtree (and add persons to the org).</summary>
    public const string MembersManage = "org.members.manage";

    /// <summary>Edit the tenant seeds whose root unit is in a subtree.</summary>
    public const string SeedsManage = "org.seeds.manage";

    /// <summary>Read the org's units and members (every person of the org can, without this).</summary>
    public const string Read = "org.read";
}
