using ErsatzTV.Core.CopyPrep;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.CopyPrep;

[TestFixture]
public class CopyPrepProgressParserTests
{
    [Test]
    public void ParseLines_Should_ReturnLatestProgressBlock_AndAverageRecentSpeed()
    {
        string[] lines =
        [
            "frame=100",
            "fps=24.5",
            "total_size=10485760",
            "out_time_us=4000000",
            "speed=1.10x",
            "progress=continue",
            "frame=220",
            "fps=27.3",
            "total_size=20971520",
            "out_time_us=9000000",
            "speed=1.40x",
            "progress=continue"
        ];

        var lastProgressAt = new DateTime(2026, 3, 31, 6, 0, 0, DateTimeKind.Utc);
        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, lastProgressAt);

        result.ProcessedFrames.ShouldBe(220);
        result.FramesPerSecond.ShouldNotBeNull();
        result.FramesPerSecond.Value.ShouldBe(27.3d, 0.001d);
        result.OutputBytes.ShouldBe(20_971_520L);
        result.ProcessedDuration.ShouldBe(TimeSpan.FromSeconds(9));
        result.CurrentSpeedMultiplier.ShouldNotBeNull();
        result.CurrentSpeedMultiplier.Value.ShouldBe(1.40d, 0.001d);
        result.AverageSpeedMultiplier.ShouldNotBeNull();
        result.AverageSpeedMultiplier.Value.ShouldBe(1.25d, 0.001d);
        result.LastProgressAt.ShouldBe(lastProgressAt);
        result.HasLiveProgress.ShouldBeTrue();
    }

    [Test]
    public void ParseLines_Should_IgnoreIncompleteTrailingBlock()
    {
        string[] lines =
        [
            "frame=100",
            "fps=24.5",
            "out_time_us=4000000",
            "speed=1.10x",
            "progress=continue",
            "frame=150",
            "fps=26.0"
        ];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, DateTime.UtcNow);

        result.ProcessedFrames.ShouldBe(100);
        result.ProcessedDuration.ShouldBe(TimeSpan.FromSeconds(4));
        result.CurrentSpeedMultiplier.ShouldNotBeNull();
        result.CurrentSpeedMultiplier.Value.ShouldBe(1.10d, 0.001d);
    }

    [Test]
    public void ParseLines_Should_ReturnEmptySnapshot_WhenNoProgressBlockExists()
    {
        string[] lines = ["ffmpeg version 7.1", "Input #0, matroska,webm, from 'episode.mkv':"];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, null);

        result.ShouldBe(CopyPrepProgressSnapshot.Empty);
    }
}
