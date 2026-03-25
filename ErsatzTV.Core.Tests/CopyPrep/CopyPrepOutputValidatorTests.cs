using ErsatzTV.Core.CopyPrep;
using ErsatzTV.Core.Domain;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.CopyPrep;

[TestFixture]
public class CopyPrepOutputValidatorTests
{
    [Test]
    public void Validate_Should_Succeed_For_CopyPrepCompatible_Target()
    {
        MediaVersion source = CreateSourceVersion(withAudio: true, durationSeconds: 600);
        MediaVersion target = CreateTargetVersion(videoCodec: "h264", audioCodec: "aac", durationSeconds: 605, audioChannels: 2);

        CopyPrepOutputValidationResult result = CopyPrepOutputValidator.Validate(source, target);

        result.IsValid.ShouldBeTrue();
        result.Summary.ShouldContain("validated successfully");
    }

    [Test]
    public void Validate_Should_Fail_When_Target_Duration_Exceeds_Tolerance()
    {
        MediaVersion source = CreateSourceVersion(withAudio: true, durationSeconds: 600);
        MediaVersion target = CreateTargetVersion(videoCodec: "h264", audioCodec: "aac", durationSeconds: 640, audioChannels: 2);

        CopyPrepOutputValidationResult result = CopyPrepOutputValidator.Validate(source, target);

        result.IsValid.ShouldBeFalse();
        result.Summary.ShouldContain("duration delta");
    }

    [Test]
    public void Validate_Should_Fail_When_Target_Is_Missing_Audio_For_Audio_Source()
    {
        MediaVersion source = CreateSourceVersion(withAudio: true, durationSeconds: 600);
        MediaVersion target = CreateTargetVersion(videoCodec: "h264", audioCodec: null, durationSeconds: 600, audioChannels: 0);

        CopyPrepOutputValidationResult result = CopyPrepOutputValidator.Validate(source, target);

        result.IsValid.ShouldBeFalse();
        result.Summary.ShouldContain("missing an audio stream");
    }

    [Test]
    public void Validate_Should_Fail_When_Target_Contains_Audio_But_Source_Does_Not()
    {
        MediaVersion source = CreateSourceVersion(withAudio: false, durationSeconds: 600);
        MediaVersion target = CreateTargetVersion(videoCodec: "h264", audioCodec: "aac", durationSeconds: 600, audioChannels: 2);

        CopyPrepOutputValidationResult result = CopyPrepOutputValidator.Validate(source, target);

        result.IsValid.ShouldBeFalse();
        result.Summary.ShouldContain("unexpectedly contains audio");
    }

    private static MediaVersion CreateSourceVersion(bool withAudio, int durationSeconds)
    {
        var streams = new List<MediaStream>
        {
            new()
            {
                MediaStreamKind = MediaStreamKind.Video,
                Codec = "mpeg2video",
                PixelFormat = "yuv420p",
                Profile = "main"
            }
        };

        if (withAudio)
        {
            streams.Add(new MediaStream
            {
                MediaStreamKind = MediaStreamKind.Audio,
                Codec = "ac3",
                Channels = 6
            });
        }

        return new MediaVersion
        {
            Streams = streams,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            SampleAspectRatio = "4:3",
            Width = 1920,
            Height = 1080,
            RFrameRate = "30000/1001"
        };
    }

    private static MediaVersion CreateTargetVersion(string videoCodec, string audioCodec, int durationSeconds, int audioChannels)
    {
        var streams = new List<MediaStream>
        {
            new()
            {
                MediaStreamKind = MediaStreamKind.Video,
                Codec = videoCodec,
                PixelFormat = CopyPrepTranscodeProfile.PixelFormat,
                Profile = CopyPrepTranscodeProfile.VideoProfile
            }
        };

        if (!string.IsNullOrWhiteSpace(audioCodec))
        {
            streams.Add(new MediaStream
            {
                MediaStreamKind = MediaStreamKind.Audio,
                Codec = audioCodec,
                Channels = audioChannels
            });
        }

        return new MediaVersion
        {
            Streams = streams,
            Duration = TimeSpan.FromSeconds(durationSeconds),
            SampleAspectRatio = "1:1",
            Width = 1920,
            Height = 1080,
            RFrameRate = "30000/1001",
            VideoScanKind = VideoScanKind.Progressive
        };
    }
}
