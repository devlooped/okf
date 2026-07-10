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
    ];
}
