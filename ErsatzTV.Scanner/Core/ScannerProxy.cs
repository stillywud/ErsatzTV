using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Repositories;
using ErsatzTV.Infrastructure.Data;
using ErsatzTV.Scanner.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Scanner.Core;

public class ScannerProxy : IScannerProxy
{
    private readonly ILogger<ScannerProxy> _logger;
    private readonly IDbContextFactory<TvContext> _dbContextFactory;
    private string? _baseUrl;

    public ScannerProxy(
        IDbContextFactory<TvContext> dbContextFactory,
        ILogger<ScannerProxy> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public async Task<bool> UpdateProgress(decimal progress, CancellationToken cancellationToken)
    {
        // 进度更新不再通过 HTTP，直接返回成功
        return true;
    }

    public async Task<bool> ReindexMediaItems(int[] mediaItemIds, CancellationToken cancellationToken)
    {
        if (mediaItemIds.Length == 0)
        {
            return true;
        }

        try
        {
            _logger.LogDebug("[ScannerProxy] Enqueuing {Count} items for reindex via SQLite queue", mediaItemIds.Length);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

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

            _logger.LogDebug("[ScannerProxy] Successfully enqueued {Count} items for reindex", mediaItemIds.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScannerProxy] Failed to enqueue reindex requests for {Count} items", mediaItemIds.Length);
            return false;
        }
    }

    public async Task<bool> RemoveMediaItems(int[] mediaItemIds, CancellationToken cancellationToken)
    {
        if (mediaItemIds.Length == 0)
        {
            return true;
        }

        try
        {
            _logger.LogDebug("[ScannerProxy] Enqueuing {Count} items for removal via SQLite queue", mediaItemIds.Length);

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

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

            _logger.LogDebug("[ScannerProxy] Successfully enqueued {Count} items for removal", mediaItemIds.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ScannerProxy] Failed to enqueue remove requests for {Count} items", mediaItemIds.Length);
            return false;
        }
    }

    public async Task NotifyScanComplete(CancellationToken cancellationToken)
    {
        // 扫描完成通知不再通过 HTTP，直接记录日志
        _logger.LogInformation("[ScannerProxy] Scan completed");
    }
}
