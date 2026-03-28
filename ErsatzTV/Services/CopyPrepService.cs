using System.Diagnostics;
using System.Globalization;
using System.Text;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.CopyPrep;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Core.Extensions;
using ErsatzTV.Core.FFmpeg;
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

            queueItem.WorkingPath = workingPath;
            queueItem.TargetPath = targetPath;
            queueItem.ArchivePath = archivePath;
            queueItem.AttemptCount += 1;

            if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(targetPath))
            {
                CopyPrepOutputValidationResult existingTargetValidation = await ValidatePreparedOutput(
                    localStatisticsProvider,
                    ffprobePath,
                    queueItem.MediaVersion,
                    targetPath);

                if (existingTargetValidation.IsValid)
                {
                    queueItem.LastCommand = null;
                    queueItem.LastLogPath = null;
                    queueItem.LastExitCode = null;
                    queueItem.LastError = null;

                    if (!await TryMarkPrepared(dbContext, queueItem, logger, cancellationToken))
                    {
                        return;
                    }

                    await AddLogEntry(
                        dbContext,
                        queueItem.Id,
                        "Information",
                        "existing_target_validated",
                        "Existing prepared target passed validation and will be reused",
                        cancellationToken,
                        existingTargetValidation.Summary);

                    ArchiveSourceFile(sourcePath, archivePath);

                    await FinalizePreparedTarget(
                        dbContext,
                        localStatisticsProvider,
                        ffmpegPath,
                        ffprobePath,
                        queueItem,
                        "existing_target_reused",
                        "Validated prepared target already existed; skipped FFmpeg preprocessing",
                        logger,
                        cancellationToken);

                    logger.LogInformation(
                        "Reused existing prepared target for media item {MediaItemId}; archived source {SourcePath} and kept target {TargetPath}",
                        queueItem.MediaItemId,
                        sourcePath,
                        targetPath);
                    return;
                }

                await AddLogEntry(
                    dbContext,
                    queueItem.Id,
                    "Warning",
                    "existing_target_invalid",
                    "Existing prepared target failed validation and will be replaced",
                    cancellationToken,
                    existingTargetValidation.Summary);
            }

            if (File.Exists(workingPath))
            {
                File.Delete(workingPath);
            }

            queueItem.LastLogPath = logPath;
            bool hasAudioStream = queueItem.MediaVersion?.Streams?.Any(s => s.MediaStreamKind == MediaStreamKind.Audio) ?? true;
            string[] ffmpegArguments = CopyPrepTranscodeProfile.BuildArguments(
                sourcePath,
                workingPath,
                queueItem.MediaVersion?.RFrameRate,
                hasAudioStream,
                settings.ThreadsPerJob);
            queueItem.LastCommand = FFmpegCommandLine.Format(ffmpegPath, ffmpegArguments);
            queueItem.UpdatedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogDebug("Copy prep ffmpeg command line {CommandLine}", queueItem.LastCommand);
            await AddLogEntry(
                dbContext,
                queueItem.Id,
                "Information",
                "ffmpeg_started",
                "Started FFmpeg preprocessing",
                cancellationToken,
                queueItem.LastCommand);

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

            CopyPrepOutputValidationResult preparedOutputValidation = await ValidatePreparedOutput(
                localStatisticsProvider,
                ffprobePath,
                queueItem.MediaVersion,
                workingPath);
            if (!preparedOutputValidation.IsValid)
            {
                await AddLogEntry(
                    dbContext,
                    queueItem.Id,
                    "Error",
                    "prepared_output_invalid",
                    "Prepared output failed validation",
                    cancellationToken,
                    preparedOutputValidation.Summary);
                throw new InvalidOperationException($"Prepared output validation failed: {preparedOutputValidation.Summary}");
            }

            await AddLogEntry(
                dbContext,
                queueItem.Id,
                "Information",
                "prepared_output_validated",
                "Prepared output passed validation",
                cancellationToken,
                preparedOutputValidation.Summary);

            if (!await TryMarkPrepared(dbContext, queueItem, logger, cancellationToken))
            {
                return;
            }

            await AddLogEntry(dbContext, queueItem.Id, "Information", "ffmpeg_completed", "Prepared output created successfully", cancellationToken);

            if (await AbortIfCanceled(
                    dbContext,
                    queueItem,
                    logger,
                    "prepared target replacement/finalization",
                    cancellationToken))
            {
                return;
            }

            ReplacePaths(sourcePath, targetPath, workingPath, archivePath);

            await FinalizePreparedTarget(
                dbContext,
                localStatisticsProvider,
                ffmpegPath,
                ffprobePath,
                queueItem,
                "replacement_completed",
                "Prepared media replaced the active library file",
                logger,
                cancellationToken);

            logger.LogInformation(
                "Completed copy prep for media item {MediaItemId}; source {SourcePath} prepared to {TargetPath}",
                queueItem.MediaItemId,
                sourcePath,
                targetPath);
        }
        catch (Exception ex) when (ex is not (TaskCanceledException or OperationCanceledException))
        {
            bool markedFailed = await MarkFailed(queueItemId, ex, cancellationToken);
            if (markedFailed)
            {
                logger.LogWarning(ex, "Copy prep failed for queue item {QueueItemId}", queueItemId);
            }
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

    private static async Task<CopyPrepOutputValidationResult> ValidatePreparedOutput(
        ILocalStatisticsProvider localStatisticsProvider,
        string ffprobePath,
        MediaVersion sourceVersion,
        string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return CopyPrepOutputValidationResult.Failure($"prepared output does not exist: {targetPath}");
        }

        Either<BaseError, MediaVersion> targetStatistics = await localStatisticsProvider.GetStatistics(ffprobePath, targetPath);
        return targetStatistics.Match(
            targetVersion => CopyPrepOutputValidator.Validate(sourceVersion, targetVersion),
            error => CopyPrepOutputValidationResult.Failure($"ffprobe could not read prepared output: {error.Value}"));
    }

    internal static async Task FinalizePreparedTarget(
        TvContext dbContext,
        ILocalStatisticsProvider localStatisticsProvider,
        string ffmpegPath,
        string ffprobePath,
        CopyPrepQueueItem queueItem,
        string eventName,
        string message,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await AbortIfCanceled(dbContext, queueItem, logger, "prepared target finalization", cancellationToken))
        {
            return;
        }

        MediaItem mediaItem = await LoadMediaItemForUpdate(dbContext, queueItem.MediaItemId, cancellationToken)
            ?? throw new InvalidOperationException($"Unable to reload media item {queueItem.MediaItemId} for copy-prep replacement");

        MediaFile mediaFile = mediaItem.GetHeadVersion().MediaFiles.Head();
        mediaFile.Path = queueItem.TargetPath;
        mediaFile.PathHash = PathUtils.GetPathHash(queueItem.TargetPath);
        await dbContext.SaveChangesAsync(cancellationToken);

        // RefreshStatistics mutates mediaItem.MediaVersions with entities loaded from a different DbContext.
        // Detach the current graph first so EF Core in this context does not try to track duplicate MediaVersion instances.
        dbContext.ChangeTracker.Clear();

        Either<BaseError, bool> refreshResult = await localStatisticsProvider.RefreshStatistics(
            ffmpegPath,
            ffprobePath,
            mediaItem);

        CopyPrepQueueItem trackedQueueItem = await dbContext.CopyPrepQueueItems
            .FirstOrDefaultAsync(item => item.Id == queueItem.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Unable to reload copy-prep queue item {queueItem.Id} for finalization");

        if (trackedQueueItem.Status == CopyPrepStatus.Canceled)
        {
            await AddCancellationObservedLog(dbContext, trackedQueueItem.Id, logger, "prepared target finalization", cancellationToken);
            return;
        }

        foreach (BaseError error in refreshResult.LeftToSeq())
        {
            trackedQueueItem.LastError = $"Statistics refresh warning: {error.Value}";
            await AddLogEntry(
                dbContext,
                trackedQueueItem.Id,
                "Warning",
                "statistics_refresh_warning",
                error.Value,
                cancellationToken);
        }

        trackedQueueItem.Status = CopyPrepStatus.Replaced;
        trackedQueueItem.ReplacedAt = DateTime.UtcNow;
        trackedQueueItem.UpdatedAt = trackedQueueItem.ReplacedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddLogEntry(dbContext, trackedQueueItem.Id, "Information", eventName, message, cancellationToken);
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

    private static void ArchiveSourceFile(string sourcePath, string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        File.Move(sourcePath, archivePath);
    }

    private static void ReplacePaths(string sourcePath, string targetPath, string workingPath, string archivePath)
    {
        ArchiveSourceFile(sourcePath, archivePath);

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
        CancellationToken cancellationToken,
        string details = null)
    {
        dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
        {
            CopyPrepQueueItemId = queueItemId,
            CreatedAt = DateTime.UtcNow,
            Level = level,
            Event = eventName,
            Message = message,
            Details = details
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> MarkFailed(int queueItemId, Exception ex, CancellationToken cancellationToken)
    {
        using IServiceScope scope = serviceScopeFactory.CreateScope();
        IDbContextFactory<TvContext> dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TvContext>>();

        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await TryMarkFailed(dbContext, queueItemId, ex, logger, cancellationToken);
    }

    internal static async Task<bool> TryMarkPrepared(
        TvContext dbContext,
        CopyPrepQueueItem queueItem,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await AbortIfCanceled(dbContext, queueItem, logger, "prepared state transition", cancellationToken))
        {
            return false;
        }

        queueItem.Status = CopyPrepStatus.Prepared;
        queueItem.CompletedAt = DateTime.UtcNow;
        queueItem.UpdatedAt = queueItem.CompletedAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    internal static async Task<bool> TryMarkFailed(
        TvContext dbContext,
        int queueItemId,
        Exception ex,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .FirstOrDefaultAsync(item => item.Id == queueItemId, cancellationToken);

        if (queueItem is null)
        {
            return false;
        }

        if (queueItem.Status == CopyPrepStatus.Canceled)
        {
            await AddCancellationObservedLog(dbContext, queueItem.Id, logger, "failure handling", cancellationToken);
            return false;
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
        return true;
    }

    private static async Task<bool> AbortIfCanceled(
        TvContext dbContext,
        CopyPrepQueueItem queueItem,
        ILogger logger,
        string followUpStage,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(queueItem).ReloadAsync(cancellationToken);
        if (queueItem.Status != CopyPrepStatus.Canceled)
        {
            return false;
        }

        await AddCancellationObservedLog(dbContext, queueItem.Id, logger, followUpStage, cancellationToken);
        return true;
    }

    private static async Task AddCancellationObservedLog(
        TvContext dbContext,
        int queueItemId,
        ILogger logger,
        string followUpStage,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Copy prep queue item {QueueItemId} was manually canceled; aborting {FollowUpStage}",
            queueItemId,
            followUpStage);

        await AddLogEntry(
            dbContext,
            queueItemId,
            "Information",
            "processing_canceled",
            $"Processing observed manual cancellation and aborted {followUpStage}",
            cancellationToken);
    }

    private sealed record CopyPrepSettings(bool Enabled, int CpuTargetPercent, int MaxConcurrentJobs, int ThreadsPerJob);
}
