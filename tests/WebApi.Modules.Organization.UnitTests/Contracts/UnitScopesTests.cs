using soft.square.net.Organization.Contracts;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.Contracts;

public class UnitScopesTests
{
    private static readonly Guid A = Guid.Parse("0193a000-0000-7000-8000-000000000001");
    private static readonly Guid B = Guid.Parse("0193a000-0000-7000-8000-000000000002");
    private static readonly Guid C = Guid.Parse("0193a000-0000-7000-8000-000000000003");

    [Fact]
    public void APathGivesTheScopeOfEveryAncestor()
    {
        var path = UnitPaths.Child(UnitPaths.Child(UnitPaths.Root(A), B), C);

        path.ShouldBe("/0193a000000070008000000000000001/0193a000000070008000000000000002/0193a000000070008000000000000003");
        UnitScopes.For(path).ShouldBe(["unit:0193a000000070008000000000000001", "unit:0193a000000070008000000000000002", "unit:0193a000000070008000000000000003"]);
        UnitScopes.Of(C).ShouldBe("unit:0193a000000070008000000000000003");
        UnitScopes.For(null).ShouldBeEmpty();
    }

    [Fact]
    public void SubtreesArePathPrefixes()
    {
        var ab = UnitPaths.Child(UnitPaths.Root(A), B);
        var abc = UnitPaths.Child(ab, C);

        UnitPaths.IsSelfOrBelow(abc, ab).ShouldBeTrue();
        UnitPaths.IsSelfOrBelow(ab, ab).ShouldBeTrue();
        UnitPaths.IsSelfOrBelow(ab, abc).ShouldBeFalse();
        UnitPaths.IsSelfOrBelow(ab + "x", ab).ShouldBeFalse();
    }

    [Fact]
    public void RebaseMovesASubtree()
    {
        var oldPrefix = UnitPaths.Child(UnitPaths.Root(A), B);
        var newPrefix = UnitPaths.Child(UnitPaths.Child(UnitPaths.Root(A), C), B);

        UnitPaths.Rebase(UnitPaths.Child(oldPrefix, C), oldPrefix, newPrefix).ShouldBe(UnitPaths.Child(newPrefix, C));
        Should.Throw<ArgumentException>(() => UnitPaths.Rebase(UnitPaths.Root(C), oldPrefix, newPrefix));
    }

    [Fact]
    public void EventTypesAreValidIntegrationTypes() =>
        OrgEvents.All.ShouldAllBe(t => soft.square.net.WebApi.Contracts.Messaging.IntegrationTypes.IsValidType(t)
            && soft.square.net.WebApi.Contracts.Messaging.IntegrationTypes.Matches(OrgEvents.Any, t));
}
