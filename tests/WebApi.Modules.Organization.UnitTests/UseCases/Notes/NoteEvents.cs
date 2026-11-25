using System.Text.Json;
using soft.square.net.WebApi.Contracts.Live;
using soft.square.net.WebApi.Contracts.Messaging;
using soft.square.net.WebApi.Modules.Organization.UseCases;
using soft.square.net.WebApi.Modules.Organization.UseCases.Notes.Events;

namespace soft.square.net.WebApi.Modules.Organization.UnitTests.UseCases.Notes;

/// <summary>The module's integration events and live topics: names, patterns and the example handler.</summary>
public class NoteEvents
{
    private readonly ILiveNotifier _live = Substitute.For<ILiveNotifier>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IntegrationEnvelope Envelope(string type, object data) =>
        new(Guid.NewGuid(), type, "acme", "note:7", DateTimeOffset.UtcNow, "corr", "organization",
            JsonSerializer.SerializeToElement(data, IntegrationEnvelope.JsonOptions));

    [Fact]
    public void TheNamesAreValidAndTheManifestPatternsMatchTheEvents()
    {
        IntegrationTypes.IsValidType(OrganizationEvents.NoteEdited).ShouldBeTrue();
        OrganizationEvents.Subscriptions.ShouldAllBe(p => IntegrationTypes.IsValidPattern(p));
        OrganizationEvents.Subscriptions.ShouldContain(p => IntegrationTypes.Matches(p, OrganizationEvents.NoteEdited));
        OrganizationEvents.NoteTopic(7).ShouldBe("organization:note:7");
        OrganizationEvents.LiveTopicKinds.ShouldContain(new LiveTopic("note", OrganizationPermissions.Read));
    }

    [Fact]
    public async Task AnEditedNoteIsPushedToItsLiveTopic()
    {
        await new NoteEditedLiveHandler(_live).HandleAsync(Envelope(OrganizationEvents.NoteEdited, new NoteEdited(7, 3, "Title")), Ct);

        await _live.Received(1).PublishAsync("organization:note:7", NoteEditedLiveHandler.MessageType,
            Arg.Is<JsonElement?>(p => p!.Value.GetProperty("version").GetInt32() == 3), 3, Ct);
    }

    [Fact]
    public async Task OtherNoteEventsAreIgnored()
    {
        await new NoteEditedLiveHandler(_live).HandleAsync(Envelope("organization.note-archived.v1", new { id = 7 }), Ct);

        await _live.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!, default, default, default);
    }
}
