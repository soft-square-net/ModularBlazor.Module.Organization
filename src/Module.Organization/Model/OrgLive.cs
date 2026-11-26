namespace soft.square.net.Module.Organization.Model;

/// <summary>
/// The live topic of an org: <c>organization:org:{orgId}</c>, in the platform's module topic form
/// <c>{moduleId}:{kind}:{key}</c> (the plan's "org:{orgId}" under the module's id). The pages subscribe to it and reload
/// when a message comes; a deployment whose Organization module does not declare the kind refuses the topic, and the
/// pages keep their Refresh button.
/// </summary>
public static class OrgLive
{
    public const string Module = "organization";
    public const string Kind = "org";

    public static string Topic(Guid orgId) => $"{Module}:{Kind}:{orgId:N}";
}
