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
    public void ParseLines_Should_UseLatestCompletedProgressBlock_EvenWhenDurationKeysAreMissing()
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
            "speed=1.40x",
            "progress=continue",
            "frame=300",
            "fps=30.0"
        ];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, DateTime.UtcNow);

        result.ProcessedFrames.ShouldBe(220);
        result.FramesPerSecond.ShouldNotBeNull();
        result.FramesPerSecond.Value.ShouldBe(27.3d, 0.001d);
        result.OutputBytes.ShouldBe(20_971_520L);
        result.ProcessedDuration.ShouldBeNull();
        result.CurrentSpeedMultiplier.ShouldNotBeNull();
        result.CurrentSpeedMultiplier.Value.ShouldBe(1.40d, 0.001d);
    }

    [Test]
    public void ParseLines_Should_AverageOnlyParseableSpeedsWithinLastSixCompletedBlocks()
    {
        string[] lines =
        [
            "frame=10",
            "out_time_us=1000000",
            "speed=10.0x",
            "progress=continue",
            "frame=20",
            "out_time_us=2000000",
            "speed=2.0x",
            "progress=continue",
            "frame=30",
            "out_time_us=3000000",
            "speed=not-a-number",
            "progress=continue",
            "frame=40",
            "out_time_us=4000000",
            "speed=4.0x",
            "progress=continue",
            "frame=50",
            "out_time_us=5000000",
            "speed=N/A",
            "progress=continue",
            "frame=60",
            "out_time_us=6000000",
            "speed=6.0x",
            "progress=continue",
            "frame=70",
            "out_time_us=7000000",
            "speed=8.0x",
            "progress=continue"
        ];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, DateTime.UtcNow);

        result.ProcessedFrames.ShouldBe(70);
        result.CurrentSpeedMultiplier.ShouldNotBeNull();
        result.CurrentSpeedMultiplier.Value.ShouldBe(8.0d, 0.001d);
        result.AverageSpeedMultiplier.ShouldNotBeNull();
        result.AverageSpeedMultiplier.Value.ShouldBe(5.0d, 0.001d);
    }

    [Test]
    public void ParseLines_Should_Treat_OutTimeMs_AsMicroseconds()
    {
        string[] lines =
        [
            "frame=220",
            "fps=27.3",
            "total_size=20971520",
            "out_time_ms=9000000",
            "speed=1.40x",
            "progress=continue"
        ];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, DateTime.UtcNow);

        result.ProcessedDuration.ShouldBe(TimeSpan.FromSeconds(9));
    }

    [Test]
    public void ParseLines_Should_FallBack_To_OutTime_WhenNumericDurationKeysAreMissing()
    {
        string[] lines =
        [
            "frame=220",
            "fps=27.3",
            "total_size=20971520",
            "out_time=00:00:09.000000",
            "speed=1.40x",
            "progress=continue"
        ];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, DateTime.UtcNow);

        result.ProcessedDuration.ShouldBe(TimeSpan.FromSeconds(9));
    }

    [Test]
    public void ParseLines_Should_ReturnEmptySnapshot_WhenNoProgressBlockExists()
    {
        string[] lines = ["ffmpeg version 7.1", "Input #0, matroska,webm, from 'episode.mkv':"];

        CopyPrepProgressSnapshot result = CopyPrepProgressParser.ParseLines(lines, null);

        result.ShouldBe(CopyPrepProgressSnapshot.Empty);
    }
}
