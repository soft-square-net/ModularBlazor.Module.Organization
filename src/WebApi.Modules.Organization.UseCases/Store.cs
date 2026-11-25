using System.Text.Json;
using soft.square.net.WebApi.Modules.Organization.Core.FeedAggregate;

namespace soft.square.net.WebApi.Modules.Organization.UseCases;

/// <summary>
/// The central database as a unit of work (scoped). Use cases read through the repositories (tracked), change the
/// aggregates, <see cref="Add"/> or <see cref="Remove"/> rows, append feed entries, and call <see cref="SaveAsync"/> once:
/// everything a use case changes, its feed entries included, is written in one save, or not at all.
/// </summary>
public interface IOrgStore
{
    void Add<T>(T entity) where T : class, IAggregateRoot;

    void Remove<T>(T entity) where T : class, IAggregateRoot;

    Task SaveAsync(CancellationToken ct);
}

/// <summary>
/// The org feed: each change appends its <c>org.*</c> v1 event (<see cref="OrgEvents"/>), saved with the change by
/// <see cref="IOrgStore.SaveAsync"/>.
/// </summary>
public sealed class OrgFeed(IOrgStore store, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Append(Guid orgId, string type, object data, string subject, string? unitPath = null) =>
        store.Add(FeedEntry.Create(orgId, type, subject, unitPath, JsonSerializer.Serialize(data, data.GetType(), Json), clock.GetUtcNow()));

    public static JsonElement Parse(string data) => JsonSerializer.Deserialize<JsonElement>(data, Json);
}
