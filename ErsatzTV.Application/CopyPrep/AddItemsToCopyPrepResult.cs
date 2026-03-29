namespace ErsatzTV.Application.CopyPrep;

public class AddItemsToCopyPrepResult
{
    public int QueuedCount { get; set; }
    public int RetriedCount { get; set; }
    public int SkippedCopyReadyCount { get; set; }
    public int SkippedExistingActiveCount { get; set; }
    public int SkippedUnsupportedCount { get; set; }
    public int SkippedMissingCount { get; set; }
}
