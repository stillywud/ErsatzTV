using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Core.Domain.CopyPrep;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Pages;

[TestFixture]
public class CopyPrepPageStateTests
{
    [Test]
    public void Should_use_file_name_without_extension_for_display_name()
    {
        var model = new CopyPrepQueueItemViewModel(
            1,
            10,
            CopyPrepStatus.Queued,
            "Needs prep",
            "episode-01",
            "My Library",
            @"D:\media\show\episode-01.mkv",
            @"D:\copy-prep\episode-01.mp4",
            @"D:\copy-prep\archive\episode-01.mkv",
            @"D:\copy-prep\logs\episode-01.log",
            "ffmpeg -i input",
            string.Empty,
            null,
            0,
            new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 28, 12, 1, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 28, 12, 0, 0, DateTimeKind.Utc),
            null,
            null,
            null,
            null,
            null,
            []);

        model.DisplayName.ShouldBe("episode-01");
        model.LibraryName.ShouldBe("My Library");
    }
}
