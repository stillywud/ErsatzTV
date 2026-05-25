using System.Globalization;
using System.Threading.Channels;
using ErsatzTV.Application;
using ErsatzTV.Application.Channels;
using ErsatzTV.Application.Streaming;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Interfaces.FFmpeg;
using MediatR;

namespace ErsatzTV.Services;

/// <summary>
/// Service that pre-starts FFmpeg sessions for all channels on application startup.
/// This ensures all channels have segments ready for immediate playback ("instant channel switching").
/// </summary>
public class ChannelPreloadService : BackgroundService
{
    private readonly ILogger<ChannelPreloadService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SystemStartup _systemStartup;
    private readonly ChannelWriter<IFFmpegWorkerRequest> _ffmpegWorkerChannel;

    public ChannelPreloadService(
        IServiceScopeFactory serviceScopeFactory,
        SystemStartup systemStartup,
        ILogger<ChannelPreloadService> logger,
        ChannelWriter<IFFmpegWorkerRequest> ffmpegWorkerChannel)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _systemStartup = systemStartup;
        _logger = logger;
        _ffmpegWorkerChannel = ffmpegWorkerChannel;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        // Wait for system startup to complete (search index, etc.)
        await _systemStartup.WaitForSearchIndex(stoppingToken);
        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Channel preload service started - will pre-start FFmpeg for all channels");

            // Delay slightly to allow other services to initialize
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            await PreloadAllChannels(stoppingToken);

            _logger.LogInformation("Channel preload service completed");
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogInformation("Channel preload service shutting down");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during channel preload");
        }
    }

    private async Task PreloadAllChannels(CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            IMediator mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Get all channels
            List<ChannelViewModel> channels = await mediator.Send(
                new GetAllChannels(false),
                cancellationToken);

            if (channels.Count == 0)
            {
                _logger.LogInformation("No channels found to preload");
                return;
            }

            _logger.LogInformation("Preloading {Count} channels for instant playback", channels.Count);

            // Sort channels by number for consistent ordering
            var sortedChannels = channels.OrderBy(c =>
            {
                if (decimal.TryParse(c.Number, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal num))
                {
                    return num;
                }
                return decimal.MaxValue;
            }).ToList();

            // Start FFmpeg sessions for all channels in parallel with throttling
            var semaphore = new SemaphoreSlim(3, 3); // Max 3 concurrent starts
            var tasks = new List<Task>();

            foreach (ChannelViewModel channel in sortedChannels)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var task = Task.Run(async () =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await StartChannelSession(channel, mediator, cancellationToken);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);

                // Small delay between starting tasks to avoid overwhelming the system
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }

            await Task.WhenAll(tasks);

            _logger.LogInformation("All {Count} channels preloaded successfully", sortedChannels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preloading channels");
        }
    }

    private async Task StartChannelSession(
        ChannelViewModel channel,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Preloading channel {ChannelNumber} - {ChannelName}",
                channel.Number, channel.Name);

            // Determine mode based on channel's streaming mode
            string mode = channel.StreamingMode switch
            {
                StreamingMode.HttpLiveStreamingConcat => "segmenter-concat",
                _ => "segmenter"
            };

            // Start FFmpeg session for this channel
            // Use "segmenter" mode and empty scheme/host (will be set on actual request)
            var request = new StartFFmpegSession(
                channel.Number,
                Mode: mode,
                Scheme: "http",
                Host: "localhost");

            Either<BaseError, Unit> result = await mediator.Send(request, cancellationToken);

            result.Match(
                _ => _logger.LogDebug("Successfully preloaded channel {ChannelNumber}", channel.Number),
                error => _logger.LogWarning("Failed to preload channel {ChannelNumber}: {Error}",
                    channel.Number, error.Value));
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            // do nothing
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error starting FFmpeg session for channel {ChannelNumber}",
                channel.Number);
        }
    }
}
