using System.IO.Abstractions;
using System.Text;
using CliWrap;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.FFmpeg;
using ErsatzTV.Core.Interfaces.FFmpeg;
using ErsatzTV.Core.Interfaces.Metadata;
using ErsatzTV.Core.Interfaces.Repositories;
using ErsatzTV.Core.Interfaces.Streaming;
using ErsatzTV.FFmpeg;
using ErsatzTV.FFmpeg.OutputFormat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ErsatzTV.Application.Streaming;

public class HlsConcatSessionWorker : IHlsSessionWorker
{
    private readonly IFileSystem _fileSystem;
    private readonly IConfigElementRepository _configElementRepository;
    private readonly IHlsPlaylistFilter _hlsPlaylistFilter;
    private readonly ILocalFileSystem _localFileSystem;
    private readonly ILogger<HlsConcatSessionWorker> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SemaphoreSlim _slim = new(1, 1);

    private CancellationTokenSource _cancellationTokenSource;
    private string _channelNumber;
    private bool _disposedValue;
    private DateTimeOffset _lastAccess;
    private string _workingDirectory;

    public HlsConcatSessionWorker(
        IServiceScopeFactory serviceScopeFactory,
        IConfigElementRepository configElementRepository,
        IFileSystem fileSystem,
        ILocalFileSystem localFileSystem,
        ILogger<HlsConcatSessionWorker> logger,
        IHlsPlaylistFilter hlsPlaylistFilter)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _configElementRepository = configElementRepository;
        _fileSystem = fileSystem;
        _localFileSystem = localFileSystem;
        _logger = logger;
        _hlsPlaylistFilter = hlsPlaylistFilter;
    }

    public async Task Run(
        string channelNumber,
        Option<TimeSpan> idleTimeout,
        CancellationToken incomingCancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(incomingCancellationToken);

        try
        {
            _channelNumber = channelNumber;
            _workingDirectory = Path.Combine(FileSystemLayout.TranscodeFolder, _channelNumber);

            _logger.LogInformation("Starting HLS concat session for channel {Channel}", channelNumber);

            if (_localFileSystem.ListFiles(_workingDirectory).Any())
            {
                _logger.LogError("Transcode folder is NOT empty!");
            }

            Touch(Option<string>.None);
            _lastAccess = DateTimeOffset.Now;

            _localFileSystem.EnsureFolderExists(_workingDirectory);
            _localFileSystem.EmptyFolder(_workingDirectory);

            CancellationToken cancellationToken = _cancellationTokenSource.Token;

            while (!cancellationToken.IsCancellationRequested)
            {
                foreach (TimeSpan timeout in idleTimeout)
                {
                    if (DateTimeOffset.Now - _lastAccess > timeout)
                    {
                        _logger.LogInformation("Stopping idle HLS concat session for channel {Channel}", channelNumber);
                        return;
                    }
                }

                bool success = await RunConcatProcess(cancellationToken);
                if (!success && !cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("HLS concat process failed for channel {Channel}, restarting in 5s", channelNumber);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
        }
        finally
        {
            try
            {
                _localFileSystem.EmptyFolder(_workingDirectory);
            }
            catch
            {
                // do nothing
            }
        }
    }

    private async Task<bool> RunConcatProcess(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var channelRepository = scope.ServiceProvider.GetRequiredService<IChannelRepository>();
            var ffmpegProcessService = scope.ServiceProvider.GetRequiredService<IFFmpegProcessService>();

            Option<Channel> maybeChannel = await channelRepository.GetByNumber(_channelNumber);

            foreach (Channel channel in maybeChannel)
            {
                string ffmpegPath = await _configElementRepository.GetValue<string>(ConfigElementKey.FFmpegPath, cancellationToken)
                    .Match(p => p, () => "ffmpeg");

                Command command = await ffmpegProcessService.ConcatHlsChannel(
                    ffmpegPath,
                    false,
                    channel,
                    "http",
                    "localhost",
                    cancellationToken);

                _logger.LogInformation(
                    "Starting HLS concat process for channel {Channel}: {FFmpegArguments}",
                    _channelNumber,
                    command.Arguments);

                var progressParser = new FFmpegProgress();
                var stdErrBuffer = new StringBuilder();

                CommandResult result = await command
                    .WithWorkingDirectory(_workingDirectory)
                    .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stdErrBuffer))
                    .WithStandardOutputPipe(PipeTarget.ToDelegate(progressParser.ParseLine))
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteAsync(cancellationToken);

                if (result.ExitCode == 0)
                {
                    _logger.LogInformation("HLS concat process completed for channel {Channel}", _channelNumber);
                    return true;
                }

                _logger.LogError(
                    "HLS concat process for channel {Channel} exited with code {ExitCode}: {Error}",
                    _channelNumber,
                    result.ExitCode,
                    stdErrBuffer.ToString());

                return false;
            }

            _logger.LogWarning("Channel {Channel} not found for HLS concat session", _channelNumber);
            return false;
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            _logger.LogInformation("Terminating HLS concat session for channel {Channel}", _channelNumber);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in HLS concat session for channel {Channel}", _channelNumber);
            return false;
        }
    }

    public Task Cancel(CancellationToken cancellationToken)
    {
        if (_cancellationTokenSource is not null)
        {
            return _cancellationTokenSource.CancelAsync();
        }

        return Task.CompletedTask;
    }

    public void Touch(Option<string> fileName)
    {
        _lastAccess = DateTimeOffset.Now;
    }

    public Task<Option<TrimPlaylistResult>> TrimPlaylist(DateTimeOffset filterBefore, CancellationToken cancellationToken)
    {
        // For concat mode, ffmpeg manages the playlist directly
        // We still trim to keep the playlist size manageable
        return TrimAndDelete(filterBefore, cancellationToken);
    }

    private async Task<Option<TrimPlaylistResult>> TrimAndDelete(DateTimeOffset filterBefore, CancellationToken cancellationToken)
    {
        await _slim.WaitAsync(cancellationToken);
        try
        {
            string playlistPath = Path.Combine(_workingDirectory, "live.m3u8");
            if (!_fileSystem.File.Exists(playlistPath))
            {
                return Option<TrimPlaylistResult>.None;
            }

            string[] lines = await _fileSystem.File.ReadAllLinesAsync(playlistPath, cancellationToken);
            TrimPlaylistResult trimResult = _hlsPlaylistFilter.TrimPlaylistWithDiscontinuity(
                new Dictionary<long, int>(),
                OutputFormatKind.Hls,
                DateTimeOffset.Now.AddMinutes(-1),
                filterBefore,
                new HlsInitSegmentCache(_localFileSystem),
                lines);

            await _fileSystem.File.WriteAllTextAsync(playlistPath, trimResult.Playlist, cancellationToken);

            return trimResult;
        }
        finally
        {
            _slim.Release();
        }
    }

    public void PlayoutUpdated()
    {
        // For concat mode, playout updates don't require special handling
        // The concat demuxer reads the current playout item via HTTP endpoint
    }

    public HlsSessionModel GetModel() => new(_channelNumber, "Concat", DateTimeOffset.Now, _lastAccess);

    public async Task WaitForPlaylistSegments(int initialSegmentCount, CancellationToken cancellationToken)
    {
        string playlistPath = Path.Combine(_workingDirectory, "live.m3u8");

        for (int i = 0; i < 100; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (_fileSystem.File.Exists(playlistPath))
            {
                string[] lines = await _fileSystem.File.ReadAllLinesAsync(playlistPath, cancellationToken);
                int segmentCount = lines.Count(l => l.StartsWith("#EXTINF", StringComparison.Ordinal));
                if (segmentCount >= initialSegmentCount)
                {
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        _logger.LogWarning("Timed out waiting for playlist segments for channel {Channel}", _channelNumber);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _cancellationTokenSource?.Dispose();
                _slim.Dispose();
            }

            _disposedValue = true;
        }
    }
}
