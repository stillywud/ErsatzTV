using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.CopyPrep.Queries;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;
using CopyPrepPageState = ErsatzTV.Pages.CopyPrepPageState;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Pages;

[TestFixture]
public class CopyPrepPageStateTests
{
    [Test]
    public void Should_project_display_name_from_source_path_without_extension()
    {
        var result = GetCopyPrepQueueItemsHandler.Project(BuildQueueItem(
            sourcePath: @"D:\media\show\episode-01.mkv",
            libraryName: "My Library"));

        result.DisplayName.ShouldBe("episode-01");
    }

    [Test]
    public void Should_project_library_name_from_media_item_library()
    {
        var result = GetCopyPrepQueueItemsHandler.Project(BuildQueueItem(
            sourcePath: @"D:\media\show\episode-01.mkv",
            libraryName: "My Library"));

        result.LibraryName.ShouldBe("My Library");
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public void Should_project_unknown_library_name_when_navigation_path_is_missing(
        bool missingMediaItem,
        bool missingLibraryPath,
        bool missingLibrary)
    {
        var result = GetCopyPrepQueueItemsHandler.Project(BuildQueueItem(
            sourcePath: @"D:\media\show\episode-01.mkv",
            libraryName: "My Library",
            includeMediaItem: !missingMediaItem,
            includeLibraryPath: !missingLibraryPath,
            includeLibrary: !missingLibrary));

        result.LibraryName.ShouldBe("Unknown");
    }

    [Test]
    public void Should_filter_by_search_status_and_library()
    {
        List<CopyPrepQueueItemViewModel> items =
        [
            MakeItem(
                id: 1,
                displayName: "Episode 1",
                sourcePath: @"D:\media\shows\episode-1.mkv",
                libraryName: "TV",
                status: CopyPrepStatus.Queued,
                updatedAt: new DateTime(2026, 3, 28, 8, 0, 0, DateTimeKind.Utc)),
            MakeItem(
                id: 2,
                displayName: "Episode 2",
                sourcePath: @"D:\media\shows\episode-2.mkv",
                libraryName: "TV",
                status: CopyPrepStatus.Processing,
                updatedAt: new DateTime(2026, 3, 28, 9, 0, 0, DateTimeKind.Utc)),
            MakeItem(
                id: 3,
                displayName: "Movie Night",
                sourcePath: @"D:\media\movies\movie-night.mkv",
                libraryName: "Movies",
                status: CopyPrepStatus.Processing,
                updatedAt: new DateTime(2026, 3, 28, 10, 0, 0, DateTimeKind.Utc)),
            MakeItem(
                id: 4,
                displayName: "Bonus Clip",
                sourcePath: @"D:\media\tv\special-feature.mp4",
                libraryName: "tv",
                status: CopyPrepStatus.Processing,
                updatedAt: new DateTime(2026, 3, 28, 11, 0, 0, DateTimeKind.Utc))
        ];

        List<CopyPrepQueueItemViewModel> result = CopyPrepPageState
            .ApplyFilters(items, "  special  ", CopyPrepStatus.Processing, "TV")
            .ToList();

        result.Select(item => item.Id).ShouldBe([4]);
    }

    [Test]
    public void Should_build_summary_counts_and_average_duration()
    {
        DateTime now = new(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc);

        List<CopyPrepQueueItemViewModel> items =
        [
            MakeItem(
                id: 1,
                status: CopyPrepStatus.Queued,
                updatedAt: now.AddMinutes(-1)),
            MakeItem(
                id: 2,
                status: CopyPrepStatus.Processing,
                updatedAt: now.AddMinutes(-2),
                startedAt: now.AddMinutes(-15)),
            MakeItem(
                id: 3,
                status: CopyPrepStatus.Failed,
                updatedAt: now.AddMinutes(-3),
                startedAt: now.AddMinutes(-30)),
            MakeItem(
                id: 4,
                status: CopyPrepStatus.Replaced,
                updatedAt: now.AddMinutes(-4),
                startedAt: now.AddMinutes(-40),
                completedAt: now.AddMinutes(-20)),
            MakeItem(
                id: 5,
                status: CopyPrepStatus.Replaced,
                updatedAt: now.AddMinutes(-5),
                startedAt: now.AddMinutes(-70),
                replacedAt: now.AddMinutes(-40)),
            MakeItem(
                id: 6,
                status: CopyPrepStatus.Replaced,
                updatedAt: now.AddDays(-1),
                startedAt: now.AddDays(-1).AddMinutes(-50),
                completedAt: now.AddDays(-1).AddMinutes(-20))
        ];

        var result = CopyPrepPageState.BuildSummary(items, now);

        result.ShouldBe(new ErsatzTV.Pages.CopyPrepSummaryViewModel(
            Queued: 1,
            Running: 1,
            Failed: 1,
            CompletedToday: 2,
            AverageDuration: TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void Should_use_replaced_at_for_replaced_item_summary_when_both_terminal_timestamps_exist()
    {
        DateTime now = new(2026, 3, 29, 12, 0, 0, DateTimeKind.Utc);

        List<CopyPrepQueueItemViewModel> items =
        [
            MakeItem(
                id: 1,
                status: CopyPrepStatus.Replaced,
                updatedAt: now,
                startedAt: new DateTime(2026, 3, 28, 23, 30, 0, DateTimeKind.Utc),
                completedAt: new DateTime(2026, 3, 28, 23, 50, 0, DateTimeKind.Utc),
                replacedAt: new DateTime(2026, 3, 29, 0, 10, 0, DateTimeKind.Utc))
        ];

        var result = CopyPrepPageState.BuildSummary(items, now);

        result.ShouldBe(new ErsatzTV.Pages.CopyPrepSummaryViewModel(
            Queued: 0,
            Running: 0,
            Failed: 0,
            CompletedToday: 1,
            AverageDuration: TimeSpan.FromMinutes(40)));
    }

    [TestCase(CopyPrepStatus.Queued, true)]
    [TestCase(CopyPrepStatus.Processing, true)]
    [TestCase(CopyPrepStatus.Failed, false)]
    [TestCase(CopyPrepStatus.Replaced, false)]
    public void Should_indicate_when_queue_item_can_be_canceled(CopyPrepStatus status, bool expected) =>
        CopyPrepPageState.CanCancel(status).ShouldBe(expected);

    [TestCase(CopyPrepStatus.Failed, true)]
    [TestCase(CopyPrepStatus.Canceled, true)]
    [TestCase(CopyPrepStatus.Skipped, true)]
    [TestCase(CopyPrepStatus.Queued, false)]
    [TestCase(CopyPrepStatus.Processing, false)]
    public void Should_indicate_when_queue_item_can_be_retried(CopyPrepStatus status, bool expected) =>
        CopyPrepPageState.CanRetry(status).ShouldBe(expected);

    [TestCase(CopyPrepStatus.Queued, 5)]
    [TestCase(CopyPrepStatus.Processing, 50)]
    [TestCase(CopyPrepStatus.Prepared, 80)]
    [TestCase(CopyPrepStatus.Replaced, 100)]
    [TestCase(CopyPrepStatus.Failed, 100)]
    [TestCase(CopyPrepStatus.Canceled, 100)]
    [TestCase(CopyPrepStatus.Skipped, 100)]
    public void Should_map_status_to_pseudo_progress_percent(CopyPrepStatus status, int expected) =>
        CopyPrepPageState.GetPseudoProgressPercent(status).ShouldBe(expected);

    [Test]
    public void Should_format_row_duration_using_terminal_timestamp_priority()
    {
        var item = MakeItem(
            id: 1,
            status: CopyPrepStatus.Replaced,
            startedAt: new DateTime(2026, 3, 28, 10, 0, 0, DateTimeKind.Utc),
            completedAt: new DateTime(2026, 3, 28, 10, 20, 0, DateTimeKind.Utc),
            replacedAt: new DateTime(2026, 3, 28, 10, 30, 0, DateTimeKind.Utc));

        CopyPrepPageState.FormatDuration(item).ShouldBe("30m");
    }

    [Test]
    public void Should_format_missing_average_duration_as_dash() =>
        CopyPrepPageState.FormatDuration((TimeSpan?)null).ShouldBe("—");

    private static CopyPrepQueueItemViewModel MakeItem(
        int id,
        string displayName = "Episode",
        string sourcePath = @"D:\media\shows\episode.mkv",
        string libraryName = "Library",
        CopyPrepStatus status = CopyPrepStatus.Queued,
        DateTime? updatedAt = null,
        DateTime? startedAt = null,
        DateTime? completedAt = null,
        DateTime? replacedAt = null) =>
        new(
            Id: id,
            MediaItemId: id,
            Status: status,
            Reason: "Needs prep",
            DisplayName: displayName,
            LibraryName: libraryName,
            SourcePath: sourcePath,
            TargetPath: @"D:\copy-prep\target.mp4",
            ArchivePath: @"D:\copy-prep\archive\target.mkv",
            LastLogPath: @"D:\copy-prep\logs\target.log",
            LastCommand: "ffmpeg -i input",
            LastError: string.Empty,
            LastExitCode: null,
            AttemptCount: 0,
            CreatedAt: new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt: updatedAt ?? new DateTime(2026, 3, 28, 12, 1, 0, DateTimeKind.Utc),
            QueuedAt: new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc),
            StartedAt: startedAt,
            CompletedAt: completedAt,
            FailedAt: null,
            CanceledAt: null,
            ReplacedAt: replacedAt,
            LogEntries: []);

    private static CopyPrepQueueItem BuildQueueItem(
        string sourcePath,
        string libraryName,
        bool includeMediaItem = true,
        bool includeLibraryPath = true,
        bool includeLibrary = true)
    {
        var queueItem = new CopyPrepQueueItem
        {
            Id = 1,
            MediaItemId = 10,
            Status = CopyPrepStatus.Queued,
            Reason = "Needs prep",
            SourcePath = sourcePath,
            TargetPath = @"D:\copy-prep\episode-01.mp4",
            ArchivePath = @"D:\copy-prep\archive\episode-01.mkv",
            LastLogPath = @"D:\copy-prep\logs\episode-01.log",
            LastCommand = "ffmpeg -i input",
            LastError = string.Empty,
            AttemptCount = 0,
            CreatedAt = new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 3, 28, 12, 1, 0, DateTimeKind.Utc),
            QueuedAt = new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc),
            LogEntries = []
        };

        if (!includeMediaItem)
        {
            return queueItem;
        }

        queueItem.MediaItem = new Movie();

        if (!includeLibraryPath)
        {
            return queueItem;
        }

        queueItem.MediaItem.LibraryPath = new LibraryPath();

        if (!includeLibrary)
        {
            return queueItem;
        }

        queueItem.MediaItem.LibraryPath.Library = new LocalLibrary
        {
            Name = libraryName
        };

        return queueItem;
    }
}
