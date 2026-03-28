using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Application.CopyPrep.Commands;

public class CancelCopyPrepQueueItemHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<CancelCopyPrepQueueItem, bool>
{
    public async Task<bool> Handle(CancelCopyPrepQueueItem request, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .FirstOrDefaultAsync(item => item.Id == request.Id, cancellationToken);

        if (queueItem is null)
        {
            return false;
        }

        if (queueItem.Status is CopyPrepStatus.Prepared
            or CopyPrepStatus.Replaced
            or CopyPrepStatus.Failed
            or CopyPrepStatus.Canceled)
        {
            return true;
        }

        DateTime now = DateTime.UtcNow;
        queueItem.Status = CopyPrepStatus.Canceled;
        queueItem.CanceledAt = now;
        queueItem.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = queueItem.Id,
            CreatedAt = now,
            Level = "Information",
            Event = "manual_cancel",
            Message = "Queue item manually canceled"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
