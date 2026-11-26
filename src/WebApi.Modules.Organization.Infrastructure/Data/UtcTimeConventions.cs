using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using soft.square.net.WebApi.Modules.Organization.Core;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;

/// <summary>
/// Writes every <see cref="DateTimeOffset"/> of a model as UTC (see <see cref="UtcTimes"/>), in the values the context
/// saves and in the parameters of its queries: PostgreSQL's <c>timestamptz</c> refuses any other offset. The column
/// types do not change (a DateTimeOffset-to-DateTimeOffset converter), so the migrations do not either; on MySQL the
/// platform's own UTC converter replaces this one.
/// </summary>
public static class UtcTimeConventions
{
    public static void StoreDateTimeOffsetsAsUtc(this ModelConfigurationBuilder configuration)
    {
        configuration.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>(); // DateTimeOffset? too
    }
}

/// <summary>A <see cref="DateTimeOffset"/> to the same instant at offset zero; read back as stored.</summary>
public sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUtc(), v => v);
