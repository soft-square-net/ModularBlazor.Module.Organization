using static soft.square.net.Module.Organization.Tests.Samples;

namespace soft.square.net.Module.Organization.Tests;

/// <summary>The unit editor's form: names, lists, and a Section's post categories (at least one, Sections only).</summary>
public class UnitFormTests
{
    private static readonly UnitTypeDto Section = Type("section", true, 30, "department");
    private static readonly UnitTypeDto Department = Type("department", false, 20, "sector");

    [Fact]
    public void A_section_without_categories_is_refused_before_it_is_sent()
    {
        var form = new UnitForm { ParentId = Politics, TypeKey = "section", NameEn = "Local", NameAr = "محلي" };

        form.Validate(Section).ShouldBe(["A section needs at least one post category."]);

        form.Categories = ["  ", ""];
        form.Validate(Section).ShouldBe(["A section needs at least one post category."]);

        form.Categories = ["local"];
        form.Validate(Section).ShouldBeEmpty();
    }

    [Fact]
    public void A_section_may_hold_all_categories_but_only_alone()
    {
        var form = new UnitForm { ParentId = Politics, TypeKey = "section", NameEn = "Multimedia", NameAr = "الوسائط", Categories = ["*"] };
        form.Validate(Section).ShouldBeEmpty();
        form.ToCreate(Section).Categories.ShouldBe(["*"]);

        form.Categories = ["*", "local"];
        form.Validate(Section).ShouldBe(["All categories can't be combined with other categories."]);
    }

    [Fact]
    public void Only_sections_hold_categories()
    {
        var form = new UnitForm { ParentId = News, TypeKey = "department", NameEn = "Sports", NameAr = "رياضة", Categories = ["sports"] };

        form.Validate(Department).ShouldBe(["Only sections hold post categories."]);
    }

    [Fact]
    public void Both_names_and_the_type_are_needed()
    {
        new UnitForm().Validate(null).ShouldBe(["Choose the unit's type.", "Enter the English name.", "Enter the Arabic name."]);
    }

    [Fact]
    public void A_new_section_sends_its_trimmed_distinct_categories_tags_and_time_zone()
    {
        var parent = Tree().Find(Politics)!.Unit;
        var form = UnitForm.New(parent, "section");
        form.NameEn = " Local ";
        form.NameAr = " محلي ";
        form.Tags = "city, news; city";
        form.TimeZone = " Africa/Cairo ";
        form.Categories = ["local", " cairo", "local"];

        var request = form.ToCreate(Section);

        request.ParentId.ShouldBe(Politics);
        request.TypeKey.ShouldBe("section");
        request.NameEn.ShouldBe("Local");
        request.NameAr.ShouldBe("محلي");
        request.Tags.ShouldBe(["city", "news"]);
        request.TimeZone.ShouldBe("Africa/Cairo");
        request.Categories.ShouldBe(["local", "cairo"]);
    }

    [Fact]
    public void Editing_keeps_the_version_and_a_department_sends_no_categories()
    {
        var unit = Tree().Find(Sports)!.Unit;
        var form = UnitForm.Edit(unit);
        form.TimeZone = "";

        var request = form.ToUpdate(Department);

        form.IsNew.ShouldBeFalse();
        request.Version.ShouldBe(unit.Version);
        request.TimeZone.ShouldBeNull();
        request.Categories.ShouldBeEmpty();
    }

    [Fact]
    public void Editing_a_section_starts_from_its_categories()
    {
        var form = UnitForm.Edit(Tree().Find(Local)!.Unit);

        form.Categories.ShouldBe(["local", "cairo"]);
        form.ToUpdate(Section).Categories.ShouldBe(["local", "cairo"]);
    }

    [Fact]
    public void Names_follow_the_ui_language()
    {
        Names.Pick("News", "أخبار", "ar").ShouldBe("أخبار");
        Names.Pick("News", "أخبار", "ar-EG").ShouldBe("أخبار");
        Names.Pick("News", "", "ar").ShouldBe("News");
        Names.Pick("News", "أخبار", "en").ShouldBe("News");
    }

    [Fact]
    public void Lists_split_on_commas_semicolons_and_lines()
    {
        Lists.Split("a, b;c\nd,,a").ShouldBe(["a", "b", "c", "d"]);
        Lists.Split(null).ShouldBeEmpty();
        Lists.Join(["a", "b"]).ShouldBe("a, b");
    }
}
