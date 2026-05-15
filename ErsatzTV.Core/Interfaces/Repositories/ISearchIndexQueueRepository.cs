using ErsatzTV.Core.Domain;

namespace ErsatzTV.Core.Interfaces.Repositories;

public interface ISearchIndexQueueRepository
{
    Task EnqueueReindexRequest(List<int> mediaItemIds, CancellationToken cancellationToken);
    Task EnqueueRemoveRequest(List<int> mediaItemIds, CancellationToken cancellationToken);
    Task<List<SearchIndexQueueItem>> GetPendingRequests(int batchSize, CancellationToken cancellationToken);
    Task MarkAsProcessed(List<int> requestIds, CancellationToken cancellationToken);
    Task<int> GetPendingCount(CancellationToken cancellationToken);
    Task CleanupProcessedRequests(TimeSpan retentionPeriod, CancellationToken cancellationToken);
}
