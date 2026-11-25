using System.Text.RegularExpressions;

namespace soft.square.net.WebApi.Modules.Organization.Core;

/// <summary>Field rules shared by the aggregates. Each throws <see cref="DomainException"/> with a message for the caller.</summary>
public static partial class Rules
{
    public const int KeyMaxLength = 40;
    public const int NameMaxLength = 200;
    public const int TextMaxLength = 200;
    public const int ListMaxItems = 50;
    public const int ItemMaxLength = 100;
    public const int IdentityMaxLength = 256;
    public const int SubjectMaxLength = 128;

    /// <summary>A key: lowercase letters, digits and '-', starting with a letter or digit, at most 40 characters.</summary>
    public static string Key(string? key, string what)
    {
        var value = key?.Trim() ?? "";
        if (!KeyText().IsMatch(value))
            throw new DomainException($"{what} must be lowercase letters, digits and '-' (at most {KeyMaxLength} characters).");
        return value;
    }

    /// <summary>English and Arabic names; at least one is needed, and a missing one copies the other.</summary>
    public static (string En, string Ar) Names(string? en, string? ar, string what)
    {
        var english = en?.Trim() ?? "";
        var arabic = ar?.Trim() ?? "";
        if (english.Length == 0 && arabic.Length == 0) throw new DomainException($"{what} needs a name.");
        if (english.Length > NameMaxLength || arabic.Length > NameMaxLength)
            throw new DomainException($"{what}'s names are at most {NameMaxLength} characters.");
        return (english.Length == 0 ? arabic : english, arabic.Length == 0 ? english : arabic);
    }

    /// <summary>Required text, trimmed, bounded.</summary>
    public static string Required(string? text, string what, int max = TextMaxLength)
    {
        var value = text?.Trim() ?? "";
        if (value.Length == 0) throw new DomainException($"{what} is required.");
        if (value.Length > max) throw new DomainException($"{what} is at most {max} characters.");
        return value;
    }

    /// <summary>Optional text: null when blank, otherwise trimmed and bounded.</summary>
    public static string? Optional(string? text, string what, int max = TextMaxLength)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > max) throw new DomainException($"{what} is at most {max} characters.");
        return value;
    }

    /// <summary>A list of short texts: trimmed, blanks dropped, duplicates dropped (ordinal), bounded.</summary>
    public static List<string> List(IEnumerable<string>? items, string what, bool lowercase = false)
    {
        var values = (items ?? [])
            .Select(i => i?.Trim() ?? "")
            .Where(i => i.Length > 0)
            .Select(i => lowercase ? i.ToLowerInvariant() : i)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (values.Count > ListMaxItems) throw new DomainException($"{what}: at most {ListMaxItems} items.");
        if (values.Any(v => v.Length > ItemMaxLength)) throw new DomainException($"{what}: each item is at most {ItemMaxLength} characters.");
        return values;
    }

    /// <summary>A permission name from the platform's catalog: lowercase words separated by dots (<c>shifts.manage</c>).</summary>
    public static List<string> Permissions(IEnumerable<string>? permissions)
    {
        var values = List(permissions, "Permissions");
        var bad = values.FirstOrDefault(p => !PermissionText().IsMatch(p));
        if (bad is not null) throw new DomainException($"'{bad}' is not a permission name (lowercase words separated by dots, e.g. shifts.manage).");
        return values;
    }

    /// <summary>An IANA (or Windows) time zone id the server knows, or null.</summary>
    public static string? TimeZone(string? id)
    {
        var value = id?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(value, out _)) throw new DomainException($"'{value}' is not a known time zone.");
        return value;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    private static partial Regex KeyText();

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]*(\\.[a-z0-9][a-z0-9_-]*)+$")]
    private static partial Regex PermissionText();
}
