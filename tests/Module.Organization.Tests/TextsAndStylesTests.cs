using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using soft.square.net.ModularBlazor.Abstractions;

namespace soft.square.net.Module.Organization.Tests;

/// <summary>
/// Arabic and English: every text the module shows has an Arabic entry (the keys are the English texts, so English
/// needs no file), and the file ships inside the module. RTL and the theme: the stylesheet uses logical sides only and
/// theme tokens for colours, and the pages use UI.MudTail's classes, not another kit's components.
/// </summary>
public partial class TextsAndStylesTests
{
    private static readonly string ModuleDir = Path.Combine(RepoRoot(), "src", "Module.Organization");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ModularBlazor.Module.Organization.slnx"))) return dir.FullName;
        throw new InvalidOperationException("The repository root (ModularBlazor.Module.Organization.slnx) was not found.");
    }

    private static IEnumerable<string> Sources(string pattern) =>
        Directory.EnumerateFiles(ModuleDir, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static Dictionary<string, string> Arabic() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ModuleDir, "i18n", "ar.json")))!;

    /// <summary>L["..."] in the pages and components, the model's English texts, the page titles and the menu titles.</summary>
    private static IReadOnlyList<string> Keys()
    {
        var keys = new List<string>();
        foreach (var file in Sources("*.razor").Concat(Sources("*.cs")))
            keys.AddRange(LocalizedText().Matches(File.ReadAllText(file)).Select(m => Regex.Unescape(m.Groups[1].Value)));

        var tree = Samples.Tree();
        foreach (var (unit, target) in new[]
                 {
                     (Samples.Root, Samples.News), (Samples.Local, Samples.Politics), (Samples.News, Samples.Local),
                     (Samples.Local, Samples.News), (Guid.NewGuid(), Samples.News),
                 })
            keys.Add(tree.WhyNotMove(unit, target)!);
        keys.AddRange(new UnitForm().Validate(null));
        keys.AddRange(new UnitForm { TypeKey = "section", NameEn = "x", NameAr = "x" }.Validate(Samples.Types()[3]));
        keys.AddRange(new UnitForm { TypeKey = "department", NameEn = "x", NameAr = "x", Categories = ["a"] }.Validate(Samples.Types()[2]));
        keys.AddRange(Enum.GetValues<SeedState>().Select(SeedView.Text));
        keys.AddRange(Enum.GetValues<TenantLinkState>().Select(LinkView.Text));
        keys.AddRange(OrgLinks.Pages.Select(p => p.Title));

        var assembly = typeof(OrganizationPageBase).Assembly;
        keys.AddRange(assembly.GetCustomAttributes<MenuLinkAttribute>().Select(a => a.Title));
        keys.AddRange(assembly.GetCustomAttributes<MenuGroupAttribute>().Select(a => a.Title));
        keys.Add(assembly.GetCustomAttribute<ClientModuleAttribute>()!.NavTitle!);
        return [.. keys.Where(k => !string.IsNullOrEmpty(k)).Distinct()];
    }

    [GeneratedRegex(@"L\[""((?:[^""\\]|\\.)*)""")]
    private static partial Regex LocalizedText();

    [Fact]
    public void Every_text_has_an_arabic_translation()
    {
        var arabic = Arabic();
        var keys = Keys();

        keys.Count.ShouldBeGreaterThan(150);
        keys.Where(k => !arabic.ContainsKey(k)).ShouldBeEmpty();
    }

    [Fact]
    public void Translations_keep_their_placeholders()
    {
        foreach (var (key, text) in Arabic().Where(e => e.Key != "$comment"))
        {
            var expected = Placeholder().Matches(key).Select(m => m.Value).Order().ToList();
            Placeholder().Matches(text).Select(m => m.Value).Order().ToList().ShouldBe(expected, $"\"{key}\"");
            text.ShouldNotBeNullOrWhiteSpace(key);
        }
    }

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [Fact]
    public void The_arabic_file_ships_inside_the_module()
    {
        using var stream = typeof(OrganizationPageBase).Assembly.GetManifestResourceStream("i18n/ar.json");

        stream.ShouldNotBeNull();
        using var reader = new StreamReader(stream);
        JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd())!.ShouldContainKey("Organizations");
    }

    [Fact]
    public void The_stylesheet_uses_logical_sides_so_it_mirrors_in_rtl()
    {
        var css = File.ReadAllText(Path.Combine(ModuleDir, "wwwroot", "organization.css"));

        PhysicalSide().Matches(css).Select(m => m.Value).ShouldBeEmpty();
    }

    [GeneratedRegex(@"(?:(?:margin|padding|border)-(?:left|right)|(?<![-\w])(?:left|right)\s*:|text-align\s*:\s*(?:left|right)|float\s*:\s*(?:left|right))")]
    private static partial Regex PhysicalSide();

    [Fact]
    public void Colours_come_from_the_theme_tokens()
    {
        var css = File.ReadAllText(Path.Combine(ModuleDir, "wwwroot", "organization.css"));
        var razor = string.Join("\n", Sources("*.razor").Select(File.ReadAllText));

        HexColour().Matches(css + razor).Select(m => m.Value).ShouldBeEmpty();
        RawColourFunction().Matches(css + razor).Select(m => m.Value).ShouldBeEmpty();
        Regex.Matches(css, @"var\(--(?!mud-)[\w-]+").Select(m => m.Value).ShouldBeEmpty();
    }

    [GeneratedRegex(@"#[0-9a-fA-F]{3,8}\b(?![\w-]*\s*\{)")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"\b(?:rgba?|hsla?)\((?!\s*var\()")]
    private static partial Regex RawColourFunction();

    [Fact]
    public void The_pages_use_mudtail_and_no_other_component_kit()
    {
        foreach (var file in Sources("*.razor"))
        {
            var text = File.ReadAllText(file);
            Regex.Matches(text, @"<Mud(?!TailDataTable\b)\w+").Select(m => m.Value).ShouldBeEmpty(file);
            Regex.Matches(text, @"class=""[^""]*\b(?:btn|form-control|col-md-\d+)\b").Select(m => m.Value).ShouldBeEmpty(file);
        }
    }

    [Fact]
    public void Arabic_names_are_typed_right_to_left_and_english_ones_left_to_right()
    {
        foreach (var file in Sources("*.razor"))
        {
            var text = File.ReadAllText(file);
            Regex.Matches(text, @"<input[^>]*@bind=""[^""]*NameAr""[^>]*>").ShouldAllBe(m => m.Value.Contains("dir=\"rtl\""));
            Regex.Matches(text, @"<input[^>]*@bind=""[^""]*NameEn""[^>]*>").ShouldAllBe(m => m.Value.Contains("dir=\"ltr\""));
        }
    }
}
