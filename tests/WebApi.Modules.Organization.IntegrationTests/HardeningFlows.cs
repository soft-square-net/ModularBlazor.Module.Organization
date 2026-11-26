using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using soft.square.net.Organization.Contracts;
using soft.square.net.WebApi.Contracts.Tenancy;
using OrgEntity = soft.square.net.WebApi.Modules.Organization.Core.OrgAggregate.Org;
using soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;
using soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data;
using soft.square.net.WebApi.Modules.Organization.UseCases.Memberships;
using soft.square.net.WebApi.Modules.Organization.UseCases.Orgs;
using soft.square.net.WebApi.Modules.Organization.UseCases.Persons;
using Db = soft.square.net.WebApi.Modules.Organization.IntegrationTests.Data.OrganizationDatabase;

namespace soft.square.net.WebApi.Modules.Organization.IntegrationTests;

/// <summary>UTC times, the database failures the API reports, and the org's live messages after a central change.</summary>
public sealed class HardeningFlows : IDisposable
{
    private readonly Db _db = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    private async Task<OrgDto> CreateOrgAsync(string key = "acme") =>
        (await _db.Run<CreateOrgHandler, Result<OrgDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateOrgCommand(new CreateOrgRequest(key, "Acme", "أكمي")), Ct))).Value;

    private async Task<PersonDto> AddPersonAsync(Guid orgId, string subject) =>
        (await _db.Run<CreatePersonHandler, Result<PersonDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreatePersonCommand(orgId, new CreatePersonRequest(Db.Issuer, subject, subject)), Ct))).Value;

    [Fact]
    public async Task MembershipTimesInALocalOffsetAreStoredAsUtc()
    {
        var org = await CreateOrgAsync();
        var person = await AddPersonAsync(org.Id, "alice");
        var from = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.FromHours(3));
        var until = new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.FromHours(-5));

        var created = await _db.Run<CreateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateMembershipCommand(org.Id, new CreateMembershipRequest(person.Id, org.RootUnitId, null, from, until)), Ct));

        created.IsSuccess.ShouldBeTrue(string.Join("; ", created.Errors));
        created.Value.From.ShouldBe(from);
        created.Value.From.Offset.ShouldBe(TimeSpan.Zero);
        created.Value.To!.Value.Offset.ShouldBe(TimeSpan.Zero);
        await using var context = _db.NewContext();
        var stored = await context.Memberships.SingleAsync(m => m.Id == created.Value.Id, Ct);
        stored.From.ShouldBe(from);
        stored.From.Offset.ShouldBe(TimeSpan.Zero);
        stored.To.ShouldBe(until);

        var moved = from.AddDays(1).ToOffset(TimeSpan.FromHours(2));
        var updated = await _db.Run<UpdateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new UpdateMembershipCommand(org.Id, created.Value.Id, new UpdateMembershipRequest(null, moved, null, false)), Ct));
        updated.Value.From.ShouldBe(moved);
        updated.Value.From.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void TheModelConverterWritesUtcAndKeepsTheInstant()
    {
        var converter = new UtcDateTimeOffsetConverter();
        var local = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.FromHours(3));

        var stored = (DateTimeOffset)converter.ConvertToProvider(local)!;

        stored.Offset.ShouldBe(TimeSpan.Zero);
        stored.ShouldBe(local);
        stored.UtcDateTime.Hour.ShouldBe(9);
    }

    [Fact]
    public async Task ADuplicateKeyIsReportedAsADuplicate()
    {
        await CreateOrgAsync("acme");
        await using var context = _db.NewContext();
        context.Orgs.Add(OrgEntity.Create(Guid.NewGuid(), Guid.NewGuid(), "acme", "Again", null, null, _db.Clock.GetUtcNow()));

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync(Ct));

        DbFailures.Classify(failure).ShouldBe(DbFailure.Duplicate);
    }

    [Theory]
    [InlineData("23505", DbFailure.Duplicate)]      // PostgreSQL unique_violation
    [InlineData("23503", DbFailure.MissingReference)] // PostgreSQL foreign_key_violation
    [InlineData("22007", DbFailure.Other)]          // PostgreSQL invalid_datetime_format
    [InlineData(null, DbFailure.Other)]
    public void ADatabaseErrorIsClassifiedByItsSqlState(string? sqlState, DbFailure expected) =>
        DbFailures.Classify(new DbUpdateException("save failed", new StateException(sqlState))).ShouldBe(expected);

    [Fact]
    public void ARefusedValueIsNotADuplicate()
    {
        // Npgsql refuses a non-UTC DateTimeOffset for timestamptz before the command runs: no DbException at all.
        var failure = new DbUpdateException("save failed", new ArgumentException("Cannot write DateTimeOffset with Offset=03:00:00 to PostgreSQL type 'timestamp with time zone'"));

        DbFailures.Classify(failure).ShouldBe(DbFailure.Other);
    }

    [Fact]
    public async Task ACentralChangePublishesOneNudgeOnTheOrgTopicInTheRequestsTenant()
    {
        var org = await CreateOrgAsync(); // no tenant: nothing is published
        _db.Live.Sent.ShouldBeEmpty();

        _db.RequestTenant = new TenantInfo("platform", "Platform", TenantStatus.Active, [], "test", "platform");
        var person = await AddPersonAsync(org.Id, "alice");

        var sent = _db.Live.Sent.ShouldHaveSingleItem();
        sent.Topic.ShouldBe($"organization:org:{org.Id:N}");
        sent.Type.ShouldBe(OrgLiveTopic.ChangedMessage);
        sent.Sequence.ShouldBeNull();
        var payload = _db.Live.Payloads.Single().Payload;
        payload.GetProperty("type").GetString().ShouldBe("person");
        payload.GetProperty("id").GetString().ShouldBe(person.Id.ToString("N"));

        // A save that changes several items (a membership, and the person's previous primary one) is one message.
        await _db.Run<CreateMembershipHandler, Result<MembershipDto>>(Db.PlatformAdmin,
            h => h.Handle(new CreateMembershipCommand(org.Id, new CreateMembershipRequest(person.Id, org.RootUnitId, null, Primary: true)), Ct));
        _db.Live.Sent.Count.ShouldBe(2);
        _db.Live.Payloads[1].Payload.GetProperty("type").GetString().ShouldBe("membership");
    }

    [Fact]
    public void ManyChangesAreSummarizedAsTheOrg()
    {
        var orgId = Guid.NewGuid();
        OrgLiveTopic.Summarize(orgId, [new("unit", "a"), new("unit", "a")]).ShouldBe(new OrgChange("unit", "a"));
        OrgLiveTopic.Summarize(orgId, [new("unit", "a"), new("person", "b")]).ShouldBe(new OrgChange(OrgLiveTopic.ManyChanges, orgId.ToString("N")));
        OrgLiveTopic.FromSubject(OrgEvents.SeedSubject("acme-news")).ShouldBe(new OrgChange("seed", "acme-news"));
    }

    private sealed class StateException(string? sqlState) : DbException("refused")
    {
        public override string? SqlState { get; } = sqlState;
    }
}
