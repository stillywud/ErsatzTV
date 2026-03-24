using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Application.CopyPrep.Commands;

public class RetryCopyPrepQueueItemHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<RetryCopyPrepQueueItem, bool>
{
    public async Task<bool> Handle(RetryCopyPrepQueueItem request, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (queueItem is null)
        {
            return false;
        }

        if (queueItem.Status is CopyPrepStatus.Processing or CopyPrepStatus.Queued)
        {
            return true;
        }

        DateTime now = DateTime.UtcNow;
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
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = queueItem.Id,
            CreatedAt = now,
            Level = "Information",
            Event = "manual_retry",
            Message = "Queue item manually re-queued"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
