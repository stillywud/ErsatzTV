using System.Net.Http.Json;
using ErsatzTV.Scanner.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Scanner.Core;

public class ScannerProxy(IHttpClientFactory httpClientFactory, ILogger<ScannerProxy> logger) : IScannerProxy
{
    private string? _baseUrl;

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl;
    }

    public async Task<bool> UpdateProgress(decimal progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return false;
        }

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var url = $"{_baseUrl}/progress";
            await httpClient.PostAsJsonAsync(url, progress, cancellationToken);
            return true;
        }
        catch
        {
            // do nothing
        }

        return false;
    }

    public async Task<bool> ReindexMediaItems(int[] mediaItemIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            logger.LogWarning("[ScannerProxy] ReindexMediaItems failed: _baseUrl is null or empty!");
            return false;
        }

        if (mediaItemIds.Length == 0)
        {
            return true;
        }

        // 添加重试逻辑，最多重试3次
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30); // 增加超时到30秒
                var url = $"{_baseUrl}/items/reindex";

                logger.LogDebug("[ScannerProxy] Sending reindex request for {Count} items (attempt {Attempt})",
                    mediaItemIds.Length, attempt);

                var response = await httpClient.PostAsJsonAsync(url, mediaItemIds, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogDebug("[ScannerProxy] Reindex request succeeded for {Count} items", mediaItemIds.Length);
                    return true;
                }

                logger.LogWarning("[ScannerProxy] Reindex request failed with status {StatusCode} (attempt {Attempt})",
                    response.StatusCode, attempt);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                logger.LogWarning("[ScannerProxy] Reindex request timed out (attempt {Attempt})", attempt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ScannerProxy] Reindex failed for IDs: {Ids} (attempt {Attempt})",
                    string.Join(",", mediaItemIds), attempt);
            }

            // 指数退避重试
            if (attempt < 3)
            {
                var delayMs = 1000 * attempt; // 1s, 2s
                logger.LogDebug("[ScannerProxy] Retrying in {DelayMs}ms", delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        logger.LogError("[ScannerProxy] Reindex failed after all retries for {Count} items", mediaItemIds.Length);
        return false;
    }

    public async Task<bool> RemoveMediaItems(int[] mediaItemIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            logger.LogWarning("[ScannerProxy] RemoveMediaItems failed: _baseUrl is null or empty!");
            return false;
        }

        if (mediaItemIds.Length == 0)
        {
            return true;
        }

        // 添加重试逻辑，最多重试3次
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                var url = $"{_baseUrl}/items/remove";

                logger.LogDebug("[ScannerProxy] Sending remove request for {Count} items (attempt {Attempt})",
                    mediaItemIds.Length, attempt);

                var response = await httpClient.PostAsJsonAsync(url, mediaItemIds, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogDebug("[ScannerProxy] Remove request succeeded for {Count} items", mediaItemIds.Length);
                    return true;
                }

                logger.LogWarning("[ScannerProxy] Remove request failed with status {StatusCode} (attempt {Attempt})",
                    response.StatusCode, attempt);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                logger.LogWarning("[ScannerProxy] Remove request timed out (attempt {Attempt})", attempt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ScannerProxy] Remove failed for IDs: {Ids} (attempt {Attempt})",
                    string.Join(",", mediaItemIds), attempt);
            }

            if (attempt < 3)
            {
                var delayMs = 1000 * attempt;
                logger.LogDebug("[ScannerProxy] Retrying in {DelayMs}ms", delayMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        logger.LogError("[ScannerProxy] Remove failed after all retries for {Count} items", mediaItemIds.Length);
        return false;
    }

    public async Task NotifyScanComplete(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            logger.LogWarning("[ScannerProxy] NotifyScanComplete failed: _baseUrl is null or empty!");
            return;
        }

        // 添加重试逻辑
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                httpClient.Timeout = TimeSpan.FromSeconds(30);
                var url = $"{_baseUrl}/scan-complete";

                logger.LogDebug("[ScannerProxy] Sending scan-complete notification (attempt {Attempt})", attempt);

                var response = await httpClient.PostAsync(url, null, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("[ScannerProxy] Scan complete notification sent successfully");
                    return;
                }

                logger.LogWarning("[ScannerProxy] Scan-complete failed with status {StatusCode} (attempt {Attempt})",
                    response.StatusCode, attempt);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                logger.LogWarning("[ScannerProxy] Scan-complete request timed out (attempt {Attempt})", attempt);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[ScannerProxy] Failed to notify scan complete (attempt {Attempt})", attempt);
            }

            if (attempt < 3)
            {
                var delayMs = 1000 * attempt;
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        logger.LogError("[ScannerProxy] Failed to notify scan complete after all retries");
    }
}
