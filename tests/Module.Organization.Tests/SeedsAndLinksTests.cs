namespace soft.square.net.Module.Organization.Tests;

/// <summary>How the seeds page reads a seed's state and history, and the links page the TenantLink states.</summary>
public class SeedsAndLinksTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private static TenantSeedDto Seed(long version, long? reported, SeedState status, PlacementDto? actual = null, params SeedStatusEntryDto[] history) => new()
    {
        Id = "acme-news", OrgId = Samples.OrgId, RootUnitId = Samples.News, Version = version, DeploymentId = "eu-1", TenantId = "acme-news",
        Name = "Acme News", Hosts = ["news.acme.test"], DatabaseServer = "tenant-data", DatabaseName = "acme_news", Modules = [],
        AdminPersonIds = [], Status = status, ReportedVersion = reported, ActualPlacement = actual, History = history,
    };

    private static SeedStatusEntryDto Entry(int minutes, long version, SeedState status) =>
        new() { At = At.AddMinutes(minutes), Version = version, Status = status };

    [Fact]
    public void A_seed_waits_until_the_host_reports_on_its_current_version()
    {
        SeedView.Pending(Seed(1, null, SeedState.Requested)).ShouldBeTrue();
        SeedView.Pending(Seed(2, 1, SeedState.Provisioned)).ShouldBeTrue();
        SeedView.Pending(Seed(2, 2, SeedState.Provisioned)).ShouldBeFalse();
    }

    [Fact]
    public void An_adjusted_placement_shows_as_moved()
    {
        SeedView.Moved(Seed(1, 1, SeedState.Adjusted, new PlacementDto { DatabaseServer = "tenant-data-2", DatabaseName = "acme_news" })).ShouldBeTrue();
        SeedView.Moved(Seed(1, 1, SeedState.Provisioned, new PlacementDto { DatabaseServer = "tenant-data", DatabaseName = "acme_news" })).ShouldBeFalse();
        SeedView.Moved(Seed(1, null, SeedState.Requested)).ShouldBeFalse();
        SeedView.Placement(new PlacementDto { DatabaseServer = "s", DatabaseName = "d" }).ShouldBe("s / d");
    }

    [Fact]
    public void The_history_is_newest_first()
    {
        var seed = Seed(2, 2, SeedState.Provisioned, null,
            Entry(0, 1, SeedState.Requested), Entry(5, 1, SeedState.Provisioned), Entry(10, 2, SeedState.Requested), Entry(12, 2, SeedState.Provisioned));

        SeedView.History(seed).Select(h => (h.Version, h.Status)).ShouldBe(
            [(2L, SeedState.Provisioned), (2L, SeedState.Requested), (1L, SeedState.Provisioned), (1L, SeedState.Requested)]);
    }

    [Theory]
    [InlineData(SeedState.Requested, "info")]
    [InlineData(SeedState.Provisioning, "info")]
    [InlineData(SeedState.Provisioned, "success")]
    [InlineData(SeedState.Adjusted, "warning")]
    [InlineData(SeedState.Rejected, "error")]
    [InlineData(SeedState.Failed, "error")]
    public void Every_seed_state_has_a_tone_and_words(SeedState state, string tone)
    {
        SeedView.Tone(state).ShouldBe(tone);
        SeedView.Text(state).ShouldBe(state.ToString());
    }

    [Fact]
    public void Link_states_follow_the_skeleton()
    {
        LinkView.States.ShouldBe(Enum.GetValues<TenantLinkState>());
        LinkView.Next(TenantLinkState.Requested).ShouldBe([TenantLinkState.Active, TenantLinkState.Declined]);
        LinkView.Next(TenantLinkState.Active).ShouldBe([TenantLinkState.Revoked]);
        LinkView.Next(TenantLinkState.Declined).ShouldBeEmpty();
        LinkView.Next(TenantLinkState.Revoked).ShouldBeEmpty();
        LinkView.Tone(TenantLinkState.Active).ShouldBe("success");
        LinkView.Tone(TenantLinkState.Revoked).ShouldBe("error");
        foreach (var state in LinkView.States) LinkView.Text(state).ShouldBe(state.ToString());
    }
}
