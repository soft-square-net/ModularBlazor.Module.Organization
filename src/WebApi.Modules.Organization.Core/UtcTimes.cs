namespace soft.square.net.WebApi.Modules.Organization.Core;

/// <summary>
/// Every time the module stores is UTC (offset zero). PostgreSQL's <c>timestamptz</c> refuses a
/// <see cref="DateTimeOffset"/> with another offset, so a client sending its local time (<c>+03:00</c>) would fail
/// the write; the instant is kept and only the offset changes. The aggregates normalize what callers pass in, and
/// both DbContexts normalize every <see cref="DateTimeOffset"/> property again on the way to the database.
/// </summary>
public static class UtcTimes
{
    public static DateTimeOffset ToUtc(this DateTimeOffset value) => value.Offset == TimeSpan.Zero ? value : value.ToUniversalTime();

    public static DateTimeOffset? ToUtc(this DateTimeOffset? value) => value?.ToUtc();
}
