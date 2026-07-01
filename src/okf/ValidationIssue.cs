namespace okf;

public sealed record ValidationIssue(
    CheckRule Rule,
    string File,
    string Message,
    IssueLocation? Location = null,
    SourceSnippet? Snippet = null)
{
    public override string ToString()
        => Location is null
            ? $"{File}: {Message}"
            : $"{File}{Location.FormatSuffix()}: {Message}";
}
