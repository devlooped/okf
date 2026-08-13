namespace Devlooped;

public enum CheckRule
{
    BundleExists,
    ConceptFrontmatter,
    ConceptType,
    IndexFrontmatter,
    IndexStructure,
    IndexProse,
    LogFormat,
    InternalLinks,
    SourcesResource,
    GeneratedBy,
    VerifiedShape,
    StatusValue,
    StaleAfter,
    PathValuedFields,
    SourceFootnotes,
}

public static class CheckRules
{
    public static readonly IReadOnlyList<(CheckRule Rule, string Description)> All =
    [
        (CheckRule.BundleExists, "Bundle directory exists"),
        (CheckRule.ConceptFrontmatter, "Concept files have valid YAML frontmatter"),
        (CheckRule.ConceptType, "Concept files declare a type"),
        (CheckRule.IndexFrontmatter, "index.md frontmatter is valid"),
        (CheckRule.IndexStructure, "index.md structure and entries are valid"),
        (CheckRule.LogFormat, "log.md format is valid"),
    ];

    public static readonly IReadOnlyList<(CheckRule Rule, string Description)> Warnings =
    [
        (CheckRule.InternalLinks, "Unresolved internal links"),
        (CheckRule.IndexProse, "index.md free prose (non-structural lines)"),
        (CheckRule.SourcesResource, "sources entries missing resource"),
        (CheckRule.GeneratedBy, "generated present without by"),
        (CheckRule.VerifiedShape, "verified is not a mapping or list of mappings"),
        (CheckRule.StatusValue, "status is not draft, stable, or deprecated"),
        (CheckRule.StaleAfter, "stale_after is not YYYY-MM-DD"),
        (CheckRule.PathValuedFields, "Unresolved path-valued frontmatter fields"),
        (CheckRule.SourceFootnotes, "Footnote labels with no matching sources id"),
    ];
}
