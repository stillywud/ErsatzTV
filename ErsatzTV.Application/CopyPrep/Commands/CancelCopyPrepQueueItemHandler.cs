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

        CopyPrepStatus? currentStatus = await dbContext.CopyPrepQueueItems
            .Where(item => item.Id == request.Id)
            .Select(item => (CopyPrepStatus?)item.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (currentStatus is null)
        {
            return false;
        }

        if (IsNonCancelable(currentStatus.Value))
        {
            return true;
        }

        DateTime now = DateTime.UtcNow;
        bool canceled = await TryCancel(dbContext, request.Id, now, cancellationToken);
        if (!canceled)
        {
            CopyPrepStatus? refreshedStatus = await dbContext.CopyPrepQueueItems
                .Where(item => item.Id == request.Id)
                .Select(item => (CopyPrepStatus?)item.Status)
                .SingleOrDefaultAsync(cancellationToken);

            return refreshedStatus is not null;
        }

        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = request.Id,
            CreatedAt = now,
            Level = "Information",
            Event = "manual_cancel",
            Message = "Queue item manually canceled"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    internal static async Task<bool> TryCancel(TvContext dbContext, int queueItemId, DateTime now, CancellationToken cancellationToken)
    {
        int rowsAffected = await dbContext.CopyPrepQueueItems
            .Where(item => item.Id == queueItemId && (item.Status == CopyPrepStatus.Queued || item.Status == CopyPrepStatus.Processing))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.Status, CopyPrepStatus.Canceled)
                    .SetProperty(item => item.CanceledAt, now)
                    .SetProperty(item => item.UpdatedAt, now),
                cancellationToken);

        return rowsAffected > 0;
    }

    private static bool IsNonCancelable(CopyPrepStatus status) => status is CopyPrepStatus.Prepared
        or CopyPrepStatus.Replaced
        or CopyPrepStatus.Failed
        or CopyPrepStatus.Canceled;
}
