namespace ErsatzTV.Application.CopyPrep;

public record CopyPrepProgressViewModel(
    TimeSpan? TotalDuration,
    TimeSpan? ProcessedDuration,
    double? Percent,
    TimeSpan? EstimatedRemaining,
    double? CurrentSpeedMultiplier,
    double? AverageSpeedMultiplier,
    double? FramesPerSecond,
    long? ProcessedFrames,
    long? EstimatedTotalFrames,
    long? OutputBytes,
    DateTime? LastProgressAt);
