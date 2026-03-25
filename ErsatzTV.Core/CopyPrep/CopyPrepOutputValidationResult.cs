namespace ErsatzTV.Core.CopyPrep;

public record CopyPrepOutputValidationResult(bool IsValid, string Summary, IReadOnlyList<string> Issues)
{
    public static CopyPrepOutputValidationResult Success(string summary) => new(true, summary, []);

    public static CopyPrepOutputValidationResult Failure(params string[] issues) =>
        Failure((IEnumerable<string>)issues);

    public static CopyPrepOutputValidationResult Failure(IEnumerable<string> issues)
    {
        List<string> materializedIssues = issues
            .Where(issue => !string.IsNullOrWhiteSpace(issue))
            .ToList();

        string summary = materializedIssues.Count == 0
            ? "Prepared output validation failed"
            : string.Join("; ", materializedIssues);

        return new CopyPrepOutputValidationResult(false, summary, materializedIssues);
    }
}
