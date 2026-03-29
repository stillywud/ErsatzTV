using ErsatzTV.Core.CopyPrep;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Core.Extensions;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Application.CopyPrep.Commands;

public class AddItemsToCopyPrepHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<AddItemsToCopyPrep, AddItemsToCopyPrepResult>
{
    public async Task<AddItemsToCopyPrepResult> Handle(
        AddItemsToCopyPrep request,
        CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        List<Movie> movies = await dbContext.Movies
            .Where(movie => request.MovieIds.Contains(movie.Id))
            .Include(movie => movie.MediaVersions)
                .ThenInclude(version => version.MediaFiles)
            .Include(movie => movie.MediaVersions)
                .ThenInclude(version => version.Streams)
            .ToListAsync(cancellationToken);

        List<CopyPrepQueueItem> existingQueueItems = await dbContext.CopyPrepQueueItems
            .Where(item => request.MovieIds.Contains(item.MediaItemId))
            .ToListAsync(cancellationToken);

        var moviesById = movies.ToDictionary(movie => movie.Id);
        var queueItemsByMediaItemId = existingQueueItems
            .GroupBy(item => item.MediaItemId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new AddItemsToCopyPrepResult();

        foreach (int movieId in request.MovieIds)
        {
            if (!moviesById.TryGetValue(movieId, out Movie movie) || movie.MediaVersions.Count == 0)
            {
                result.SkippedMissingCount++;
                continue;
            }

            MediaVersion version = movie.GetHeadVersion();
            if (version.MediaFiles.Count == 0)
            {
                result.SkippedMissingCount++;
                continue;
            }

            MediaFile file = version.MediaFiles.Head();
            if (string.IsNullOrWhiteSpace(file.Path) || !File.Exists(file.Path))
            {
                result.SkippedMissingCount++;
                continue;
            }

            CopyPrepDecision decision = CopyPrepAnalyzer.Analyze(version, file.Path);
            if (!decision.ShouldQueue)
            {
                result.SkippedCopyReadyCount++;
                continue;
            }

            if (!queueItemsByMediaItemId.TryGetValue(movieId, out List<CopyPrepQueueItem> queueItems))
            {
                queueItems = [];
                queueItemsByMediaItemId[movieId] = queueItems;
            }

            CopyPrepQueueItem activeQueueItem = queueItems
                .FirstOrDefault(item => item.Status is not (CopyPrepStatus.Failed or CopyPrepStatus.Canceled or CopyPrepStatus.Skipped));
            if (activeQueueItem is not null)
            {
                result.SkippedExistingActiveCount++;
                continue;
            }

            CopyPrepQueueItem retryQueueItem = queueItems
                .OrderByDescending(item => item.UpdatedAt)
                .FirstOrDefault(item => item.Status is CopyPrepStatus.Failed or CopyPrepStatus.Canceled or CopyPrepStatus.Skipped);

            if (retryQueueItem is not null)
            {
                DateTime now = DateTime.UtcNow;
                RefreshForRetry(retryQueueItem, version, file, decision, now);
                dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
                {
                    CopyPrepQueueItemId = retryQueueItem.Id,
                    CreatedAt = now,
                    Level = "Information",
                    Event = "manual_retry_from_search_selection",
                    Message = "Queue item manually re-queued from search selection"
                });
                result.RetriedCount++;
                continue;
            }

            DateTime queuedAt = DateTime.UtcNow;
            var newQueueItem = new CopyPrepQueueItem
            {
                MediaItemId = movieId,
                MediaVersionId = version.Id,
                MediaFileId = file.Id,
                Status = CopyPrepStatus.Queued,
                Reason = decision.Summary,
                SourcePath = file.Path,
                TargetPath = Path.ChangeExtension(file.Path, ".mp4"),
                ArchivePath = Path.Combine(Path.GetDirectoryName(file.Path) ?? string.Empty, "archive", Path.GetFileName(file.Path)),
                CreatedAt = queuedAt,
                UpdatedAt = queuedAt,
                QueuedAt = queuedAt,
                LogEntries = []
            };

            dbContext.CopyPrepQueueItems.Add(newQueueItem);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
            {
                CopyPrepQueueItemId = newQueueItem.Id,
                CreatedAt = queuedAt,
                Level = "Information",
                Event = "queued_from_search_selection",
                Message = $"Queued from search selection because the source is not copy-friendly: {decision.Summary}",
                Details = string.Join(Environment.NewLine, decision.Reasons)
            });

            queueItems.Add(newQueueItem);
            result.QueuedCount++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static void RefreshForRetry(
        CopyPrepQueueItem queueItem,
        MediaVersion version,
        MediaFile file,
        CopyPrepDecision decision,
        DateTime now)
    {
        queueItem.MediaVersionId = version.Id;
        queueItem.MediaFileId = file.Id;
        queueItem.Reason = decision.Summary;
        queueItem.SourcePath = file.Path;
        queueItem.TargetPath = Path.ChangeExtension(file.Path, ".mp4");
        queueItem.ArchivePath = Path.Combine(Path.GetDirectoryName(file.Path) ?? string.Empty, "archive", Path.GetFileName(file.Path));
        queueItem.Status = CopyPrepStatus.Queued;
        queueItem.QueuedAt = now;
        queueItem.UpdatedAt = now;
        queueItem.StartedAt = null;
        queueItem.CompletedAt = null;
        queueItem.FailedAt = null;
        queueItem.CanceledAt = null;
        queueItem.ReplacedAt = null;
        queueItem.LastError = null;
        queueItem.LastExitCode = null;
    }
}
