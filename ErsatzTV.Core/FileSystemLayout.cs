using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;

namespace ErsatzTV.Core;

public static class FileSystemLayout
{
    public static readonly string AppDataFolder;

    public static readonly string TranscodeFolder;
    public static readonly string TranscodeTroubleshootingChannel;
    public static readonly string TranscodeTroubleshootingFolder;

    public static readonly string DataProtectionFolder;
    public static readonly string LogsFolder;

    public static readonly string DatabasePath;
    public static readonly string LogFilePath;

    public static readonly string LegacyImageCacheFolder;
    public static readonly string ResourcesCacheFolder;
    public static readonly string ChannelGuideCacheFolder;

    public static readonly string PlexSecretsPath;
    public static readonly string JellyfinSecretsPath;
    public static readonly string EmbySecretsPath;

    public static readonly string FFmpegReportsFolder;
    public static readonly string SearchIndexFolder;
    public static readonly string TempFilePoolFolder;
    public static readonly string CopyPrepFolder;
    public static readonly string CopyPrepWorkingFolder;
    public static readonly string CopyPrepArchiveFolder;
    public static readonly string CopyPrepLogsFolder;

    public static readonly string ArtworkCacheFolder;

    public static readonly string PosterCacheFolder;
    public static readonly string ThumbnailCacheFolder;
    public static readonly string LogoCacheFolder;
    public static readonly string FanArtCacheFolder;
    public static readonly string WatermarkCacheFolder;

    public static readonly string StreamsCacheFolder;

    public static readonly string SubtitleCacheFolder;
    public static readonly string FontsCacheFolder;

    public static readonly string TemplatesFolder;

    public static readonly string MusicVideoCreditsTemplatesFolder;

    public static readonly string ChannelGuideTemplatesFolder;

    public static readonly string GraphicsElementsTemplatesFolder;
    public static readonly string GraphicsElementsTextTemplatesFolder;
    public static readonly string GraphicsElementsImageTemplatesFolder;
    public static readonly string GraphicsElementsScriptTemplatesFolder;
    public static readonly string GraphicsElementsSubtitleTemplatesFolder;
    public static readonly string GraphicsElementsMotionTemplatesFolder;

    public static readonly string ScriptsFolder;

    public static readonly string MultiEpisodeShuffleTemplatesFolder;

    public static readonly string AudioStreamSelectorScriptsFolder;

    public static readonly string ChannelStreamSelectorsFolder;

    public static readonly string MpegTsScriptsFolder;

    public static readonly string DefaultMpegTsScriptFolder;

    private static readonly string[] CommonMountPoints = new[]
    {
        "/mnt",
        "/media",
        "/data",
        "/storage",
        "/opt",
        "/var/lib",
        "/home"
    };

    public static readonly string MacOsOldAppDataFolder = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? string.Empty,
        ".local",
        "share",
        "ersatztv");

    public static readonly string MacOsOldDatabasePath = Path.Combine(MacOsOldAppDataFolder, "ersatztv.sqlite3");

    static FileSystemLayout()
    {
        string version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        bool isDocker = version.Contains("docker", StringComparison.OrdinalIgnoreCase);

        string defaultConfigFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            "ersatztv");

        string customConfigFolder = SystemEnvironment.ConfigFolder;
        bool useCustomConfigFolder = !string.IsNullOrWhiteSpace(customConfigFolder);

        if (useCustomConfigFolder && isDocker)
        {
            // check for config at old location
            if (Directory.Exists(defaultConfigFolder))
            {
                Log.Logger.Warning(
                    "Ignoring ETV_CONFIG_FOLDER {Folder} and using default {Default}",
                    customConfigFolder,
                    defaultConfigFolder);

                // ignore custom config folder
                useCustomConfigFolder = false;
            }
        }

        AppDataFolder = useCustomConfigFolder ? customConfigFolder : defaultConfigFolder;

        string defaultTranscodeFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolderOption.DoNotVerify),
            "etv-transcode");

        string customTranscodeFolder = SystemEnvironment.TranscodeFolder;
        bool useCustomTranscodeFolder = !string.IsNullOrWhiteSpace(customTranscodeFolder);

        if (useCustomTranscodeFolder && isDocker)
        {
            // check for config at old location
            if (Directory.Exists(defaultTranscodeFolder))
            {
                Log.Logger.Warning(
                    "Ignoring ETV_TRANSCODE_FOLDER {Folder} and using default {Default}",
                    customTranscodeFolder,
                    defaultTranscodeFolder);

                // ignore custom config folder
                useCustomTranscodeFolder = false;
            }
        }

        if (!useCustomTranscodeFolder && !isDocker && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Auto-detect disk with most available space on Linux
            string bestTranscodePath = FindBestTranscodePath();
            if (!string.IsNullOrWhiteSpace(bestTranscodePath))
            {
                defaultTranscodeFolder = bestTranscodePath;
                Log.Logger.Information(
                    "Auto-selected transcode folder with most available space: {Folder}",
                    defaultTranscodeFolder);
            }
        }

        TranscodeFolder = useCustomTranscodeFolder ? customTranscodeFolder : defaultTranscodeFolder;
        TranscodeTroubleshootingChannel = ".troubleshooting";
        TranscodeTroubleshootingFolder = Path.Combine(TranscodeFolder, TranscodeTroubleshootingChannel);

        DataProtectionFolder = Path.Combine(AppDataFolder, "data-protection");
        LogsFolder = Path.Combine(AppDataFolder, "logs");

        DatabasePath = Path.Combine(AppDataFolder, "ersatztv.sqlite3");
        LogFilePath = Path.Combine(LogsFolder, "ersatztv.log");

        LegacyImageCacheFolder = Path.Combine(AppDataFolder, "cache", "images");
        ResourcesCacheFolder = Path.Combine(AppDataFolder, "cache", "resources");
        ChannelGuideCacheFolder = Path.Combine(AppDataFolder, "cache", "channel-guide");

        PlexSecretsPath = Path.Combine(AppDataFolder, "plex-secrets.json");
        JellyfinSecretsPath = Path.Combine(AppDataFolder, "jellyfin-secrets.json");
        EmbySecretsPath = Path.Combine(AppDataFolder, "emby-secrets.json");

        FFmpegReportsFolder = Path.Combine(AppDataFolder, "ffmpeg-reports");
        SearchIndexFolder = Path.Combine(AppDataFolder, "search-index");
        TempFilePoolFolder = Path.Combine(AppDataFolder, "temp-pool");
        CopyPrepFolder = Path.Combine(AppDataFolder, "copy-prep");
        CopyPrepWorkingFolder = Path.Combine(CopyPrepFolder, "working");
        CopyPrepArchiveFolder = Path.Combine(CopyPrepFolder, "archive");
        CopyPrepLogsFolder = Path.Combine(CopyPrepFolder, "logs");

        ArtworkCacheFolder = Path.Combine(AppDataFolder, "cache", "artwork");

        ArtworkCacheFolder = Path.Combine(AppDataFolder, "cache", "artwork");

        PosterCacheFolder = Path.Combine(ArtworkCacheFolder, "posters");
        ThumbnailCacheFolder = Path.Combine(ArtworkCacheFolder, "thumbnails");
        LogoCacheFolder = Path.Combine(ArtworkCacheFolder, "logos");
        FanArtCacheFolder = Path.Combine(ArtworkCacheFolder, "fanart");
        WatermarkCacheFolder = Path.Combine(ArtworkCacheFolder, "watermarks");

        StreamsCacheFolder = Path.Combine(AppDataFolder, "cache", "streams");

        SubtitleCacheFolder = Path.Combine(StreamsCacheFolder, "subtitles");
        FontsCacheFolder = Path.Combine(StreamsCacheFolder, "fonts");

        TemplatesFolder = Path.Combine(AppDataFolder, "templates");

        MusicVideoCreditsTemplatesFolder = Path.Combine(TemplatesFolder, "music-video-credits");

        ChannelGuideTemplatesFolder = Path.Combine(TemplatesFolder, "channel-guide");

        GraphicsElementsTemplatesFolder = Path.Combine(TemplatesFolder, "graphics-elements");
        GraphicsElementsTextTemplatesFolder = Path.Combine(GraphicsElementsTemplatesFolder, "text");
        GraphicsElementsImageTemplatesFolder = Path.Combine(GraphicsElementsTemplatesFolder, "image");
        GraphicsElementsScriptTemplatesFolder = Path.Combine(GraphicsElementsTemplatesFolder, "script");
        GraphicsElementsSubtitleTemplatesFolder = Path.Combine(GraphicsElementsTemplatesFolder, "subtitle");
        GraphicsElementsMotionTemplatesFolder = Path.Combine(GraphicsElementsTemplatesFolder, "motion");

        ScriptsFolder = Path.Combine(AppDataFolder, "scripts");

        MultiEpisodeShuffleTemplatesFolder = Path.Combine(ScriptsFolder, "multi-episode-shuffle");

        AudioStreamSelectorScriptsFolder = Path.Combine(ScriptsFolder, "audio-stream-selector");

        ChannelStreamSelectorsFolder = Path.Combine(ScriptsFolder, "channel-stream-selectors");

        MpegTsScriptsFolder = Path.Combine(ScriptsFolder, "mpegts");
        DefaultMpegTsScriptFolder = Path.Combine(MpegTsScriptsFolder, "default");
    }

    /// <summary>
    /// Finds the disk with the most available space for the transcode folder.
    /// Checks common mount points like /vol*, /mnt, /media, /data.
    /// Returns null if no suitable disk is found.
    /// </summary>
    private static string FindBestTranscodePath()
    {
        try
        {
            long maxAvailableSpace = 0;
            string bestPath = null;

            // Common mount point patterns to check
            var candidates = new List<string>();

            // Add /vol* directories (like /vol1, /vol2, /vol3)
            if (Directory.Exists("/vol"))
            {
                candidates.AddRange(Directory.GetDirectories("/vol"));
            }

            // Add numbered vol directories directly under root
            for (int i = 1; i <= 10; i++)
            {
                string volPath = $"/vol{i}";
                if (Directory.Exists(volPath))
                {
                    candidates.Add(volPath);
                }
            }

            // Other common mount points
            candidates.AddRange(CommonMountPoints.Where(Directory.Exists));

            // Also check the default temp location
            string tempPath = Path.GetTempPath();
            if (Directory.Exists(tempPath))
            {
                candidates.Add(tempPath);
            }

            foreach (string candidate in candidates)
            {
                try
                {
                    // Skip if not a mount point and not a subdirectory of a mount point we already check
                    var driveInfo = new DriveInfo(candidate);
                    if (driveInfo.IsReady)
                    {
                        long availableSpace = driveInfo.AvailableFreeSpace;
                        Log.Logger.Debug(
                            "Checking transcode candidate {Path}: {AvailableGB:F1} GB available",
                            candidate,
                            availableSpace / (1024.0 * 1024 * 1024));

                        if (availableSpace > maxAvailableSpace)
                        {
                            maxAvailableSpace = availableSpace;
                            bestPath = candidate;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Failed to check disk space for {Path}", candidate);
                }
            }

            if (!string.IsNullOrWhiteSpace(bestPath))
            {
                // Create etv-transcode subdirectory
                string transcodePath = Path.Combine(bestPath, "etv-transcode");
                try
                {
                    Directory.CreateDirectory(transcodePath);
                    return transcodePath;
                }
                catch (Exception ex)
                {
                    Log.Logger.Warning(ex, "Failed to create transcode directory at {Path}", transcodePath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to find best transcode path");
        }

        return string.Empty;
    }
}
