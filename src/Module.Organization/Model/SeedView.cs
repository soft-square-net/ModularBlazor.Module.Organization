using soft.square.net.WebApi.Modules.Organization.Client;

namespace soft.square.net.Module.Organization.Model;

/// <summary>How the seeds page shows a seed's state: the words (keys of the module's texts) and a tone for the chip.</summary>
public static class SeedView
{
    /// <summary>"success", "warning", "error" or "info".</summary>
    public static string Tone(SeedState state) => state switch
    {
        SeedState.Provisioned => "success",
        SeedState.Adjusted => "warning",
        SeedState.Rejected or SeedState.Failed => "error",
        _ => "info",
    };

    public static string Text(SeedState state) => state switch
    {
        SeedState.Requested => "Requested",
        SeedState.Provisioning => "Provisioning",
        SeedState.Provisioned => "Provisioned",
        SeedState.Adjusted => "Adjusted",
        SeedState.Rejected => "Rejected",
        SeedState.Failed => "Failed",
        _ => state.ToString(),
    };

    /// <summary>The host has not reported on the seed's current version yet (it was edited since its last report).</summary>
    public static bool Pending(TenantSeedDto seed) => seed.ReportedVersion is not { } reported || reported < seed.Version;

    /// <summary>The host placed the tenant somewhere other than requested.</summary>
    public static bool Moved(TenantSeedDto seed) =>
        seed.ActualPlacement is { } actual
        && (!string.Equals(actual.DatabaseServer, seed.DatabaseServer, StringComparison.Ordinal)
            || !string.Equals(actual.DatabaseName, seed.DatabaseName, StringComparison.Ordinal));

    /// <summary>The status history, newest first.</summary>
    public static IReadOnlyList<SeedStatusEntryDto> History(TenantSeedDto seed) =>
        [.. (seed.History ?? []).OrderByDescending(h => h.At).ThenByDescending(h => h.Version)];

    public static string Placement(PlacementDto? placement) =>
        placement is null ? "" : $"{placement.DatabaseServer} / {placement.DatabaseName}";
}

/// <summary>A tenant link's states (the TenantLink skeleton): words, tones and where each one may go next.</summary>
public static class LinkView
{
    public static readonly TenantLinkState[] States = [TenantLinkState.Requested, TenantLinkState.Active, TenantLinkState.Declined, TenantLinkState.Revoked];

    public static string Text(TenantLinkState state) => state switch
    {
        TenantLinkState.Requested => "Requested",
        TenantLinkState.Active => "Active",
        TenantLinkState.Declined => "Declined",
        TenantLinkState.Revoked => "Revoked",
        _ => state.ToString(),
    };

    public static string Tone(TenantLinkState state) => state switch
    {
        TenantLinkState.Active => "success",
        TenantLinkState.Declined or TenantLinkState.Revoked => "error",
        _ => "info",
    };

    /// <summary>Requested → Active | Declined; Active → Revoked; the others are final.</summary>
    public static IReadOnlyList<TenantLinkState> Next(TenantLinkState state) => state switch
    {
        TenantLinkState.Requested => [TenantLinkState.Active, TenantLinkState.Declined],
        TenantLinkState.Active => [TenantLinkState.Revoked],
        _ => [],
    };
}
