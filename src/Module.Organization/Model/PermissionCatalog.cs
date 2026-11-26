using System.Text.Json;
using System.Text.Json.Serialization;

namespace soft.square.net.Module.Organization.Model;

/// <summary>One permission of the platform's catalog.</summary>
public sealed record CatalogPermission(string Name, string? Description);

/// <summary>A module's permissions in the catalog (the platform's own come as module "platform").</summary>
public sealed record CatalogGroup(string ModuleId, string ModuleName, IReadOnlyList<CatalogPermission> Permissions);

/// <summary>
/// The platform's permission catalog (<c>GET api/identity/permissions</c>: every installed module's declared permissions,
/// the identity core module's ModulePermissionSet list), which a position's permissions are picked from. Reading it needs
/// <c>access.grants.manage</c> in the current tenant; without it the picker falls back to typed names.
/// </summary>
public sealed class PermissionCatalog
{
    public const string Url = "api/identity/permissions";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private PermissionCatalog(IReadOnlyList<CatalogGroup> groups) => Groups = groups;

    public static PermissionCatalog Empty { get; } = new([]);

    public IReadOnlyList<CatalogGroup> Groups { get; }

    public bool IsEmpty => Groups.Count == 0;

    public bool Contains(string permission) => Groups.Any(g => g.Permissions.Any(p => p.Name == permission));

    public string? Describe(string permission) =>
        Groups.SelectMany(g => g.Permissions).FirstOrDefault(p => p.Name == permission)?.Description;

    /// <summary>The catalog from the endpoint's JSON; groups and permissions sorted, empty groups left out.</summary>
    public static PermissionCatalog Parse(string json)
    {
        var sets = JsonSerializer.Deserialize<List<Wire>>(json, Web) ?? [];
        var groups = sets
            .Where(s => s.Permissions is { Count: > 0 })
            .Select(s => new CatalogGroup(s.ModuleId ?? "", string.IsNullOrWhiteSpace(s.ModuleName) ? s.ModuleId ?? "" : s.ModuleName!,
                [.. s.Permissions!.Where(p => !string.IsNullOrWhiteSpace(p.Name)).DistinctBy(p => p.Name).OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new CatalogPermission(p.Name!, p.Description))]))
            .Where(g => g.Permissions.Count > 0)
            .OrderBy(g => g.ModuleName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PermissionCatalog(groups);
    }

    /// <summary>The permissions of <paramref name="selected"/> that the catalog does not know (a module not installed here).</summary>
    public IReadOnlyList<string> Unknown(IEnumerable<string> selected) => [.. selected.Where(p => !Contains(p)).Distinct()];

    private sealed class Wire
    {
        [JsonPropertyName("moduleId")] public string? ModuleId { get; set; }
        [JsonPropertyName("moduleName")] public string? ModuleName { get; set; }
        [JsonPropertyName("permissions")] public List<WirePermission>? Permissions { get; set; }
    }

    private sealed class WirePermission
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }
}

/// <summary>Permission names as people type them: lower case, dotted, no spaces.</summary>
public static class PermissionNames
{
    public static bool IsValid(string name) =>
        name.Length is > 0 and <= 200 && name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '-' or '_' or ':')
        && name[0] != '.' && name[^1] != '.';
}
