using ErsatzTV.Application.Search;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.Repositories;
using MediatR;

namespace ErsatzTV.Services;

public class SearchIndexHealthCheckService : BackgroundService
{
    private readonly ILogger<SearchIndexHealthCheckService> _logger;
    private readonly IMediator _mediator;
    private readonly SystemStartup _systemStartup;
    private readonly IConfigElementRepository _configElementRepository;

    // 默认配置值
    private const int DefaultCheckIntervalHours = 1;
    private const int DefaultAutoRebuildThreshold = 10;
    private const double DefaultAutoRebuildPercentThreshold = 0.01;

    public SearchIndexHealthCheckService(
        IMediator mediator,
        SystemStartup systemStartup,
        IConfigElementRepository configElementRepository,
        ILogger<SearchIndexHealthCheckService> logger)
    {
        _mediator = mediator;
        _systemStartup = systemStartup;
        _configElementRepository = configElementRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _logger.LogInformation("[SearchIndexHealthCheck] Health check service started");

        // 等待搜索索引就绪
        await _systemStartup.WaitForSearchIndex(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 读取配置
                var checkInterval = await GetCheckInterval(stoppingToken);
                var autoRebuildEnabled = await GetAutoRebuildEnabled(stoppingToken);
                var autoRebuildThreshold = await GetAutoRebuildThreshold(stoppingToken);
                var autoRebuildPercentThreshold = await GetAutoRebuildPercentThreshold(stoppingToken);

                await Task.Delay(checkInterval, stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _logger.LogDebug("[SearchIndexHealthCheck] Running health check...");

                var health = await _mediator.Send(new CheckSearchIndexHealth(), stoppingToken);

                if (!health.IsHealthy)
                {
                    _logger.LogError(
                        "[SearchIndexHealthCheck] Index out of sync! DB: {DbCount}, Index: {IndexCount}, Diff: {Difference}",
                        health.DatabaseCount,
                        health.IndexCount,
                        health.Difference);

                    // 检查是否需要自动重建
                    bool shouldRebuild = autoRebuildEnabled &&
                                         (health.Difference >= autoRebuildThreshold ||
                                          (health.DatabaseCount > 0 &&
                                           (double)health.Difference / health.DatabaseCount > autoRebuildPercentThreshold));

                    if (shouldRebuild)
                    {
                        _logger.LogWarning(
                            "[SearchIndexHealthCheck] Auto-rebuilding search index (threshold: {Threshold} items or {PercentThreshold}%)",
                            autoRebuildThreshold,
                            autoRebuildPercentThreshold * 100);

                        try
                        {
                            await _mediator.Send(new RebuildSearchIndex(), stoppingToken);
                            _logger.LogInformation("[SearchIndexHealthCheck] Search index rebuilt successfully");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[SearchIndexHealthCheck] Failed to rebuild search index");
                        }
                    }
                    else if (!autoRebuildEnabled)
                    {
                        _logger.LogInformation(
                            "[SearchIndexHealthCheck] Auto-rebuild disabled, skipping rebuild");
                    }
                    else
                    {
                        _logger.LogInformation(
                            "[SearchIndexHealthCheck] Difference below threshold, skipping auto-rebuild");
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "[SearchIndexHealthCheck] Index is healthy (DB: {DbCount}, Index: {IndexCount})",
                        health.DatabaseCount,
                        health.IndexCount);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SearchIndexHealthCheck] Error during health check");
            }
        }

        _logger.LogInformation("[SearchIndexHealthCheck] Health check service stopped");
    }

    private async Task<TimeSpan> GetCheckInterval(CancellationToken cancellationToken)
    {
        Option<int> maybeHours = await _configElementRepository.GetValue<int>(
            ConfigElementKey.SearchIndexHealthCheckInterval,
            cancellationToken);
        int hours = maybeHours.Match(x => x, DefaultCheckIntervalHours);
        return TimeSpan.FromHours(Math.Max(1, hours)); // 最小1小时
    }

    private async Task<bool> GetAutoRebuildEnabled(CancellationToken cancellationToken)
    {
        Option<bool> maybeEnabled = await _configElementRepository.GetValue<bool>(
            ConfigElementKey.SearchIndexAutoRebuildEnabled,
            cancellationToken);
        return maybeEnabled.Match(x => x, true); // 默认启用
    }

    private async Task<int> GetAutoRebuildThreshold(CancellationToken cancellationToken)
    {
        Option<int> maybeThreshold = await _configElementRepository.GetValue<int>(
            ConfigElementKey.SearchIndexAutoRebuildThreshold,
            cancellationToken);
        return maybeThreshold.Match(x => x, DefaultAutoRebuildThreshold);
    }

    private async Task<double> GetAutoRebuildPercentThreshold(CancellationToken cancellationToken)
    {
        Option<double> maybeThreshold = await _configElementRepository.GetValue<double>(
            ConfigElementKey.SearchIndexAutoRebuildThreshold, // 复用同一个key，存储百分比
            cancellationToken);
        return maybeThreshold.Match(x => x > 1 ? x / 100.0 : x, DefaultAutoRebuildPercentThreshold);
    }
}
