using ErsatzTV.Core.CopyPrep;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Application.CopyPrep.Queries;

public class GetCopyPrepQueueItemsHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<GetCopyPrepQueueItems, List<CopyPrepQueueItemViewModel>>
{
    public async Task<List<CopyPrepQueueItemViewModel>> Handle(
        GetCopyPrepQueueItems request,
        CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        int limit = Math.Clamp(request.Limit, 1, 500);

        List<CopyPrepQueueItem> items = await dbContext.CopyPrepQueueItems
            .AsNoTracking()
            .Include(item => item.LogEntries)
            .Include(item => item.MediaItem)
                .ThenInclude(mediaItem => mediaItem.LibraryPath)
                .ThenInclude(libraryPath => libraryPath.Library)
            .Include(item => item.MediaVersion)
            .OrderByDescending(item => item.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return items.Select(Project).ToList();
    }

    internal static CopyPrepQueueItemViewModel Project(CopyPrepQueueItem item)
    {
        DateTime? lastProgressAt = File.Exists(item.LastLogPath)
            ? File.GetLastWriteTimeUtc(item.LastLogPath)
            : null;

        CopyPrepProgressSnapshot snapshot = CopyPrepProgressParser.ParseFile(item.LastLogPath, lastProgressAt);
        TimeSpan? totalDuration = item.MediaVersion?.Duration > TimeSpan.Zero ? item.MediaVersion.Duration : null;

        double frameRate = CopyPrepTranscodeProfile.NormalizeFrameRate(item.MediaVersion?.RFrameRate);
        long? estimatedTotalFrames = totalDuration.HasValue
            ? (long?)Math.Round(totalDuration.Value.TotalSeconds * frameRate, MidpointRounding.AwayFromZero)
            : null;

        double? percent = totalDuration.HasValue && snapshot.ProcessedDuration.HasValue
            ? Math.Clamp(
                snapshot.ProcessedDuration.Value.TotalMilliseconds /
                totalDuration.Value.TotalMilliseconds * 100d,
                0d,
                100d)
            : item.Status switch
            {
                CopyPrepStatus.Queued => 0d,
                CopyPrepStatus.Prepared or CopyPrepStatus.Replaced => 100d,
                _ => null
            };

        TimeSpan? eta = totalDuration.HasValue &&
            snapshot.ProcessedDuration.HasValue &&
            snapshot.AverageSpeedMultiplier is > 0d &&
            snapshot.ProcessedDuration.Value < totalDuration.Value
                ? TimeSpan.FromSeconds(
                    Math.Max(
                        0d,
                        (totalDuration.Value - snapshot.ProcessedDuration.Value).TotalSeconds /
                        snapshot.AverageSpeedMultiplier.Value))
                : null;

        return new CopyPrepQueueItemViewModel(
            item.Id,
            item.MediaItemId,
            item.Status,
            item.Reason,
            Path.GetFileNameWithoutExtension(item.SourcePath),
            item.MediaItem?.LibraryPath?.Library?.Name ?? "Unknown",
            item.SourcePath,
            item.TargetPath,
            item.ArchivePath,
            item.LastLogPath,
            item.LastCommand,
            item.LastError,
            item.LastExitCode,
            item.AttemptCount,
            item.CreatedAt,
            item.UpdatedAt,
            item.QueuedAt,
            item.StartedAt,
            item.CompletedAt,
            item.FailedAt,
            item.CanceledAt,
            item.ReplacedAt,
            new CopyPrepProgressViewModel(
                TotalDuration: totalDuration,
                ProcessedDuration: snapshot.ProcessedDuration,
                Percent: percent,
                EstimatedRemaining: eta,
                CurrentSpeedMultiplier: snapshot.CurrentSpeedMultiplier,
                AverageSpeedMultiplier: snapshot.AverageSpeedMultiplier,
                FramesPerSecond: snapshot.FramesPerSecond,
                ProcessedFrames: snapshot.ProcessedFrames,
                EstimatedTotalFrames: estimatedTotalFrames,
                OutputBytes: snapshot.OutputBytes,
                LastProgressAt: snapshot.LastProgressAt),
            (item.LogEntries ?? [])
                .OrderByDescending(logEntry => logEntry.CreatedAt)
                .Select(logEntry => new CopyPrepQueueLogEntryViewModel(
                    logEntry.Id,
                    logEntry.CreatedAt,
                    logEntry.Level,
                    logEntry.Event,
                    logEntry.Message,
                    logEntry.Details))
                .ToList());
    }
}
