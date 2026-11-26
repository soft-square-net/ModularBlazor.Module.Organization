using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace soft.square.net.Organization.Contracts;

/// <summary>
/// A unit as a deployment's replica has it (the tenant's view of the org tree). <see cref="InTenant"/>: the unit is
/// the tenant's root unit or below it; otherwise it is one of the root's read-only ancestors.
/// </summary>
public sealed record DirectoryUnitDto(
    Guid Id, Guid OrgId, Guid? ParentId, string TypeKey, string Path, string NameEn, string NameAr, IReadOnlyList<string> Tags,
    string? TimeZone, IReadOnlyList<string> Categories, TenantBindingKind Binding, string? SeedId, int Version, bool InTenant);

/// <summary>A membership of a unit of the tenant, with the person's identity (token issuer and subject).</summary>
public sealed record DirectoryMemberDto(
    Guid MembershipId, Guid PersonId, string Issuer, string Subject, string? DisplayName, Guid UnitId, Guid? PositionId, string? PositionKey,
    IReadOnlyList<string> Permissions, DateTimeOffset From, DateTimeOffset? To, bool Primary);

/// <summary>
/// The current tenant's slice of its org tree, read from the deployment's replica (never from the central service):
/// the units of the tenant (its root unit and everything below it) and the root's ancestors, read-only. It keeps
/// answering while the central service is down. Unknown units: <c>null</c> or empty lists.
/// <para>Inside the Organization module it reads the replica directly. Other modules use <see cref="HttpUnitDirectory"/>
/// over the local read API (<see cref="LocalRoutes"/>): no type is shared between modules in one process.</para>
/// </summary>
public interface IUnitDirectory
{
    /// <summary>The tenant's units and the root's ancestors, by path (root first).</summary>
    Task<IReadOnlyList<DirectoryUnitDto>> ListAsync(CancellationToken ct = default);

    Task<DirectoryUnitDto?> GetAsync(Guid unitId, CancellationToken ct = default);

    /// <summary>The unit's ancestors, root first, without the unit itself.</summary>
    Task<IReadOnlyList<DirectoryUnitDto>> AncestorsAsync(Guid unitId, CancellationToken ct = default);

    /// <summary>
    /// The Content post categories of a unit: its own when its type holds categories (a Section); otherwise those of the
    /// tenant's units below it, each once. Empty for an unknown unit.
    /// </summary>
    Task<IReadOnlyList<string>> CategoriesOfAsync(Guid unitId, CancellationToken ct = default);

    /// <summary>The memberships of one unit of the tenant (ended ones included; see <see cref="DirectoryMemberDto.To"/>).</summary>
    Task<IReadOnlyList<DirectoryMemberDto>> MembersOfAsync(Guid unitId, CancellationToken ct = default);
}

/// <summary>The local read API of a deployment (relative to the module route <c>/api/organization</c>): tenant-scoped, from the replica.</summary>
public static class LocalRoutes
{
    public const string Prefix = "local";
    public const string Units = Prefix + "/units";

    public static string Unit(Guid id) => $"{Units}/{id}";
    public static string Ancestors(Guid id) => $"{Units}/{id}/ancestors";
    public static string Categories(Guid id) => $"{Units}/{id}/categories";
    public static string Members(Guid id) => $"{Units}/{id}/members";
}

/// <summary>
/// <see cref="IUnitDirectory"/> over the local read API, for other modules and services of the deployment. Give it an
/// <see cref="HttpClient"/> whose base address ends with the module route (<c>https://gateway/api/organization/</c>)
/// and that sends the tenant (<c>X-Tenant</c> or the host) and a signed-in caller's token.
/// </summary>
public sealed class HttpUnitDirectory(HttpClient http) : IUnitDirectory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DirectoryUnitDto>> ListAsync(CancellationToken ct = default) =>
        await GetAsync<List<DirectoryUnitDto>>(LocalRoutes.Units, ct) ?? [];

    public Task<DirectoryUnitDto?> GetAsync(Guid unitId, CancellationToken ct = default) => GetAsync<DirectoryUnitDto>(LocalRoutes.Unit(unitId), ct);

    public async Task<IReadOnlyList<DirectoryUnitDto>> AncestorsAsync(Guid unitId, CancellationToken ct = default) =>
        await GetAsync<List<DirectoryUnitDto>>(LocalRoutes.Ancestors(unitId), ct) ?? [];

    public async Task<IReadOnlyList<string>> CategoriesOfAsync(Guid unitId, CancellationToken ct = default) =>
        await GetAsync<List<string>>(LocalRoutes.Categories(unitId), ct) ?? [];

    public async Task<IReadOnlyList<DirectoryMemberDto>> MembersOfAsync(Guid unitId, CancellationToken ct = default) =>
        await GetAsync<List<DirectoryMemberDto>>(LocalRoutes.Members(unitId), ct) ?? [];

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(path, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false);
    }
}
