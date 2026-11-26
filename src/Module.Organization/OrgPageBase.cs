using Microsoft.AspNetCore.Components;
using soft.square.net.Module.Organization.Model;
using soft.square.net.WebApi.Modules.Organization.Client;

namespace soft.square.net.Module.Organization;

/// <summary>
/// A page about one org (<c>?org={id}</c>): the orgs the user can see, the chosen one (else the one used last in this
/// session, else the only one), and what the user may do in it (<see cref="OrgRightsView"/>).
/// </summary>
public abstract class OrgPageBase : OrganizationPageBase
{
    /// <summary>The org used last in this session (the menu links carry no org).</summary>
    internal static Guid? LastOrg { get; set; }

    [SupplyParameterFromQuery(Name = "org")] public Guid? OrgId { get; set; }

    protected IReadOnlyList<OrgDto>? Orgs { get; private set; }
    protected OrgDto? Org { get; private set; }
    protected OrgRightsView Rights { get; set; } = OrgRightsView.None;

    /// <summary>The page's key in the org bar ("units", "people", ...).</summary>
    protected abstract string PageKey { get; }

    private Guid? _loaded;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await RunAsync(async () => Orgs = await Api.ListOrgsAsync(), toast: false);
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Orgs is null) return;
        var chosen = Orgs.FirstOrDefault(o => o.Id == (OrgId ?? LastOrg)) ?? (Orgs.Count == 1 ? Orgs[0] : null);
        if (chosen?.Id == _loaded && _loaded is not null) return;
        Org = chosen;
        _loaded = chosen?.Id;
        Rights = OrgRightsView.None;
        if (chosen is null) return;
        LastOrg = chosen.Id;
        await LoadOrgAsync(chosen);
    }

    /// <summary>Loads what the page shows about <paramref name="org"/>.</summary>
    protected abstract Task LoadOrgAsync(OrgDto org);

    /// <summary>Works out <see cref="Rights"/> from the org's data (persons, memberships, positions, units).</summary>
    protected async Task LoadRightsAsync(OrgDto org, IReadOnlyList<OrgUnitDto>? units = null, IReadOnlyList<PersonDto>? persons = null,
        IReadOnlyList<MembershipDto>? memberships = null, IReadOnlyList<PositionDto>? positions = null)
    {
        if (Caller.IsPlatformAdmin || org.Admins.Any(a => Caller.Is(a.Issuer, a.Subject)))
        {
            Rights = OrgRightsView.For(Caller, org, [], [], [], [], DateTimeOffset.UtcNow);
            return;
        }
        try
        {
            persons ??= await Api.ListPersonsAsync(org.Id);
            var me = persons.FirstOrDefault(p => Caller.Is(p.Issuer, p.Subject));
            if (me is null) { Rights = OrgRightsView.None; return; }
            memberships ??= await Api.ListMembershipsAsync(org.Id, personId: me.Id);
            positions ??= await Api.ListPositionsAsync(org.Id);
            units ??= await Api.ListUnitsAsync(org.Id);
            Rights = OrgRightsView.For(Caller, org, persons, memberships, positions, units, DateTimeOffset.UtcNow);
        }
        catch (OrganizationApiException)
        {
            Rights = OrgRightsView.None; // the buttons stay hidden; the API decides anyway
        }
    }

    /// <summary>A page of this module for the current org.</summary>
    protected string Href(string page) => OrgLinks.Href(page, Org?.Id);
}

/// <summary>Links between the module's pages.</summary>
public static class OrgLinks
{
    public static readonly (string Key, string Title)[] Pages =
    [
        ("units", "Units"),
        ("people", "People"),
        ("positions", "Positions"),
        ("seeds", "Tenant seeds"),
        ("links", "Tenant links"),
        ("settings", "Settings"),
    ];

    public static string Href(string page, Guid? orgId) =>
        orgId is { } id ? $"organization/{page}?org={id}" : $"organization/{page}";
}
