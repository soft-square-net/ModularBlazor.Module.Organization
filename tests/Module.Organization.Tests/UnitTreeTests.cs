using static soft.square.net.Module.Organization.Tests.Samples;

namespace soft.square.net.Module.Organization.Tests;

/// <summary>The explorer's tree: built from the flat list, and the rules it follows before asking the API.</summary>
public class UnitTreeTests
{
    [Fact]
    public void Builds_parents_before_children_whatever_the_order_of_the_list()
    {
        var tree = Tree();

        tree.Count.ShouldBe(5);
        tree.Roots.Single().Unit.Id.ShouldBe(Root);
        var news = tree.Roots[0].Children.Single();
        news.Unit.Id.ShouldBe(News);
        news.Level.ShouldBe(1);
        news.Children.Select(c => c.Unit.NameEn).ShouldBe(["Politics desk", "Sports"]); // by name
        tree.Find(Local)!.Level.ShouldBe(3);
    }

    [Fact]
    public void Lists_every_unit_depth_first_and_the_ancestry_root_first()
    {
        var tree = Tree();

        tree.All().Select(n => n.Unit.Id).ShouldBe([Root, News, Politics, Local, Sports]);
        tree.Ancestry(Local).Select(u => u.Id).ShouldBe([Root, News, Politics, Local]);
        tree.Ancestry(Guid.NewGuid()).ShouldBeEmpty();
    }

    [Fact]
    public void A_unit_whose_parent_is_missing_becomes_a_root()
    {
        var orphan = Unit(Guid.NewGuid(), Guid.NewGuid(), "department", P(Guid.NewGuid(), Guid.NewGuid()), "Orphan");

        var tree = UnitTree.Build([.. Units(), orphan], Types());

        tree.Roots.Select(r => r.Unit.NameEn).ShouldBe(["Acme", "Orphan"]);
    }

    [Fact]
    public void New_children_get_only_the_types_allowed_under_the_parent()
    {
        var tree = Tree();

        tree.ChildTypes(tree.Find(News)!.Unit).Select(t => t.Key).ShouldBe(["department"]);
        tree.ChildTypes(tree.Find(Politics)!.Unit).Select(t => t.Key).ShouldBe(["section"]);
        tree.ChildTypes(tree.Find(Local)!.Unit).ShouldBeEmpty();
    }

    [Fact]
    public void A_section_may_move_to_another_department()
    {
        Tree().WhyNotMove(Local, Sports).ShouldBeNull();
    }

    [Theory]
    [InlineData("root", "news", "The root unit cannot move.")]
    [InlineData("local", "politics", "It is already there.")]
    [InlineData("news", "local", "A unit cannot move into its own subtree.")]
    [InlineData("news", "news", "A unit cannot move into its own subtree.")]
    [InlineData("local", "news", "This type of unit cannot go under that type.")]
    [InlineData("sports", "politics", "This type of unit cannot go under that type.")]
    public void Refuses_moves_the_api_would_refuse(string unit, string target, string why)
    {
        Tree().WhyNotMove(Id(unit), Id(target)).ShouldBe(why);
    }

    [Fact]
    public void Unknown_units_cannot_move()
    {
        Tree().WhyNotMove(Guid.NewGuid(), News).ShouldBe("The unit is not in this tree any more. Reload.");
    }

    [Fact]
    public void Move_targets_are_the_allowed_parents_outside_the_subtree()
    {
        var tree = Tree();

        tree.MoveTargets(Local).Select(n => n.Unit.Id).ShouldBe([Sports]);
        tree.MoveTargets(Politics).ShouldBeEmpty(); // the only sector is its own parent
    }

    [Fact]
    public void Only_leaves_below_the_root_offer_remove()
    {
        var tree = Tree();

        tree.CanRemove(Local).ShouldBeTrue();
        tree.CanRemove(Sports).ShouldBeTrue();
        tree.CanRemove(Politics).ShouldBeFalse();
        tree.CanRemove(Root).ShouldBeFalse();
    }

    [Fact]
    public void Self_or_below_follows_the_path_not_a_text_prefix()
    {
        UnitPath.IsSelfOrBelow("/a/b", "/a").ShouldBeTrue();
        UnitPath.IsSelfOrBelow("/a", "/a").ShouldBeTrue();
        UnitPath.IsSelfOrBelow("/ab", "/a").ShouldBeFalse();
        Tree().IsSelfOrBelow(Local, News).ShouldBeTrue();
        Tree().IsSelfOrBelow(News, Local).ShouldBeFalse();
    }

    private static Guid Id(string name) => name switch
    {
        "root" => Root, "news" => News, "politics" => Politics, "local" => Local, "sports" => Sports,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };
}
