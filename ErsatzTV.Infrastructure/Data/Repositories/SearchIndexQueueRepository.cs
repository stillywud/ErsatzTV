using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Infrastructure.Data.Repositories;

public class SearchIndexQueueRepository(
    IDbContextFactory<TvContext> dbContextFactory,
    ILogger<SearchIndexQueueRepository> logger) : ISearchIndexQueueRepository
{
    private readonly ILogger<SearchIndexQueueRepository> _logger = logger;
    public async Task EnqueueReindexRequest(List<int> mediaItemIds, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        foreach (int id in mediaItemIds)
        {
            var request = new SearchIndexQueueItem
            {
                MediaItemId = id,
                Operation = SearchIndexOperation.Reindex,
                CreatedAt = DateTime.UtcNow,
                Processed = false
            };
            dbContext.SearchIndexQueue.Add(request);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task EnqueueRemoveRequest(List<int> mediaItemIds, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        foreach (int id in mediaItemIds)
        {
            var request = new SearchIndexQueueItem
            {
                MediaItemId = id,
                Operation = SearchIndexOperation.Remove,
                CreatedAt = DateTime.UtcNow,
                Processed = false
            };
            dbContext.SearchIndexQueue.Add(request);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<SearchIndexQueueItem>> GetPendingRequests(int batchSize, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.SearchIndexQueue
            .Where(q => !q.Processed)
            .OrderBy(q => q.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkAsProcessed(List<int> requestIds, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var requests = await dbContext.SearchIndexQueue
            .Where(q => requestIds.Contains(q.Id))
            .ToListAsync(cancellationToken);

        foreach (var request in requests)
        {
            request.Processed = true;
            request.ProcessedAt = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetPendingCount(CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await dbContext.SearchIndexQueue
            .CountAsync(q => !q.Processed, cancellationToken);
    }

    public async Task CleanupProcessedRequests(TimeSpan retentionPeriod, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = DateTime.UtcNow - retentionPeriod;
        var oldRequests = await dbContext.SearchIndexQueue
            .Where(q => q.Processed && q.ProcessedAt < cutoff)
            .ToListAsync(cancellationToken);

        dbContext.SearchIndexQueue.RemoveRange(oldRequests);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cleaned up {Count} old search index queue items", oldRequests.Count);
    }
}
