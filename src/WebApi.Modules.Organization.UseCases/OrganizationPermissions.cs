using soft.square.net.WebApi.Modules.Organization.Core;

namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>
/// The permissions the module declares in its manifest (<see cref="All"/>), so they are in every deployment's permission
/// catalog: positions name them, and the connector (P1-2) turns positions into standing grants on <c>unit:{id}</c>.
/// The central management API does not use tenant grants: it authorizes from its own data (<see cref="OrgAccess"/>).
/// </summary>
public static class OrganizationPermissions
{
    public const string UnitsManage = OrgPermissions.UnitsManage;
    public const string MembersManage = OrgPermissions.MembersManage;
    public const string SeedsManage = OrgPermissions.SeedsManage;
    public const string Read = OrgPermissions.Read;

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Read the organization's units and members."),
        new(UnitsManage, "Create, edit, move and remove the units of a subtree (on unit:{id})."),
        new(MembersManage, "Add, change and end the memberships of a subtree (on unit:{id})."),
        new(SeedsManage, "Edit the tenant seeds owned by the units of a subtree (on unit:{id}).")
    ];
}
