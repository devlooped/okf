namespace okf;

public enum IssueSeverity
{
    Error,
    Warning,
}

public sealed record ValidationIssue(
    IssueSeverity Severity,
    string File,
    string Message,
    int? Line = null)
{
    public override string ToString()
        => Line is int line
            ? $"{Severity.ToString().ToLowerInvariant()}: {File}:{line}: {Message}"
            : $"{Severity.ToString().ToLowerInvariant()}: {File}: {Message}";
}
