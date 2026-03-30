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

        List<OtherVideo> otherVideos = await dbContext.OtherVideos
            .Where(otherVideo => request.OtherVideoIds.Contains(otherVideo.Id))
            .Include(otherVideo => otherVideo.MediaVersions)
                .ThenInclude(version => version.MediaFiles)
            .Include(otherVideo => otherVideo.MediaVersions)
                .ThenInclude(version => version.Streams)
            .ToListAsync(cancellationToken);

        int[] allMediaItemIds = request.MovieIds
            .Concat(request.OtherVideoIds)
            .Distinct()
            .ToArray();

        List<CopyPrepQueueItem> existingQueueItems = await dbContext.CopyPrepQueueItems
            .Where(item => allMediaItemIds.Contains(item.MediaItemId))
            .ToListAsync(cancellationToken);

        var queueItemsByMediaItemId = existingQueueItems
            .GroupBy(item => item.MediaItemId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var result = new AddItemsToCopyPrepResult();

        foreach (SelectedItem item in GetSelectedItems(request, movies, otherVideos))
        {
            if (item.Version.MediaFiles.Count == 0)
            {
                result.SkippedMissingCount++;
                continue;
            }

            MediaFile file = item.Version.MediaFiles.Head();
            if (string.IsNullOrWhiteSpace(file.Path) || !File.Exists(file.Path))
            {
                result.SkippedMissingCount++;
                continue;
            }

            CopyPrepDecision decision = CopyPrepAnalyzer.Analyze(item.Version, file.Path);
            if (!decision.ShouldQueue)
            {
                result.SkippedCopyReadyCount++;
                continue;
            }

            if (!queueItemsByMediaItemId.TryGetValue(item.MediaItemId, out List<CopyPrepQueueItem> queueItems))
            {
                queueItems = [];
                queueItemsByMediaItemId[item.MediaItemId] = queueItems;
            }

            CopyPrepQueueItem activeQueueItem = queueItems
                .FirstOrDefault(queueItem => queueItem.Status is not (CopyPrepStatus.Failed or CopyPrepStatus.Canceled or CopyPrepStatus.Skipped));
            if (activeQueueItem is not null)
            {
                result.SkippedExistingActiveCount++;
                continue;
            }

            CopyPrepQueueItem retryQueueItem = queueItems
                .OrderByDescending(queueItem => queueItem.UpdatedAt)
                .FirstOrDefault(queueItem => queueItem.Status is CopyPrepStatus.Failed or CopyPrepStatus.Canceled or CopyPrepStatus.Skipped);

            if (retryQueueItem is not null)
            {
                DateTime now = DateTime.UtcNow;
                RefreshForRetry(retryQueueItem, item.Version, file, decision, now);
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
                MediaItemId = item.MediaItemId,
                MediaVersionId = item.Version.Id,
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

        int foundMediaItems = movies.Count + otherVideos.Count;
        int requestedMediaItems = allMediaItemIds.Length;
        result.SkippedMissingCount += requestedMediaItems - foundMediaItems;

        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static IEnumerable<SelectedItem> GetSelectedItems(
        AddItemsToCopyPrep request,
        IReadOnlyCollection<Movie> movies,
        IReadOnlyCollection<OtherVideo> otherVideos)
    {
        var moviesById = movies.ToDictionary(movie => movie.Id);
        foreach (int movieId in request.MovieIds.Distinct())
        {
            if (moviesById.TryGetValue(movieId, out Movie movie) && movie.MediaVersions.Count > 0)
            {
                yield return new SelectedItem(movie.Id, "movie", movie.MediaVersions[0]);
            }
        }

        var otherVideosById = otherVideos.ToDictionary(otherVideo => otherVideo.Id);
        foreach (int otherVideoId in request.OtherVideoIds.Distinct())
        {
            if (otherVideosById.TryGetValue(otherVideoId, out OtherVideo otherVideo) && otherVideo.MediaVersions.Count > 0)
            {
                yield return new SelectedItem(otherVideo.Id, "other_video", otherVideo.MediaVersions[0]);
            }
        }
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

    private sealed record SelectedItem(int MediaItemId, string MediaKind, MediaVersion Version);
}
