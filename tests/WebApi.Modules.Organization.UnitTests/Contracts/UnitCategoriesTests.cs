using soft.square.net.Organization.Contracts;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.Contracts;

public class UnitCategoriesTests
{
    [Fact]
    public void TheWildcardCoversEveryCategory()
    {
        UnitCategories.Covers(["*"], "sports").ShouldBeTrue();
        UnitCategories.Covers(["local", "sports"], "sports").ShouldBeTrue();
        UnitCategories.Covers(["local"], "sports").ShouldBeFalse();
        UnitCategories.Covers([], "sports").ShouldBeFalse();
        UnitCategories.IsAll(["*"]).ShouldBeTrue();
        UnitCategories.IsAll(["local"]).ShouldBeFalse();
    }

    [Fact]
    public void AUnionIsTheWildcardWhenAnyListHoldsIt()
    {
        UnitCategories.Union([["local", "sports"], ["sports", "arts"]]).ShouldBe(["local", "sports", "arts"]);
        UnitCategories.Union([["local"], ["*"], ["arts"]]).ShouldBe(["*"]);
        UnitCategories.Union([]).ShouldBeEmpty();
    }
}
