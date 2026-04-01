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

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var url = $"{_baseUrl}/items/reindex";
            await httpClient.PostAsJsonAsync(url, mediaItemIds, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ScannerProxy] Reindex failed for IDs: {Ids}", string.Join(",", mediaItemIds));
        }

        return false;
    }

    public async Task<bool> RemoveMediaItems(int[] mediaItemIds, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return false;
        }

        if (mediaItemIds.Length == 0)
        {
            return true;
        }

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var url = $"{_baseUrl}/items/remove";
            await httpClient.PostAsJsonAsync(url, mediaItemIds, cancellationToken);
            return true;
        }
        catch
        {
            // do nothing
        }

        return false;
    }

    public async Task NotifyScanComplete(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            return;
        }

        try
        {
            using var httpClient = httpClientFactory.CreateClient();
            var url = $"{_baseUrl}/scan-complete";
            await httpClient.PostAsync(url, null, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[ScannerProxy] Failed to notify scan complete");
        }
    }
}
