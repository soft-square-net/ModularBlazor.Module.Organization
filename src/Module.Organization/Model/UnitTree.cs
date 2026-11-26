using soft.square.net.WebApi.Modules.Organization.Client;

namespace soft.square.net.Module.Organization.Model;

/// <summary>One unit of the explorer with its children, in tree order.</summary>
public sealed class UnitNode(OrgUnitDto unit, int level)
{
    public OrgUnitDto Unit { get; } = unit;

    /// <summary>0 for the root.</summary>
    public int Level { get; } = level;

    public List<UnitNode> Children { get; } = [];

    public bool HasChildren => Children.Count > 0;
}

/// <summary>
/// An org's units as a tree, built from the flat list the API returns (GET .../units, in tree order), and the rules the
/// explorer follows before it asks the API: what may go under what (the unit types' allowed parents) and where a unit
/// may move (never into its own subtree). The API checks all of it again; this only shapes the UI.
/// </summary>
public sealed class UnitTree
{
    private readonly Dictionary<Guid, UnitNode> _nodes;
    private readonly Dictionary<string, UnitTypeDto> _types;

    private UnitTree(List<UnitNode> roots, Dictionary<Guid, UnitNode> nodes, Dictionary<string, UnitTypeDto> types)
    {
        Roots = roots;
        _nodes = nodes;
        _types = types;
    }

    public IReadOnlyList<UnitNode> Roots { get; }

    public int Count => _nodes.Count;

    public static UnitTree Build(IEnumerable<OrgUnitDto> units, IEnumerable<UnitTypeDto>? types = null)
    {
        // Parents before children: the path's depth orders them, whatever order the list came in.
        var ordered = units.OrderBy(u => Depth(u.Path)).ToList();
        var nodes = new Dictionary<Guid, UnitNode>();
        var roots = new List<UnitNode>();
        foreach (var unit in ordered)
        {
            if (unit.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent))
            {
                var node = new UnitNode(unit, parent.Level + 1);
                parent.Children.Add(node);
                nodes[unit.Id] = node;
            }
            else
            {
                var node = new UnitNode(unit, 0);
                roots.Add(node);
                nodes[unit.Id] = node;
            }
        }

        foreach (var node in nodes.Values) node.Children.Sort(Compare);
        roots.Sort(Compare);
        var byKey = (types ?? []).GroupBy(t => t.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return new UnitTree(roots, nodes, byKey);
    }

    private static int Compare(UnitNode a, UnitNode b) =>
        string.Compare(a.Unit.NameEn, b.Unit.NameEn, StringComparison.OrdinalIgnoreCase);

    public static int Depth(string? path) => string.IsNullOrEmpty(path) ? 0 : path.Count(c => c == '/');

    public UnitNode? Find(Guid id) => _nodes.GetValueOrDefault(id);

    public UnitTypeDto? TypeOf(OrgUnitDto unit) => _types.GetValueOrDefault(unit.TypeKey);

    public UnitTypeDto? Type(string key) => _types.GetValueOrDefault(key);

    /// <summary>Every unit, depth first in tree order (for pickers).</summary>
    public IEnumerable<UnitNode> All()
    {
        var stack = new Stack<UnitNode>(Roots.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--) stack.Push(node.Children[i]);
        }
    }

    /// <summary>The units from the root down to <paramref name="id"/> (itself last); empty when unknown.</summary>
    public IReadOnlyList<OrgUnitDto> Ancestry(Guid id)
    {
        var chain = new List<OrgUnitDto>();
        for (var node = Find(id); node is not null; node = node.Unit.ParentId is { } p ? Find(p) : null)
            chain.Add(node.Unit);
        chain.Reverse();
        return chain;
    }

    /// <summary>True when <paramref name="id"/> is <paramref name="ancestorId"/> or below it.</summary>
    public bool IsSelfOrBelow(Guid id, Guid ancestorId) =>
        Find(id) is { } node && Find(ancestorId) is { } ancestor && UnitPath.IsSelfOrBelow(node.Unit.Path, ancestor.Unit.Path);

    /// <summary>The types a new unit under <paramref name="parent"/> may have (in their order).</summary>
    public IReadOnlyList<UnitTypeDto> ChildTypes(OrgUnitDto parent) =>
        [.. _types.Values.Where(t => t.AllowedParents.Contains(parent.TypeKey, StringComparer.Ordinal)).OrderBy(t => t.Order).ThenBy(t => t.Key)];

    /// <summary>
    /// Why <paramref name="unitId"/> may not move under <paramref name="newParentId"/>, as an English text (a key of
    /// the module's texts), or null when it may.
    /// </summary>
    public string? WhyNotMove(Guid unitId, Guid newParentId)
    {
        if (Find(unitId) is not { } node || Find(newParentId) is not { } parent) return "The unit is not in this tree any more. Reload.";
        if (node.Unit.ParentId is null) return "The root unit cannot move.";
        if (node.Unit.ParentId == newParentId) return "It is already there.";
        if (UnitPath.IsSelfOrBelow(parent.Unit.Path, node.Unit.Path)) return "A unit cannot move into its own subtree.";
        if (TypeOf(node.Unit) is { } type && !type.AllowedParents.Contains(parent.Unit.TypeKey, StringComparer.Ordinal))
            return "This type of unit cannot go under that type.";
        return null;
    }

    /// <summary>The units <paramref name="unitId"/> may move under.</summary>
    public IEnumerable<UnitNode> MoveTargets(Guid unitId) => All().Where(n => WhyNotMove(unitId, n.Unit.Id) is null);

    /// <summary>A unit may be removed when it has no children (the API also wants no members and no tenant).</summary>
    public bool CanRemove(Guid unitId) => Find(unitId) is { Unit.ParentId: not null, HasChildren: false };
}

/// <summary>Unit paths as the API writes them: <c>/{rootId:N}/{childId:N}/...</c> (the same rule as Organization.Contracts).</summary>
public static class UnitPath
{
    public static bool IsSelfOrBelow(string path, string ancestorPath) =>
        string.Equals(path, ancestorPath, StringComparison.Ordinal)
        || path.StartsWith(ancestorPath + "/", StringComparison.Ordinal);
}
