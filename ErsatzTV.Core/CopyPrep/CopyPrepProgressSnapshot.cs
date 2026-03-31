namespace ErsatzTV.Core.CopyPrep;

public record CopyPrepProgressSnapshot(
    long? ProcessedFrames,
    double? FramesPerSecond,
    long? OutputBytes,
    TimeSpan? ProcessedDuration,
    double? CurrentSpeedMultiplier,
    double? AverageSpeedMultiplier,
    DateTime? LastProgressAt)
{
    public static readonly CopyPrepProgressSnapshot Empty = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null);

    public bool HasLiveProgress =>
        ProcessedFrames.HasValue ||
        FramesPerSecond.HasValue ||
        OutputBytes.HasValue ||
        ProcessedDuration.HasValue ||
        CurrentSpeedMultiplier.HasValue;
}
