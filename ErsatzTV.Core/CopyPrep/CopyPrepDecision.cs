namespace ErsatzTV.Core.CopyPrep;

public record CopyPrepDecision(bool ShouldQueue, string Summary, IReadOnlyList<string> Reasons);
