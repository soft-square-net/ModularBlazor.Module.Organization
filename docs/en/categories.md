# Post categories

[العربية](../ar/categories.md) · [Operator docs](README.md)

A **Section** (any unit type that holds categories) lists the Content post categories it serves, as Content category
ids: at least one. Other units hold none. A unit above Sections serves the union of the categories of the Sections
below it in the tenant.

## All categories: `*`

Some Sections serve every category, for example Multimedia or SEO. Such a Section holds the wildcard `*`, alone:

| Rule | |
| --- | --- |
| `*` stands alone | `["*"]` is valid; `["*", "<id>"]` is refused ("can't be combined with other categories") |
| Sections only | a unit whose type holds no categories can't hold `*` either |
| A parent's categories | `["*"]` when any Section below it holds `*`; otherwise the union of the ids |

The wildcard travels as is: in the management API (`OrgUnitDto.Categories`), in the unit events
(`OrgUnitSnapshot.Categories`), in the replica, and in the local API
(`GET /api/organization/local/units/{unitId}/categories`, `IUnitDirectory.CategoriesOfAsync`). The Units page shows
it as **All categories**; in the editor, **All categories** replaces any picked categories, and picking a category
replaces it.

## For consumers

A module that checks a post's category against a unit's categories must treat `*` as a match for any category. For
example, the Elsa check `EnsurePostInUnitCategories` passes a post of any category in a unit whose categories are
`["*"]`. Use `UnitCategories.Covers(categories, categoryId)` from `soft.square.net.Organization.Contracts`;
`UnitCategories.All` is the wildcard and `UnitCategories.IsAll` tests for it. A consumer that lists the categories
(a picker, a filter) shows `*` as "All categories", not as a category id.
