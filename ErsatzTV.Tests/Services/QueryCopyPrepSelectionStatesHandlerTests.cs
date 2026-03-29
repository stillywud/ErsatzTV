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
    public void Query_contract_should_only_advertise_movie_ids()
    {
        string[] propertyNames = typeof(QueryCopyPrepSelectionStates)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        propertyNames.ShouldBe(["MovieIds"]);
    }

    [Test]
    public async Task Should_mark_mkv_movie_as_needs_copy_prep_with_reason()
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
            new QueryCopyPrepSelectionStates([movieId]),
            CancellationToken.None);

        result.Count.ShouldBe(1);

        CopyPrepSelectionStateViewModel item = result.Single();
        item.MediaItemId.ShouldBe(movieId);
        item.MediaKind.ShouldBe("movie");
        item.Status.ShouldBe(CopyPrepSelectionStatus.NeedsCopyPrep);
        item.IsSelectable.ShouldBeTrue();
        item.Reason.ShouldContain("container/extension .mkv is not MP4/M4V");
    }

    [Test]
    public async Task Should_mark_copy_ready_mp4_as_copy_ready_and_not_selectable_with_empty_reason()
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
            new QueryCopyPrepSelectionStates([movieId]),
            CancellationToken.None);

        result.Count.ShouldBe(1);

        CopyPrepSelectionStateViewModel item = result.Single();
        item.MediaItemId.ShouldBe(movieId);
        item.MediaKind.ShouldBe("movie");
        item.Status.ShouldBe(CopyPrepSelectionStatus.CopyReady);
        item.IsSelectable.ShouldBeFalse();
        item.Reason.ShouldBeEmpty();
    }

    [Test]
    public async Task Should_use_head_version_and_primary_file_for_selection_state()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
        [
            new TestMediaVersion(
                [@"D:\media\movies\head-primary.mp4", @"D:\media\movies\head-secondary.mkv"],
                "h264",
                "aac",
                "yuv420p",
                "1:1",
                "25/1"),
            new TestMediaVersion(
                [@"D:\media\movies\later-version.mkv"],
                "h264",
                "aac",
                "yuv420p",
                "1:1",
                "25/1")
        ]);

        var sut = new QueryCopyPrepSelectionStatesHandler(factory);

        List<CopyPrepSelectionStateViewModel> result = await sut.Handle(
            new QueryCopyPrepSelectionStates([movieId]),
            CancellationToken.None);

        result.Count.ShouldBe(1);

        CopyPrepSelectionStateViewModel item = result.Single();
        item.MediaItemId.ShouldBe(movieId);
        item.Status.ShouldBe(CopyPrepSelectionStatus.CopyReady);
        item.IsSelectable.ShouldBeFalse();
        item.Reason.ShouldBeEmpty();
    }

    private sealed record TestMediaVersion(
        IReadOnlyList<string> Paths,
        string VideoCodec,
        string AudioCodec,
        string PixelFormat,
        string SampleAspectRatio,
        string FrameRate);

    private sealed class TestDbContextFactory : IDbContextFactory<TvContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<TvContext> _options;
        private readonly ILoggerFactory _loggerFactory = NullLoggerFactory.Instance;
        private readonly SlowQueryInterceptor _slowQueryInterceptor = new(NullLogger<SlowQueryInterceptor>.Instance);
        private int _nextPathHash = 1;

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

        public Task<int> SeedMovie(
            string path,
            string videoCodec,
            string audioCodec,
            string pixelFormat,
            string sampleAspectRatio,
            string frameRate) =>
            SeedMovie(
            [
                new TestMediaVersion(
                    [path],
                    videoCodec,
                    audioCodec,
                    pixelFormat,
                    sampleAspectRatio,
                    frameRate)
            ]);

        public async Task<int> SeedMovie(IReadOnlyList<TestMediaVersion> versions)
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
            var movie = new Movie
            {
                LibraryPath = libraryPath,
                MediaVersions = versions.Map(version => CreateMediaVersion(version, now)).ToList(),
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

        private MediaVersion CreateMediaVersion(TestMediaVersion version, DateTime now) =>
            new()
            {
                Name = "version",
                MediaFiles = version.Paths.Map(path => new MediaFile
                {
                    Path = path,
                    PathHash = $"hash-{_nextPathHash++}"
                }).ToList(),
                Streams =
                [
                    new MediaStream
                    {
                        Index = 0,
                        Codec = version.VideoCodec,
                        MediaStreamKind = MediaStreamKind.Video,
                        PixelFormat = version.PixelFormat,
                        BitsPerRawSample = 8
                    },
                    new MediaStream
                    {
                        Index = 1,
                        Codec = version.AudioCodec,
                        MediaStreamKind = MediaStreamKind.Audio
                    }
                ],
                Chapters = [],
                SampleAspectRatio = version.SampleAspectRatio,
                RFrameRate = version.FrameRate,
                VideoScanKind = VideoScanKind.Progressive,
                DateAdded = now,
                DateUpdated = now
            };
    }
}
