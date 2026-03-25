using ErsatzTV.Core.CopyPrep;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.CopyPrep;

[TestFixture]
public class CopyPrepTranscodeProfileTests
{
    [Test]
    public void BuildArguments_Should_IncludeExpectedDefaults_WhenAudioExists()
    {
        string[] arguments = CopyPrepTranscodeProfile.BuildArguments(
            @"D:\media\source.mkv",
            @"D:\media\output.mp4",
            "30000/1001",
            true,
            6);

        arguments.ShouldContain("-sn");
        arguments.ShouldContain("-dn");
        arguments.ShouldContain("-loglevel");
        arguments.ShouldContain("info");
        arguments.ShouldContain("-stats_period");
        arguments.ShouldContain("5");
        arguments.ShouldContain("-c:v");
        arguments.ShouldContain(CopyPrepTranscodeProfile.VideoCodec);
        arguments.ShouldContain("-preset");
        arguments.ShouldContain(CopyPrepTranscodeProfile.VideoPreset);
        arguments.ShouldContain("-crf");
        arguments.ShouldContain(CopyPrepTranscodeProfile.VideoCrf);
        arguments.ShouldContain("-pix_fmt");
        arguments.ShouldContain(CopyPrepTranscodeProfile.PixelFormat);
        arguments.ShouldContain("-movflags");
        arguments.ShouldContain(CopyPrepTranscodeProfile.FastStart);
        arguments.ShouldContain("-threads");
        arguments.ShouldContain("6");
        arguments.ShouldNotContain("-an");
        arguments.ShouldContain("-c:a");
        arguments.ShouldContain(CopyPrepTranscodeProfile.AudioCodec);
        arguments[^1].ShouldBe(@"D:\media\output.mp4");
    }

    [Test]
    public void BuildArguments_Should_DisableAudio_WhenNoAudioStreamExists()
    {
        string[] arguments = CopyPrepTranscodeProfile.BuildArguments(
            @"D:\media\source.mkv",
            @"D:\media\output.mp4",
            "25",
            false,
            2);

        arguments.ShouldContain("-an");
        arguments.ShouldNotContain("-c:a");
        arguments.ShouldNotContain(CopyPrepTranscodeProfile.AudioCodec);
    }

    [TestCase(null, 25d)]
    [TestCase("", 25d)]
    [TestCase("0/0", 25d)]
    [TestCase("24000/1001", 23.976023976023978d)]
    [TestCase("29.97", 29.97d)]
    public void NormalizeFrameRate_Should_ReturnExpectedValue(string frameRate, double expected)
    {
        double actual = CopyPrepTranscodeProfile.NormalizeFrameRate(frameRate);

        actual.ShouldBe(expected, 0.000001d);
    }

    [Test]
    public void BuildVideoFilter_Should_FormatFrameRateWithoutTrailingZeros()
    {
        string filter = CopyPrepTranscodeProfile.BuildVideoFilter(25d);

        filter.ShouldBe("scale=trunc(iw/2)*2:trunc(ih/2)*2,setsar=1,fps=25");
    }
}
