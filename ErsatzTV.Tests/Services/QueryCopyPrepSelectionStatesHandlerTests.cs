using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.CopyPrep.Queries;
using ErsatzTV.Core.Domain;
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
public class QueryCopyPrepSelectionStatesHandlerTests
{
    [Test]
    public async Task Should_mark_mkv_movie_as_needs_copy_prep()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            path: @"D:\media\movies\example.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new QueryCopyPrepSelectionStatesHandler(factory);

        List<CopyPrepSelectionStateViewModel> result = await sut.Handle(
            new QueryCopyPrepSelectionStates([movieId], [], [], []),
            CancellationToken.None);

        result.Count.ShouldBe(1);

        CopyPrepSelectionStateViewModel item = result.Single();
        item.MediaItemId.ShouldBe(movieId);
        item.MediaKind.ShouldBe("movie");
        item.Status.ShouldBe(CopyPrepSelectionStatus.NeedsCopyPrep);
        item.IsSelectable.ShouldBeTrue();
    }

    [Test]
    public async Task Should_mark_copy_ready_mp4_as_copy_ready_and_not_selectable()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            path: @"D:\media\movies\example.mp4",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new QueryCopyPrepSelectionStatesHandler(factory);

        List<CopyPrepSelectionStateViewModel> result = await sut.Handle(
            new QueryCopyPrepSelectionStates([movieId], [], [], []),
            CancellationToken.None);

        result.Count.ShouldBe(1);

        CopyPrepSelectionStateViewModel item = result.Single();
        item.MediaItemId.ShouldBe(movieId);
        item.MediaKind.ShouldBe("movie");
        item.Status.ShouldBe(CopyPrepSelectionStatus.CopyReady);
        item.IsSelectable.ShouldBeFalse();
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

        public async Task<int> SeedMovie(
            string path,
            string videoCodec,
            string audioCodec,
            string pixelFormat,
            string sampleAspectRatio,
            string frameRate)
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
                Path = @"D:\media\movies",
                Library = library
            };
            var mediaFile = new MediaFile
            {
                Path = path,
                PathHash = "hash"
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

        public TvContext CreateDbContext() => new(_options, _loggerFactory, _slowQueryInterceptor);

        public Task<TvContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
