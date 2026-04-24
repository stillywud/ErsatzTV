using System.IO.Abstractions;
using ErsatzTV.Core;
using ErsatzTV.Core.Interfaces.Metadata;

namespace ErsatzTV.Services;

/// <summary>
/// Background service that periodically cleans up old files in the transcode folder
/// to prevent disk space issues.
/// </summary>
public class TranscodeCleanupService : BackgroundService
{
    private readonly ILogger<TranscodeCleanupService> _logger;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    // Clean up files older than this age
    private static readonly TimeSpan MaxFileAge = TimeSpan.FromHours(24);

    // Run cleanup every this often
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);

    // Minimum free space threshold (10 GB) - if below this, be more aggressive
    private const long MinFreeSpaceBytes = 10L * 1024 * 1024 * 1024;

    public TranscodeCleanupService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<TranscodeCleanupService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _logger.LogInformation(
            "Transcode cleanup service started - will clean files older than {MaxAge} every {Interval}",
            MaxFileAge,
            CleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupTranscodeFolder();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during transcode folder cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupTranscodeFolder()
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        var localFileSystem = scope.ServiceProvider.GetRequiredService<ILocalFileSystem>();
        var fileSystem = scope.ServiceProvider.GetRequiredService<IFileSystem>();

        string transcodeFolder = FileSystemLayout.TranscodeFolder;

        if (!fileSystem.Directory.Exists(transcodeFolder))
        {
            _logger.LogDebug("Transcode folder does not exist: {Folder}", transcodeFolder);
            return;
        }

        try
        {
            // Check available space on the drive
            var driveInfo = new DriveInfo(transcodeFolder);
            long freeSpace = driveInfo.AvailableFreeSpace;
            bool lowSpace = freeSpace < MinFreeSpaceBytes;

            if (lowSpace)
            {
                _logger.LogWarning(
                    "Low disk space detected: {FreeSpaceGB:F1} GB remaining on transcode drive",
                    freeSpace / (1024.0 * 1024 * 1024));
            }

            DateTimeOffset cutoffTime = DateTimeOffset.Now.Subtract(MaxFileAge);
            int deletedCount = 0;
            long deletedBytes = 0;

            // Get all channel subdirectories
            foreach (string channelDir in fileSystem.Directory.GetDirectories(transcodeFolder))
            {
                try
                {
                    // Skip the troubleshooting channel
                    string dirName = Path.GetFileName(channelDir);
                    if (dirName == FileSystemLayout.TranscodeTroubleshootingChannel)
                    {
                        continue;
                    }

                    // Check if this is an active streaming session
                    // (we'll check for recent file modifications)
                    var dirInfo = new DirectoryInfo(channelDir);
                    if (dirInfo.LastWriteTimeUtc > DateTime.UtcNow.AddMinutes(-5))
                    {
                        // Directory was modified recently, likely an active session
                        // Only clean very old files (older than 1 hour) from active sessions
                        DateTimeOffset activeSessionCutoff = DateTimeOffset.Now.Subtract(TimeSpan.FromHours(1));
                        (int count, long bytes) = CleanupDirectory(
                            fileSystem,
                            channelDir,
                            lowSpace ? DateTimeOffset.Now.Subtract(TimeSpan.FromMinutes(10)) : activeSessionCutoff);
                        deletedCount += count;
                        deletedBytes += bytes;
                    }
                    else
                    // Directory not modified recently, likely an old session
                    // Clean all files
                    {
                        (int count, long bytes) = CleanupDirectory(fileSystem, channelDir, DateTimeOffset.MaxValue);
                        deletedCount += count;
                        deletedBytes += bytes;

                        // Try to remove empty directory
                        try
                        {
                            if (fileSystem.Directory.GetFiles(channelDir).Length == 0 &&
                                fileSystem.Directory.GetDirectories(channelDir).Length == 0)
                            {
                                fileSystem.Directory.Delete(channelDir);
                                _logger.LogDebug("Removed empty channel directory: {Dir}", channelDir);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to remove channel directory: {Dir}", channelDir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error cleaning channel directory: {Dir}", channelDir);
                }
            }

            // Also clean up /var/tmp/storage* files (HLS temp files)
            (int varTmpCount, long varTmpBytes) = CleanupVarTmpStorage();
            deletedCount += varTmpCount;
            deletedBytes += varTmpBytes;

            if (deletedCount > 0)
            {
                _logger.LogInformation(
                    "Transcode cleanup complete: deleted {Count} files ({SizeMB:F1} MB)",
                    deletedCount,
                    deletedBytes / (1024.0 * 1024));
            }
            else
            {
                _logger.LogDebug("Transcode cleanup complete: no files to delete");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during transcode folder cleanup");
        }
    }

    private (int count, long bytes) CleanupDirectory(IFileSystem fileSystem, string directory, DateTimeOffset cutoffTime)
    {
        int deletedCount = 0;
        long deletedBytes = 0;

        foreach (string file in fileSystem.Directory.GetFiles(directory))
        {
            try
            {
                var fileInfo = fileSystem.FileInfo.New(file);

                // Skip files that are still being written (check if locked)
                if (IsFileLocked(file))
                {
                    continue;
                }

                // Delete if older than cutoff
                if (fileInfo.LastWriteTimeUtc < cutoffTime.UtcDateTime)
                {
                    long fileSize = fileInfo.Length;
                    fileSystem.File.Delete(file);
                    deletedCount++;
                    deletedBytes += fileSize;
                }
            }
            catch (IOException)
            {
                // File may be in use, skip it
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to delete file: {File}", file);
            }
        }

        return (deletedCount, deletedBytes);
    }

    private (int count, long bytes) CleanupVarTmpStorage()
    {
        int deletedCount = 0;
        long deletedBytes = 0;

        try
        {
            if (!Directory.Exists("/var/tmp"))
            {
                return (0, 0);
            }

            foreach (string dir in Directory.GetDirectories("/var/tmp", "storage*"))
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    // Only delete directories older than 1 hour
                    if (dirInfo.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1))
                    {
                        long dirSize = GetDirectorySize(dir);
                        Directory.Delete(dir, true);
                        deletedCount++;
                        deletedBytes += dirSize;
                        _logger.LogDebug("Cleaned up old storage temp directory: {Dir}", dir);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to delete storage temp directory: {Dir}", dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error cleaning /var/tmp/storage* directories");
        }

        return (deletedCount, deletedBytes);
    }

    private static bool IsFileLocked(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch
                {
                    // Ignore files we can't access
                }
            }
        }
        catch
        {
            // Ignore directories we can't access
        }

        return size;
    }
}
