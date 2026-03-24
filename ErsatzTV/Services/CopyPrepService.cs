using System.Diagnostics;
using System.Globalization;
using System.Text;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Core.Extensions;
using ErsatzTV.Core.Interfaces.Metadata;
using ErsatzTV.Core.Interfaces.Repositories;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ErsatzTV.Services;

public class CopyPrepService(
    IServiceScopeFactory serviceScopeFactory,
    SystemStartup systemStartup,
    ILogger<CopyPrepService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        await systemStartup.WaitForDatabase(stoppingToken);

        var runningTasks = new List<Task>();

        try
        {
            logger.LogInformation("Copy prep service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                runningTasks.RemoveAll(task => task.IsCompleted);

                CopyPrepSettings settings = await GetSettings(stoppingToken);
                if (settings.Enabled)
                {
                    while (runningTasks.Count < settings.MaxConcurrentJobs && !stoppingToken.IsCancellationRequested)
                    {
                        Option<int> maybeQueueItemId = await TryClaimNextItem(stoppingToken);
                        if (maybeQueueItemId.IsNone)
                        {
                            break;
                        }

                        foreach (int queueItemId in maybeQueueItemId)
                        {
                            runningTasks.Add(ProcessQueueItem(queueItemId, settings, stoppingToken));
                        }
                    }
                }

                Task delay = Task.Delay(
                    settings.Enabled ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30),
                    stoppingToken);

                if (runningTasks.Count == 0)
                {
                    await delay;
                }
                else
                {
                    var waitTasks = runningTasks.ToList();
                    waitTasks.Add(delay);
                    await Task.WhenAny(waitTasks);
                }
            }
        }
        catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
        {
            logger.LogInformation("Copy prep service shutting down");
        }
        finally
        {
            try
            {
                await Task.WhenAll(runningTasks);
            }
            catch
            {
                // ignored on shutdown
            }
        }
    }

    private async Task<CopyPrepSettings> GetSettings(CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceScopeFactory.CreateScope();
        IConfigElementRepository configElementRepository = scope.ServiceProvider.GetRequiredService<IConfigElementRepository>();

        bool enabled = await configElementRepository
            .GetValue<bool>(ConfigElementKey.FFmpegCopyPrepEnabled, cancellationToken)
            .IfNoneAsync(false);

        int cpuTargetPercent = await configElementRepository
            .GetValue<int>(ConfigElementKey.FFmpegCopyPrepCpuTargetPercent, cancellationToken)
            .IfNoneAsync(50);

        int maxConcurrentJobs = await configElementRepository
            .GetValue<int>(ConfigElementKey.FFmpegCopyPrepMaxConcurrentJobs, cancellationToken)
            .IfNoneAsync(1);

        int configuredThreads = await configElementRepository
            .GetValue<int>(ConfigElementKey.FFmpegCopyPrepThreads, cancellationToken)
            .IfNoneAsync(0);

        int threadsPerJob = configuredThreads > 0
            ? configuredThreads
            : Math.Max(
                1,
                (int)Math.Floor(Environment.ProcessorCount * (Math.Clamp(cpuTargetPercent, 10, 100) / 100.0) /
                                 Math.Max(1, maxConcurrentJobs)));

        return new CopyPrepSettings(
            enabled,
            Math.Clamp(cpuTargetPercent, 10, 100),
            Math.Clamp(maxConcurrentJobs, 1, 8),
            Math.Max(1, threadsPerJob));
    }

    private async Task<Option<int>> TryClaimNextItem(CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceScopeFactory.CreateScope();
        IDbContextFactory<TvContext> dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TvContext>>();

        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .OrderBy(item => item.QueuedAt)
            .FirstOrDefaultAsync(item => item.Status == CopyPrepStatus.Queued, cancellationToken);

        if (queueItem is null)
        {
            return Option<int>.None;
        }

        DateTime now = DateTime.UtcNow;
        queueItem.Status = CopyPrepStatus.Processing;
        queueItem.StartedAt = now;
        queueItem.UpdatedAt = now;
        queueItem.LastError = null;
        queueItem.LastExitCode = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = queueItem.Id,
            CreatedAt = now,
            Level = "Information",
            Event = "processing_started",
            Message = "Background copy-prep processing started"
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return queueItem.Id;
    }

    private async Task ProcessQueueItem(int queueItemId, CopyPrepSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            using IServiceScope scope = serviceScopeFactory.CreateScope();
            IDbContextFactory<TvContext> dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TvContext>>();
            IConfigElementRepository configElementRepository = scope.ServiceProvider.GetRequiredService<IConfigElementRepository>();
            ILocalStatisticsProvider localStatisticsProvider = scope.ServiceProvider.GetRequiredService<ILocalStatisticsProvider>();

            await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
                .Include(item => item.MediaVersion)
                .ThenInclude(version => version.Streams)
                .Include(item => item.MediaFile)
                .FirstOrDefaultAsync(item => item.Id == queueItemId, cancellationToken);

            if (queueItem is null || queueItem.Status != CopyPrepStatus.Processing)
            {
                return;
            }

            string ffmpegPath = await configElementRepository
                .GetValue<string>(ConfigElementKey.FFmpegPath, cancellationToken)
                .IfNoneAsync(string.Empty);
            string ffprobePath = await configElementRepository
                .GetValue<string>(ConfigElementKey.FFprobePath, cancellationToken)
                .IfNoneAsync(string.Empty);

            if (!File.Exists(ffmpegPath))
            {
                throw new FileNotFoundException("Configured ffmpeg path does not exist", ffmpegPath);
            }

            if (!File.Exists(ffprobePath))
            {
                throw new FileNotFoundException("Configured ffprobe path does not exist", ffprobePath);
            }

            if (!File.Exists(queueItem.SourcePath))
            {
                throw new FileNotFoundException("Source media file no longer exists", queueItem.SourcePath);
            }

            Directory.CreateDirectory(FileSystemLayout.CopyPrepFolder);
            Directory.CreateDirectory(FileSystemLayout.CopyPrepWorkingFolder);
            Directory.CreateDirectory(FileSystemLayout.CopyPrepArchiveFolder);
            Directory.CreateDirectory(FileSystemLayout.CopyPrepLogsFolder);

            string sourcePath = queueItem.SourcePath;
            string targetPath = BuildTargetPath(sourcePath);
            string archiveDirectory = Path.Combine(
                FileSystemLayout.CopyPrepArchiveFolder,
                queueItem.Id.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(archiveDirectory);

            string archivePath = Path.Combine(archiveDirectory, Path.GetFileName(sourcePath));
            string workingPath = Path.Combine(
                FileSystemLayout.CopyPrepWorkingFolder,
                $"copy-prep-{queueItem.Id:D8}.mp4");
            string logPath = Path.Combine(
                FileSystemLayout.CopyPrepLogsFolder,
                $"copy-prep-{queueItem.Id:D8}-attempt-{queueItem.AttemptCount + 1:D2}.log");

            if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                throw new IOException($"Target path already exists: {targetPath}");
            }

            if (File.Exists(workingPath))
            {
                File.Delete(workingPath);
            }

            queueItem.WorkingPath = workingPath;
            queueItem.TargetPath = targetPath;
            queueItem.ArchivePath = archivePath;
            queueItem.LastLogPath = logPath;
            queueItem.AttemptCount += 1;

            string[] ffmpegArguments = BuildArguments(queueItem, sourcePath, workingPath, settings.ThreadsPerJob);
            queueItem.LastCommand = BuildCommandLine(ffmpegPath, ffmpegArguments);
            queueItem.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await AddLogEntry(dbContext, queueItem.Id, "Information", "ffmpeg_started", "Started FFmpeg preprocessing", cancellationToken);

            int exitCode = await RunFfmpeg(ffmpegPath, ffmpegArguments, logPath, cancellationToken);
            queueItem.LastExitCode = exitCode;
            queueItem.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg exited with code {exitCode}");
            }

            if (!File.Exists(workingPath))
            {
                throw new FileNotFoundException("FFmpeg finished without creating the prepared output", workingPath);
            }

            queueItem.Status = CopyPrepStatus.Prepared;
            queueItem.CompletedAt = DateTime.UtcNow;
            queueItem.UpdatedAt = queueItem.CompletedAt.Value;
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddLogEntry(dbContext, queueItem.Id, "Information", "ffmpeg_completed", "Prepared output created successfully", cancellationToken);

            ReplacePaths(sourcePath, targetPath, workingPath, archivePath);

            MediaItem mediaItem = await LoadMediaItemForUpdate(dbContext, queueItem.MediaItemId, cancellationToken)
                ?? throw new InvalidOperationException($"Unable to reload media item {queueItem.MediaItemId} for copy-prep replacement");

            MediaFile mediaFile = mediaItem.GetHeadVersion().MediaFiles.Head();
            mediaFile.Path = targetPath;
            mediaFile.PathHash = PathUtils.GetPathHash(targetPath);
            await dbContext.SaveChangesAsync(cancellationToken);

            Either<BaseError, bool> refreshResult = await localStatisticsProvider.RefreshStatistics(
                ffmpegPath,
                ffprobePath,
                mediaItem);

            foreach (BaseError error in refreshResult.LeftToSeq())
            {
                queueItem.LastError = $"Statistics refresh warning: {error.Value}";
                await AddLogEntry(
                    dbContext,
                    queueItem.Id,
                    "Warning",
                    "statistics_refresh_warning",
                    error.Value,
                    cancellationToken);
            }

            queueItem.Status = CopyPrepStatus.Replaced;
            queueItem.ReplacedAt = DateTime.UtcNow;
            queueItem.UpdatedAt = queueItem.ReplacedAt.Value;
            await dbContext.SaveChangesAsync(cancellationToken);
            await AddLogEntry(dbContext, queueItem.Id, "Information", "replacement_completed", "Prepared media replaced the active library file", cancellationToken);

            logger.LogInformation(
                "Completed copy prep for media item {MediaItemId}; source {SourcePath} prepared to {TargetPath}",
                queueItem.MediaItemId,
                sourcePath,
                targetPath);
        }
        catch (Exception ex) when (ex is not (TaskCanceledException or OperationCanceledException))
        {
            await MarkFailed(queueItemId, ex, cancellationToken);
            logger.LogWarning(ex, "Copy prep failed for queue item {QueueItemId}", queueItemId);
        }
    }

    private static string BuildTargetPath(string sourcePath)
    {
        string extension = Path.GetExtension(sourcePath);
        if (string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return sourcePath;
        }

        string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
        return Path.Combine(directory, fileNameWithoutExtension + ".mp4");
    }

    private static string[] BuildArguments(
        CopyPrepQueueItem queueItem,
        string sourcePath,
        string workingPath,
        int threadsPerJob)
    {
        string fps = NormalizeFrameRate(queueItem.MediaVersion?.RFrameRate);
        int gop = Math.Clamp((int)Math.Round(ParseFrameRate(fps), MidpointRounding.AwayFromZero), 1, 240);

        string filter = $"scale=trunc(iw/2)*2:trunc(ih/2)*2,setsar=1,fps={fps}";

        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-progress", "pipe:1",
            "-nostats",
            "-i", sourcePath,
            "-map", "0:v:0",
            "-map", "0:a:0?",
            "-vf", filter,
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "18",
            "-pix_fmt", "yuv420p",
            "-profile:v", "high",
            "-level", "4.1",
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            "-bf", "0",
            "-force_key_frames", "expr:gte(t,n_forced*1)",
            "-c:a", "aac",
            "-b:a", "192k",
            "-ar", "48000",
            "-ac", "2",
            "-movflags", "+faststart",
            "-threads", threadsPerJob.ToString(CultureInfo.InvariantCulture),
            workingPath
        };

        return arguments.ToArray();
    }

    private static string NormalizeFrameRate(string frameRate) =>
        string.IsNullOrWhiteSpace(frameRate) || frameRate == "0/0"
            ? "25"
            : frameRate;

    private static double ParseFrameRate(string frameRate)
    {
        if (string.IsNullOrWhiteSpace(frameRate))
        {
            return 25;
        }

        if (frameRate.Contains('/'))
        {
            string[] parts = frameRate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out double numerator) &&
                double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out double denominator) &&
                denominator > 0)
            {
                return numerator / denominator;
            }
        }

        return double.TryParse(frameRate, NumberStyles.Number, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : 25;
    }

    private static string BuildCommandLine(string executable, IEnumerable<string> arguments)
    {
        var builder = new StringBuilder();
        builder.Append(Quote(executable));
        foreach (string argument in arguments)
        {
            builder.Append(' ');
            builder.Append(Quote(argument));
        }

        return builder.ToString();
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.IndexOfAny([' ', '\t', '\n', '\r', '"']) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }

    private static async Task<int> RunFfmpeg(
        string ffmpegPath,
        IEnumerable<string> arguments,
        string logPath,
        CancellationToken cancellationToken)
    {
        await using var logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8);

        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (logWriter)
                {
                    logWriter.WriteLine(args.Data);
                    logWriter.Flush();
                }
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                lock (logWriter)
                {
                    logWriter.WriteLine(args.Data);
                    logWriter.Flush();
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch
            {
                // ignored
            }

            throw;
        }

        return process.ExitCode;
    }

    private static void ReplacePaths(string sourcePath, string targetPath, string workingPath, string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        File.Move(sourcePath, archivePath);

        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(workingPath, targetPath);
        }
        catch
        {
            try
            {
                if (!File.Exists(sourcePath) && File.Exists(archivePath))
                {
                    File.Move(archivePath, sourcePath);
                }
            }
            catch
            {
                // ignored during rollback attempt
            }

            throw;
        }
    }

    private static async Task<MediaItem> LoadMediaItemForUpdate(
        TvContext dbContext,
        int mediaItemId,
        CancellationToken cancellationToken) =>
        await dbContext.MediaItems
            .Include(i => (i as Movie).MediaVersions)
            .ThenInclude(mv => mv.MediaFiles)
            .Include(i => (i as Episode).MediaVersions)
            .ThenInclude(mv => mv.MediaFiles)
            .Include(i => (i as MusicVideo).MediaVersions)
            .ThenInclude(mv => mv.MediaFiles)
            .Include(i => (i as OtherVideo).MediaVersions)
            .ThenInclude(mv => mv.MediaFiles)
            .FirstOrDefaultAsync(i => i.Id == mediaItemId, cancellationToken);

    private static async Task AddLogEntry(
        TvContext dbContext,
        int queueItemId,
        string level,
        string eventName,
        string message,
        CancellationToken cancellationToken)
    {
        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = queueItemId,
            CreatedAt = DateTime.UtcNow,
            Level = level,
            Event = eventName,
            Message = message
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailed(int queueItemId, Exception ex, CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceScopeFactory.CreateScope();
        IDbContextFactory<TvContext> dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TvContext>>();

        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .FirstOrDefaultAsync(item => item.Id == queueItemId, cancellationToken);

        if (queueItem is null)
        {
            return;
        }

        queueItem.Status = CopyPrepStatus.Failed;
        queueItem.LastError = ex.Message;
        queueItem.FailedAt = DateTime.UtcNow;
        queueItem.UpdatedAt = queueItem.FailedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = queueItemId,
            CreatedAt = DateTime.UtcNow,
            Level = "Error",
            Event = "processing_failed",
            Message = ex.Message,
            Details = ex.ToString()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed record CopyPrepSettings(bool Enabled, int CpuTargetPercent, int MaxConcurrentJobs, int ThreadsPerJob);
}
