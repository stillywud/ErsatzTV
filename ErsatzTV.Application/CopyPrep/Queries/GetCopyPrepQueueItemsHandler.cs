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
            .OrderByDescending(item => item.UpdatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return items.Select(Project).ToList();
    }

    private static CopyPrepQueueItemViewModel Project(CopyPrepQueueItem item) =>
        new(
            item.Id,
            item.MediaItemId,
            item.Status,
            item.Reason,
            item.SourcePath,
            item.TargetPath,
            item.ArchivePath,
            item.LastLogPath,
            item.LastError,
            item.LastExitCode,
            item.AttemptCount,
            item.CreatedAt,
            item.UpdatedAt,
            item.QueuedAt,
            item.StartedAt,
            item.CompletedAt,
            item.FailedAt,
            item.ReplacedAt,
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
