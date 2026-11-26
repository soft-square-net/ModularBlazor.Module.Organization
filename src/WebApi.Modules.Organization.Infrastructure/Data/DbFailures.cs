using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace soft.square.net.WebApi.Modules.Organization.Infrastructure.Data;

/// <summary>Why the database refused a save, as far as the module tells callers.</summary>
public enum DbFailure
{
    /// <summary>Anything else: a bad value, a broken connection, a bug. Not the caller's conflict.</summary>
    Other,

    /// <summary>A unique key or primary key already holds the value (the same key, a second primary membership).</summary>
    Duplicate,

    /// <summary>A row the change refers to is gone, or a row still in use was removed.</summary>
    MissingReference
}

/// <summary>
/// Reads a <see cref="DbUpdateException"/> on any of the module's engines without referencing their providers: the
/// driver's <see cref="DbException"/> carries a standard SQLSTATE (PostgreSQL) or a vendor error number (SQL Server,
/// MySQL, Oracle, SQLite), read by name.
/// </summary>
public static class DbFailures
{
    public static DbFailure Classify(DbUpdateException exception)
    {
        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is DbException db && Classify(db) is var failure and not DbFailure.Other) return failure;
        return DbFailure.Other;
    }

    public static DbFailure Classify(DbException exception)
    {
        // PostgreSQL (Npgsql): the SQLSTATE classes. MySQL's 23000 is the whole class, so its number decides below.
        switch (exception.SqlState)
        {
            case "23505": return DbFailure.Duplicate;
            case "23503": return DbFailure.MissingReference;
        }

        var name = exception.GetType().FullName ?? "";
        if (name.StartsWith("Microsoft.Data.Sqlite.", StringComparison.Ordinal))
            return Number(exception, "SqliteExtendedErrorCode") switch
            {
                2067 or 1555 => DbFailure.Duplicate, // SQLITE_CONSTRAINT_UNIQUE, SQLITE_CONSTRAINT_PRIMARYKEY
                787 => DbFailure.MissingReference,   // SQLITE_CONSTRAINT_FOREIGNKEY
                _ => DbFailure.Other
            };
        return (name, Number(exception, "Number")) switch
        {
            (_, null) => DbFailure.Other,
            ({ } n, 2601 or 2627) when n.StartsWith("Microsoft.Data.SqlClient.", StringComparison.Ordinal) => DbFailure.Duplicate,
            ({ } n, 547) when n.StartsWith("Microsoft.Data.SqlClient.", StringComparison.Ordinal) => DbFailure.MissingReference,
            ({ } n, 1062) when n.StartsWith("MySqlConnector.", StringComparison.Ordinal) => DbFailure.Duplicate,
            ({ } n, 1451 or 1452) when n.StartsWith("MySqlConnector.", StringComparison.Ordinal) => DbFailure.MissingReference,
            ({ } n, 1) when n.StartsWith("Oracle.", StringComparison.Ordinal) => DbFailure.Duplicate,                  // ORA-00001
            ({ } n, 2291 or 2292) when n.StartsWith("Oracle.", StringComparison.Ordinal) => DbFailure.MissingReference, // ORA-02291/02292
            _ => DbFailure.Other
        };
    }

    private static int? Number(DbException exception, string property) =>
        exception.GetType().GetProperty(property)?.GetValue(exception) switch
        {
            int i => i,
            uint u => (int)u,
            Enum e => Convert.ToInt32(e, System.Globalization.CultureInfo.InvariantCulture),
            _ => null
        };
}
