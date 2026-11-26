using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using soft.square.net.WebApi.Data;
using soft.square.net.WebApi.Modules.Organization.Infrastructure;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases;
using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Seeds;

namespace soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data;

/// <summary>
/// One in-memory SQLite database with the central model (EnsureCreated, not the per-provider migrations: those are
/// checked with <c>dotnet ef migrations has-pending-model-changes</c>), and the module's use cases over it. Each
/// <see cref="Run{THandler,TResult}"/> is a new scope with a new context, as a new request would get, for a caller.
/// </summary>
public sealed class OrganizationDatabase : IDisposable
{
    public const string Issuer = "https://id.test";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<OrganizationDbContext> _options;

    public TestClock Clock { get; } = new();

    /// <summary>The host's live notifier, as the module's container sees it.</summary>
    public MemoryLiveNotifier Live { get; } = new();

    /// <summary>The request's tenant (null: a central request outside any tenant, where nothing is published live).</summary>
    public soft.square.net.WebApi.Contracts.Tenancy.TenantInfo? RequestTenant { get; set; }

    public OrganizationDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<OrganizationDbContext>().UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsets>().Options;
        using var context = NewContext();
        context.Database.EnsureCreated();
    }

    public OrganizationDbContext NewContext() => new(_options, new CentralDatabase("test", "test"));

    public static TestCaller PlatformAdmin { get; } = new(Issuer, "platform-admin", PlatformAdmin: true);
    public static TestCaller Connector { get; } = new(Issuer, "connector-eu", ServiceClient: true);
    public static TestCaller User(string subject) => new(Issuer, subject);

    /// <summary>Runs one handler, built from a fresh scope, as <paramref name="caller"/>.</summary>
    public async Task<TResult> Run<THandler, TResult>(TestCaller caller, Func<THandler, ValueTask<TResult>> call) where THandler : class
    {
        await using var provider = Services(caller);
        await using var scope = provider.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        return await call(handler);
    }

    private ServiceProvider Services(TestCaller caller)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewContext());
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<OrganizationDbContext>());
        services.AddScoped(typeof(SharedKernel.IRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(SharedKernel.IReadRepository<>), typeof(EfRepository<>));
        services.AddScoped<IOrgStore, EfOrgStore>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICaller>(caller);
        services.AddScoped<IOrgAccess, OrgAccess>();
        services.AddScoped<OrgFeed>();
        services.AddScoped<MembershipParts>();
        services.AddScoped<SeedAdmins>();
        services.AddSingleton(Options.Create(new OrganizationOptions { Mode = OrganizationMode.Central, MaxFeedPage = 50 }));
        services.AddSingleton<soft.square.net.WebApi.Contracts.Live.ILiveNotifier>(Live);
        if (RequestTenant is { } tenant) services.AddSingleton<soft.square.net.WebApi.Contracts.Tenancy.ITenantContext>(new FixedTenant(tenant));
        return services.BuildServiceProvider();
    }

    public void Dispose() => _connection.Dispose();
}

public sealed record TestCaller(string? Issuer, string? Subject, bool PlatformAdmin = false, bool ServiceClient = false) : ICaller
{
    public bool IsPlatformAdmin => PlatformAdmin;
    public bool IsServiceClient => ServiceClient;
}

/// <summary>A clock that stands still until a test moves it.</summary>
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => Now += by;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// SQLite cannot compare or order DateTimeOffset columns: the test database stores them as sortable longs (the real
/// engines have native types for them, so the module's model does not do this).
/// </summary>
public sealed class SqliteDateTimeOffsets(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }
}
