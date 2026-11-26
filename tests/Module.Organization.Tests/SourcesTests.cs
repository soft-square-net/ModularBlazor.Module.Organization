namespace soft.square.net.Module.Organization.Tests;

/// <summary>
/// Where the pages get their lists from: the platform's permission catalog, Content's post categories and the setting
/// Organization:BaseUrl.
/// </summary>
public class SourcesTests
{
    private const string Catalog = """
        [
          { "moduleId": "shifts", "moduleName": "Shifts", "permissions": [
              { "name": "shifts.manage", "description": "Plan and publish shifts." },
              { "name": "shifts.view" }, { "name": "shifts.view" } ] },
          { "moduleId": "empty", "moduleName": "Empty", "permissions": [] },
          { "moduleId": "organization", "moduleName": "", "permissions": [ { "name": "org.units.manage" }, { "name": "" } ] }
        ]
        """;

    [Fact]
    public void The_catalog_groups_permissions_by_module_and_leaves_out_empty_ones()
    {
        var catalog = PermissionCatalog.Parse(Catalog);

        catalog.Groups.Select(g => g.ModuleName).ShouldBe(["organization", "Shifts"]); // no name: the module id
        catalog.Groups.Single(g => g.ModuleId == "shifts").Permissions.Select(p => p.Name).ShouldBe(["shifts.manage", "shifts.view"]);
        catalog.Describe("shifts.manage").ShouldBe("Plan and publish shifts.");
        catalog.Contains("org.units.manage").ShouldBeTrue();
        catalog.Unknown(["shifts.manage", "newsroom.publish", "newsroom.publish"]).ShouldBe(["newsroom.publish"]);
        PermissionCatalog.Empty.IsEmpty.ShouldBeTrue();
    }

    [Theory]
    [InlineData("shifts.manage", true)]
    [InlineData("org.units.manage", true)]
    [InlineData("content:post.edit_2", true)]
    [InlineData("Shifts.Manage", false)]
    [InlineData("shifts manage", false)]
    [InlineData(".shifts", false)]
    [InlineData("shifts.", false)]
    [InlineData("", false)]
    public void Typed_permission_names_are_lower_case_and_dotted(string name, bool valid)
    {
        PermissionNames.IsValid(name).ShouldBe(valid);
    }

    [Fact]
    public void Content_categories_come_by_press_then_name_and_unknown_saved_ids_are_kept()
    {
        var categories = ContentCategories.Parse("""
            [ { "id": "sports", "pressId": "daily", "name": "Sports" }, { "id": "local", "pressId": "daily", "name": "Local" },
              { "id": "arts", "pressId": "weekly", "name": "" }, { "id": "", "pressId": "x", "name": "nothing" } ]
            """);

        categories.Select(c => c.Id).ShouldBe(["local", "sports", "arts"]);
        categories.Last().Name.ShouldBe("arts"); // no name: the id
        ContentCategories.Unknown(["local", "gone"], categories).ShouldBe(["gone"]);
    }

    [Fact]
    public void Toggling_a_category_keeps_the_order_picked()
    {
        var picked = ContentCategories.Toggle([], "local");
        picked = ContentCategories.Toggle(picked, "sports");
        picked.ShouldBe(["local", "sports"]);
        ContentCategories.Toggle(picked, "local").ShouldBe(["sports"]);
    }

    [Fact]
    public void All_categories_stands_alone()
    {
        var all = ContentCategories.Toggle(["local", "sports"], ContentCategories.All);
        all.ShouldBe(["*"]);
        ContentCategories.IsAll(all).ShouldBeTrue();
        ContentCategories.Toggle(all, "local").ShouldBe(["local"]);
        ContentCategories.Toggle(all, ContentCategories.All).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_base_url_uses_the_gateway(string? setting)
    {
        OrganizationEndpoint.From(setting).ShouldBe(OrganizationEndpoint.Gateway);
        OrganizationEndpoint.Gateway.UsesGateway.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://org.example.com", "https://org.example.com/")]
    [InlineData("https://org.example.com/central", "https://org.example.com/central/")]
    [InlineData(" http://localhost:6080/ ", "http://localhost:6080/")]
    public void An_absolute_base_url_ends_with_a_slash_so_the_relative_routes_go_under_it(string setting, string expected)
    {
        var endpoint = OrganizationEndpoint.From(setting);

        endpoint.UsesGateway.ShouldBeFalse();
        endpoint.Problem.ShouldBeNull();
        endpoint.BaseAddress!.ToString().ShouldBe(expected);
        new Uri(endpoint.BaseAddress, "api/organization/orgs").ToString().ShouldBe(expected + "api/organization/orgs");
    }

    [Theory]
    [InlineData("/api/organization")]
    [InlineData("ftp://org.example.com/")]
    [InlineData("https://org.example.com/?x=1")]
    public void A_bad_base_url_falls_back_to_the_gateway_and_says_why(string setting)
    {
        var endpoint = OrganizationEndpoint.From(setting);

        endpoint.UsesGateway.ShouldBeTrue();
        endpoint.Problem.ShouldNotBeNull();
        endpoint.Problem.ShouldContain(OrganizationEndpoint.SettingKey);
    }

    [Fact]
    public void The_live_topic_is_the_modules_org_kind()
    {
        OrgLive.Topic(Samples.OrgId).ShouldBe("organization:org:000000000000000000000000000000a1");
    }
}
