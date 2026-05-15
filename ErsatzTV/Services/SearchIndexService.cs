using ErsatzTV.Application.Search;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Repositories;
using MediatR;

namespace ErsatzTV.Services;

public class SearchIndexService : BackgroundService
{
    private readonly ILogger<SearchIndexService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SystemStartup _systemStartup;

    private const int MaxBatchSize = 100;
    private readonly TimeSpan _maxBatchTime = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(24);
    private DateTime _lastCleanupTime = DateTime.MinValue;

    private enum SearchOperation { Reindex, Remove }

    public SearchIndexService(
        IServiceScopeFactory serviceScopeFactory,
        SystemStartup systemStartup,
        ILogger<SearchIndexService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _systemStartup = systemStartup;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        await _systemStartup.WaitForDatabase(stoppingToken);
        try
        {
            _logger.LogInformation("Search index worker service started (using SQLite queue)");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var queueRepository = scope.ServiceProvider.GetRequiredService<ISearchIndexQueueRepository>();

                    // Periodic cleanup of old processed requests
                    if (DateTime.UtcNow - _lastCleanupTime > _cleanupInterval)
                    {
                        await CleanupOldRequests(queueRepository, stoppingToken);
                        _lastCleanupTime = DateTime.UtcNow;
                    }

                    // Get pending requests from queue
                    var pendingRequests = await queueRepository.GetPendingRequests(MaxBatchSize, stoppingToken);

                    if (pendingRequests.Count == 0)
                    {
                        // No pending requests, wait a bit before checking again
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                        continue;
                    }

                    // Process the batch
                    await ProcessQueueBatchAsync(pendingRequests, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing search index queue");

                    // avoid fast-looping on error
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogInformation("Search index worker service shutting down");
        }
    }

    private async Task CleanupOldRequests(ISearchIndexQueueRepository queueRepository, CancellationToken stoppingToken)
    {
        try
        {
            // Keep processed requests for 7 days
            await queueRepository.CleanupProcessedRequests(TimeSpan.FromDays(7), stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old search index queue items");
        }
    }

    private async Task ProcessQueueBatchAsync(List<SearchIndexQueueItem> requests, CancellationToken stoppingToken)
    {
        var batch = new Dictionary<int, SearchOperation>();
        var requestIds = new List<int>();

        foreach (var request in requests)
        {
            requestIds.Add(request.Id);
            switch (request.Operation)
            {
                case SearchIndexOperation.Reindex:
                    batch[request.MediaItemId] = SearchOperation.Reindex;
                    break;
                case SearchIndexOperation.Remove:
                    batch[request.MediaItemId] = SearchOperation.Remove;
                    break;
            }
        }

        var idsToReindex = new List<int>();
        var idsToRemove = new List<int>();

        foreach ((int id, SearchOperation op) in batch)
        {
            switch (op)
            {
                case SearchOperation.Reindex:
                    idsToReindex.Add(id);
                    break;
                case SearchOperation.Remove:
                    idsToRemove.Add(id);
                    break;
            }
        }

        _logger.LogDebug(
            "Processing search index batch. Reindexing: {ReindexCount}, Removing: {RemoveCount}",
            idsToReindex.Count,
            idsToRemove.Count);

        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        ISearchIndexQueueRepository queueRepository = scope.ServiceProvider.GetRequiredService<ISearchIndexQueueRepository>();

        try
        {
            if (idsToRemove.Count > 0)
            {
                await mediator.Send(new RemoveMediaItems(idsToRemove), stoppingToken);
            }

            if (idsToReindex.Count > 0)
            {
                await mediator.Send(new ReindexMediaItems(idsToReindex), stoppingToken);
            }

            // Mark all requests as processed
            await queueRepository.MarkAsProcessed(requestIds, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle search index batch worker request");
        }
    }
}
