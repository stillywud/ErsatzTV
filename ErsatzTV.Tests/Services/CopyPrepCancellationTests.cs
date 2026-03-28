using ErsatzTV.Application.CopyPrep.Commands;
using ErsatzTV.Core;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Core.Interfaces.Metadata;
using ErsatzTV.Infrastructure;
using ErsatzTV.Infrastructure.Data;
using ErsatzTV.Services;
using LanguageExt;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Services;

[TestFixture]
public class CopyPrepCancellationTests
{
    [Test]
    public async Task Cancel_handler_should_mark_queued_item_canceled_and_write_manual_cancel_log()
    {
        await using var factory = await TestDbContextFactory.Create();
        int queueItemId = await factory.SeedQueueItem(CopyPrepStatus.Queued);

        var sut = new CancelCopyPrepQueueItemHandler(factory);

        bool result = await sut.Handle(new CancelCopyPrepQueueItem(queueItemId), CancellationToken.None);

        result.ShouldBeTrue();

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .Include(item => item.LogEntries)
            .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

        queueItem.Status.ShouldBe(CopyPrepStatus.Canceled);
        queueItem.CanceledAt.ShouldNotBeNull();
        queueItem.UpdatedAt.ShouldBe(queueItem.CanceledAt.Value);
        queueItem.LogEntries.ShouldContain(logEntry =>
            logEntry.Event == "manual_cancel" &&
            logEntry.Message == "Queue item manually canceled");
    }

    [Test]
    public async Task Cancel_handler_should_be_no_op_for_terminal_item()
    {
        await using var factory = await TestDbContextFactory.Create();
        int queueItemId = await factory.SeedQueueItem(CopyPrepStatus.Failed);

        await using (TvContext beforeContext = await factory.CreateDbContextAsync(CancellationToken.None))
        {
            CopyPrepQueueItem before = await beforeContext.CopyPrepQueueItems
                .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

            var sut = new CancelCopyPrepQueueItemHandler(factory);
            bool result = await sut.Handle(new CancelCopyPrepQueueItem(queueItemId), CancellationToken.None);

            result.ShouldBeTrue();

            await using TvContext afterContext = await factory.CreateDbContextAsync(CancellationToken.None);
            CopyPrepQueueItem after = await afterContext.CopyPrepQueueItems
                .Include(item => item.LogEntries)
                .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

            after.Status.ShouldBe(CopyPrepStatus.Failed);
            after.CanceledAt.ShouldBeNull();
            after.UpdatedAt.ShouldBe(before.UpdatedAt);
            after.LogEntries.ShouldBeEmpty();
        }
    }

    [Test]
    public async Task Try_mark_prepared_should_not_overwrite_canceled_item()
    {
        await using var factory = await TestDbContextFactory.Create();
        int queueItemId = await factory.SeedQueueItem(CopyPrepStatus.Canceled);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

        bool result = await CopyPrepService.TryMarkPrepared(
            dbContext,
            queueItem,
            NullLogger.Instance,
            CancellationToken.None);

        result.ShouldBeFalse();

        await using TvContext verificationContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem persisted = await verificationContext.CopyPrepQueueItems
            .Include(item => item.LogEntries)
            .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

        persisted.Status.ShouldBe(CopyPrepStatus.Canceled);
        persisted.CompletedAt.ShouldBeNull();
        persisted.LogEntries.ShouldContain(logEntry => logEntry.Event == "processing_canceled");
        persisted.LogEntries.ShouldNotContain(logEntry => logEntry.Event == "ffmpeg_completed");
    }

    [Test]
    public async Task Finalize_prepared_target_should_not_overwrite_canceled_item()
    {
        await using var factory = await TestDbContextFactory.Create();
        int queueItemId = await factory.SeedQueueItem(CopyPrepStatus.Canceled);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem queueItem = await dbContext.CopyPrepQueueItems
            .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

        var localStatisticsProvider = new RecordingLocalStatisticsProvider();

        await CopyPrepService.FinalizePreparedTarget(
            dbContext,
            localStatisticsProvider,
            ffmpegPath: "ffmpeg",
            ffprobePath: "ffprobe",
            queueItem,
            eventName: "replacement_completed",
            message: "Prepared media replaced the active library file",
            NullLogger.Instance,
            CancellationToken.None);

        localStatisticsProvider.RefreshStatisticsCallCount.ShouldBe(0);

        await using TvContext verificationContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem persisted = await verificationContext.CopyPrepQueueItems
            .Include(item => item.LogEntries)
            .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

        persisted.Status.ShouldBe(CopyPrepStatus.Canceled);
        persisted.ReplacedAt.ShouldBeNull();
        persisted.LogEntries.ShouldContain(logEntry => logEntry.Event == "processing_canceled");
        persisted.LogEntries.ShouldNotContain(logEntry => logEntry.Event == "replacement_completed");
    }

    [Test]
    public async Task Try_mark_failed_should_not_overwrite_canceled_item()
    {
        await using var factory = await TestDbContextFactory.Create();
        int queueItemId = await factory.SeedQueueItem(CopyPrepStatus.Canceled);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);

        bool result = await CopyPrepService.TryMarkFailed(
            dbContext,
            queueItemId,
            new InvalidOperationException("boom"),
            NullLogger.Instance,
            CancellationToken.None);

        result.ShouldBeFalse();

        await using TvContext verificationContext = await factory.CreateDbContextAsync(CancellationToken.None);
        CopyPrepQueueItem persisted = await verificationContext.CopyPrepQueueItems
            .Include(item => item.LogEntries)
            .SingleAsync(item => item.Id == queueItemId, CancellationToken.None);

        persisted.Status.ShouldBe(CopyPrepStatus.Canceled);
        persisted.FailedAt.ShouldBeNull();
        persisted.LastError.ShouldBeNull();
        persisted.LogEntries.ShouldContain(logEntry => logEntry.Event == "processing_canceled");
        persisted.LogEntries.ShouldNotContain(logEntry => logEntry.Event == "processing_failed");
    }

    private sealed class TestDbContextFactory : IDbContextFactory<TvContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<TvContext> _options;
        private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private readonly SlowQueryInterceptor _slowQueryInterceptor = new(NullLogger<SlowQueryInterceptor>.Instance);

        private TestDbContextFactory(SqliteConnection connection)
        {
            _connection = connection;
            _options = new DbContextOptionsBuilder<TvContext>()
                .UseSqlite(_connection)
                .Options;
        }

        public static async Task<TestDbContextFactory> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var factory = new TestDbContextFactory(connection);
            await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
            await dbContext.Database.EnsureCreatedAsync(CancellationToken.None);
            return factory;
        }

        public async Task<int> SeedQueueItem(CopyPrepStatus status)
        {
            DateTime now = DateTime.UtcNow;

            await using TvContext dbContext = await CreateDbContextAsync(CancellationToken.None);

            var mediaSource = new LocalMediaSource();
            var library = new LocalLibrary
            {
                Name = "Library",
                MediaSource = mediaSource
            };
            var libraryPath = new LibraryPath
            {
                Path = @"D:\media",
                Library = library
            };
            var mediaFile = new MediaFile
            {
                Path = @"D:\media\movie.mkv",
                PathHash = "hash"
            };
            var mediaVersion = new MediaVersion
            {
                Name = "version",
                MediaFiles = [mediaFile],
                Streams = [],
                Chapters = [],
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

            var queueItem = new CopyPrepQueueItem
            {
                MediaItemId = movie.Id,
                MediaVersionId = mediaVersion.Id,
                MediaFileId = mediaFile.Id,
                Status = status,
                Reason = "Needs prep",
                SourcePath = mediaFile.Path,
                TargetPath = @"D:\media\movie.mp4",
                ArchivePath = @"D:\archive\movie.mkv",
                LastError = null,
                AttemptCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
                QueuedAt = now,
                StartedAt = status == CopyPrepStatus.Processing ? now : null,
                FailedAt = status == CopyPrepStatus.Failed ? now : null,
                CanceledAt = status == CopyPrepStatus.Canceled ? now : null,
                CompletedAt = null,
                ReplacedAt = null,
                LogEntries = []
            };

            dbContext.CopyPrepQueueItems.Add(queueItem);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            return queueItem.Id;
        }

        public TvContext CreateDbContext() => new(_options, _loggerFactory, _slowQueryInterceptor);

        public Task<TvContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    private sealed class RecordingLocalStatisticsProvider : ILocalStatisticsProvider
    {
        public int RefreshStatisticsCallCount { get; private set; }

        public Task<Either<BaseError, MediaVersion>> GetStatistics(string ffprobePath, string path) =>
            throw new NotSupportedException();

        public Task<Either<BaseError, bool>> RefreshStatistics(string ffmpegPath, string ffprobePath, MediaItem mediaItem)
        {
            RefreshStatisticsCallCount += 1;
            return Task.FromResult<Either<BaseError, bool>>(true);
        }

        public Either<BaseError, List<SongTag>> GetSongTags(MediaItem mediaItem) =>
            throw new NotSupportedException();

        public Task<Option<double>> GetInterlacedRatio(
            string ffmpegPath,
            MediaItem mediaItem,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Option<int>> GetProfileCount(
            string ffmpegPath,
            MediaItem mediaItem,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
