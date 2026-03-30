using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.CopyPrep.Commands;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Infrastructure;
using ErsatzTV.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Services;

[TestFixture]
public class AddItemsToCopyPrepHandlerTests
{
    [Test]
    public async Task Should_queue_new_eligible_item_and_write_search_selection_log()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            fileName: "queue-me.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new AddItemsToCopyPrepHandler(factory);

        AddItemsToCopyPrepResult result = await sut.Handle(
            new AddItemsToCopyPrep([movieId], []),
            CancellationToken.None);

        result.QueuedCount.ShouldBe(1);
        result.RetriedCount.ShouldBe(0);
        result.SkippedCopyReadyCount.ShouldBe(0);
        result.SkippedExistingActiveCount.ShouldBe(0);
        result.SkippedUnsupportedCount.ShouldBe(0);
        result.SkippedMissingCount.ShouldBe(0);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        dbContext.CopyPrepQueueItems.Count(item => item.MediaItemId == movieId).ShouldBe(1);
        dbContext.CopyPrepQueueItems.ShouldContain(item => item.MediaItemId == movieId && item.Status == CopyPrepStatus.Queued);
        dbContext.CopyPrepQueueLogEntries.ShouldContain(log => log.Event == "queued_from_search_selection");
    }

    [Test]
    public async Task Should_queue_new_eligible_other_video_and_write_search_selection_log()
    {
        await using var factory = await TestDbContextFactory.Create();
        int otherVideoId = await factory.SeedOtherVideo(
            fileName: "queue-other-video.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new AddItemsToCopyPrepHandler(factory);

        AddItemsToCopyPrepResult result = await sut.Handle(
            new AddItemsToCopyPrep([], [otherVideoId]),
            CancellationToken.None);

        result.QueuedCount.ShouldBe(1);
        result.RetriedCount.ShouldBe(0);
        result.SkippedCopyReadyCount.ShouldBe(0);
        result.SkippedExistingActiveCount.ShouldBe(0);
        result.SkippedUnsupportedCount.ShouldBe(0);
        result.SkippedMissingCount.ShouldBe(0);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        dbContext.CopyPrepQueueItems.Count(item => item.MediaItemId == otherVideoId).ShouldBe(1);
        dbContext.CopyPrepQueueItems.ShouldContain(item => item.MediaItemId == otherVideoId && item.Status == CopyPrepStatus.Queued);
        dbContext.CopyPrepQueueLogEntries.ShouldContain(log => log.Event == "queued_from_search_selection");
    }

    [Test]
    public async Task Should_requeue_failed_item_without_creating_duplicate_row()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            fileName: "retry-me.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");
        int queueItemId = await factory.SeedQueueItemForMovie(movieId, CopyPrepStatus.Failed);

        var sut = new AddItemsToCopyPrepHandler(factory);

        AddItemsToCopyPrepResult result = await sut.Handle(
            new AddItemsToCopyPrep([movieId], []),
            CancellationToken.None);

        result.QueuedCount.ShouldBe(0);
        result.RetriedCount.ShouldBe(1);
        result.SkippedCopyReadyCount.ShouldBe(0);
        result.SkippedExistingActiveCount.ShouldBe(0);
        result.SkippedUnsupportedCount.ShouldBe(0);
        result.SkippedMissingCount.ShouldBe(0);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        dbContext.CopyPrepQueueItems.Count(item => item.MediaItemId == movieId).ShouldBe(1);
        dbContext.CopyPrepQueueItems.Single(item => item.Id == queueItemId).Status.ShouldBe(CopyPrepStatus.Queued);
        dbContext.CopyPrepQueueLogEntries.ShouldContain(log => log.Event == "manual_retry_from_search_selection");
    }

    [Test]
    public async Task Should_refresh_retried_item_to_current_primary_file()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            fileName: "retry-source-old.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");
        int queueItemId = await factory.SeedQueueItemForMovie(movieId, CopyPrepStatus.Failed);
        (int originalMediaVersionId, int originalMediaFileId, string originalSourcePath) =
            await factory.GetQueueItemPointers(queueItemId);
        (int currentMediaVersionId, int currentMediaFileId, string currentSourcePath) =
            await factory.PrependMovieHeadVersion(movieId, "retry-source-current.mkv");

        var sut = new AddItemsToCopyPrepHandler(factory);

        AddItemsToCopyPrepResult result = await sut.Handle(
            new AddItemsToCopyPrep([movieId], []),
            CancellationToken.None);

        result.RetriedCount.ShouldBe(1);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem queueItem = dbContext.CopyPrepQueueItems.Single(item => item.Id == queueItemId);
        queueItem.MediaVersionId.ShouldBe(currentMediaVersionId);
        queueItem.MediaFileId.ShouldBe(currentMediaFileId);
        queueItem.SourcePath.ShouldBe(currentSourcePath);
        queueItem.TargetPath.ShouldBe(Path.ChangeExtension(currentSourcePath, ".mp4"));
        queueItem.ArchivePath.ShouldBe(Path.Combine(Path.GetDirectoryName(currentSourcePath) ?? string.Empty, "archive", Path.GetFileName(currentSourcePath)));
        queueItem.Reason.ShouldContain("container/extension .mkv is not MP4/M4V");

        queueItem.MediaVersionId.ShouldNotBe(originalMediaVersionId);
        queueItem.MediaFileId.ShouldNotBe(originalMediaFileId);
        queueItem.SourcePath.ShouldNotBe(originalSourcePath);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<TvContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<TvContext> _options;
        private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private readonly SlowQueryInterceptor _slowQueryInterceptor = new(NullLogger<SlowQueryInterceptor>.Instance);
        private readonly string _mediaRoot;
        private int _nextPathHash = 1;

        private TestDbContextFactory(SqliteConnection connection, string mediaRoot)
        {
            _connection = connection;
            _mediaRoot = mediaRoot;
            _options = new DbContextOptionsBuilder<TvContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public static async Task<TestDbContextFactory> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            string mediaRoot = Path.Combine(Path.GetTempPath(), $"etv-copy-prep-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(mediaRoot);

            var factory = new TestDbContextFactory(connection, mediaRoot);
            await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
            await dbContext.Database.EnsureCreatedAsync(CancellationToken.None);
            return factory;
        }

        public async Task<int> SeedMovie(
            string fileName,
            string videoCodec,
            string audioCodec,
            string pixelFormat,
            string sampleAspectRatio,
            string frameRate)
        {
            DateTime now = DateTime.UtcNow;
            string filePath = Path.Combine(_mediaRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, string.Empty);

            await using TvContext dbContext = await CreateDbContextAsync(CancellationToken.None);

            var mediaSource = new LocalMediaSource();
            var library = new LocalLibrary
            {
                Name = "Library",
                MediaSource = mediaSource
            };
            var libraryPath = new LibraryPath
            {
                Path = _mediaRoot,
                Library = library
            };
            var mediaFile = new MediaFile
            {
                Path = filePath,
                PathHash = $"hash-{_nextPathHash++}"
            };
            var mediaVersion = new MediaVersion
            {
                Name = "version",
                MediaFiles = [mediaFile],
                Streams =
                [
                    new MediaStream
                    {
                        Index = 0,
                        Codec = videoCodec,
                        MediaStreamKind = MediaStreamKind.Video,
                        PixelFormat = pixelFormat,
                        BitsPerRawSample = 8
                    },
                    new MediaStream
                    {
                        Index = 1,
                        Codec = audioCodec,
                        MediaStreamKind = MediaStreamKind.Audio
                    }
                ],
                Chapters = [],
                SampleAspectRatio = sampleAspectRatio,
                RFrameRate = frameRate,
                VideoScanKind = VideoScanKind.Progressive,
                DateAdded = now,
                DateUpdated = now
            };
            var movie = new Movie
            {
                LibraryPath = libraryPath,
                MediaVersions = [mediaVersion],
                MovieMetadata = []
            };

            dbContext.MediaItems.Add(movie);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            return movie.Id;
        }

        public async Task<int> SeedOtherVideo(
            string fileName,
            string videoCodec,
            string audioCodec,
            string pixelFormat,
            string sampleAspectRatio,
            string frameRate)
        {
            DateTime now = DateTime.UtcNow;
            string filePath = Path.Combine(_mediaRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, string.Empty);

            await using TvContext dbContext = await CreateDbContextAsync(CancellationToken.None);

            var mediaSource = new LocalMediaSource();
            var library = new LocalLibrary
            {
                Name = "Library",
                MediaSource = mediaSource
            };
            var libraryPath = new LibraryPath
            {
                Path = _mediaRoot,
                Library = library
            };
            var mediaFile = new MediaFile
            {
                Path = filePath,
                PathHash = $"hash-{_nextPathHash++}"
            };
            var mediaVersion = new MediaVersion
            {
                Name = "version",
                MediaFiles = [mediaFile],
                Streams =
                [
                    new MediaStream
                    {
                        Index = 0,
                        Codec = videoCodec,
                        MediaStreamKind = MediaStreamKind.Video,
                        PixelFormat = pixelFormat,
                        BitsPerRawSample = 8
                    },
                    new MediaStream
                    {
                        Index = 1,
                        Codec = audioCodec,
                        MediaStreamKind = MediaStreamKind.Audio
                    }
                ],
                Chapters = [],
                SampleAspectRatio = sampleAspectRatio,
                RFrameRate = frameRate,
                VideoScanKind = VideoScanKind.Progressive,
                DateAdded = now,
                DateUpdated = now
            };
            var otherVideo = new OtherVideo
            {
                LibraryPath = libraryPath,
                MediaVersions = [mediaVersion],
                OtherVideoMetadata = []
            };

            dbContext.MediaItems.Add(otherVideo);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            return otherVideo.Id;
        }

        public async Task<int> SeedQueueItemForMovie(int mediaItemId, CopyPrepStatus status)
        {
            DateTime now = DateTime.UtcNow;

            await using TvContext dbContext = await CreateDbContextAsync(CancellationToken.None);
            Movie movie = await dbContext.Movies
                .Include(m => m.MediaVersions)
                    .ThenInclude(version => version.MediaFiles)
                .SingleAsync(m => m.Id == mediaItemId, CancellationToken.None);

            MediaVersion mediaVersion = movie.MediaVersions.Single();
            MediaFile mediaFile = mediaVersion.MediaFiles.Single();

            var queueItem = new CopyPrepQueueItem
            {
                MediaItemId = mediaItemId,
                MediaVersionId = mediaVersion.Id,
                MediaFileId = mediaFile.Id,
                Status = status,
                Reason = "Needs prep",
                SourcePath = mediaFile.Path,
                TargetPath = Path.ChangeExtension(mediaFile.Path, ".mp4"),
                ArchivePath = Path.Combine(_mediaRoot, "archive", Path.GetFileName(mediaFile.Path)),
                AttemptCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
                QueuedAt = now,
                StartedAt = status is CopyPrepStatus.Processing or CopyPrepStatus.Prepared or CopyPrepStatus.Replaced ? now : null,
                FailedAt = status == CopyPrepStatus.Failed ? now : null,
                CanceledAt = status == CopyPrepStatus.Canceled ? now : null,
                CompletedAt = status is CopyPrepStatus.Prepared or CopyPrepStatus.Replaced ? now : null,
                ReplacedAt = status == CopyPrepStatus.Replaced ? now : null,
                LogEntries = []
            };

            dbContext.CopyPrepQueueItems.Add(queueItem);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            return queueItem.Id;
        }

        public async Task<(int MediaVersionId, int MediaFileId, string SourcePath)> GetQueueItemPointers(int queueItemId)
        {
            await using TvContext dbContext = await CreateDbContextAsync(CancellationToken.None);
            return await dbContext.CopyPrepQueueItems
                .Where(item => item.Id == queueItemId)
                .Select(item => new ValueTuple<int, int, string>(item.MediaVersionId, item.MediaFileId, item.SourcePath))
                .SingleAsync(CancellationToken.None);
        }

        public async Task<(int MediaVersionId, int MediaFileId, string SourcePath)> PrependMovieHeadVersion(int mediaItemId, string fileName)
        {
            DateTime now = DateTime.UtcNow;
            string filePath = Path.Combine(_mediaRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(filePath, string.Empty);

            await using TvContext dbContext = await CreateDbContextAsync(CancellationToken.None);
            Movie movie = await dbContext.Movies
                .Include(m => m.MediaVersions)
                    .ThenInclude(version => version.MediaFiles)
                .Include(m => m.MediaVersions)
                    .ThenInclude(version => version.Streams)
                .SingleAsync(m => m.Id == mediaItemId, CancellationToken.None);

            MediaVersion currentHeadVersion = movie.MediaVersions[0];
            var replacementVersion = new MediaVersion
            {
                Name = "replacement-version",
                MediaFiles =
                [
                    new MediaFile
                    {
                        Path = filePath,
                        PathHash = $"hash-{_nextPathHash++}"
                    }
                ],
                Streams = currentHeadVersion.Streams
                    .Select(stream => new MediaStream
                    {
                        Index = stream.Index,
                        Codec = stream.Codec,
                        MediaStreamKind = stream.MediaStreamKind,
                        PixelFormat = stream.PixelFormat,
                        BitsPerRawSample = stream.BitsPerRawSample
                    })
                    .ToList(),
                Chapters = [],
                SampleAspectRatio = currentHeadVersion.SampleAspectRatio,
                RFrameRate = currentHeadVersion.RFrameRate,
                VideoScanKind = currentHeadVersion.VideoScanKind,
                DateAdded = now,
                DateUpdated = now
            };

            movie.MediaVersions.Insert(0, replacementVersion);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            return (replacementVersion.Id, replacementVersion.MediaFiles.Single().Id, filePath);
        }

        public TvContext CreateDbContext() => new(_options, _loggerFactory, _slowQueryInterceptor);

        public Task<TvContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();

            if (Directory.Exists(_mediaRoot))
            {
                Directory.Delete(_mediaRoot, recursive: true);
            }
        }
    }
}
