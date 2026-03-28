using ErsatzTV.Application.CopyPrep.Queries;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;
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
