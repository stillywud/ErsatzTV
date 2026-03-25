using ErsatzTV.Core.Domain;

namespace ErsatzTV.Core.CopyPrep;

public static class CopyPrepOutputValidator
{
    public static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(15);
    public const double DurationToleranceRatio = 0.03;

    public static CopyPrepOutputValidationResult Validate(MediaVersion sourceVersion, MediaVersion targetVersion)
    {
        if (targetVersion is null)
        {
            return CopyPrepOutputValidationResult.Failure("prepared output could not be probed");
        }

        var issues = new List<string>();

        MediaStream sourceVideo = GetPrimaryVideo(sourceVersion);
        MediaStream targetVideo = GetPrimaryVideo(targetVersion);

        if (targetVideo is null)
        {
            issues.Add("prepared output has no video stream");
        }
        else
        {
            if (!string.Equals(targetVideo.Codec, "h264", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"prepared video codec {targetVideo.Codec ?? "unknown"} is not h264");
            }

            if (!string.Equals(targetVideo.PixelFormat, CopyPrepTranscodeProfile.PixelFormat, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"prepared pixel format {targetVideo.PixelFormat ?? "unknown"} is not {CopyPrepTranscodeProfile.PixelFormat}");
            }

            if (!string.IsNullOrWhiteSpace(targetVideo.Profile) &&
                !string.Equals(targetVideo.Profile, CopyPrepTranscodeProfile.VideoProfile, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"prepared video profile {targetVideo.Profile} is not {CopyPrepTranscodeProfile.VideoProfile}");
            }
        }

        if (!string.Equals(targetVersion.SampleAspectRatio, "1:1", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"prepared sample aspect ratio {targetVersion.SampleAspectRatio ?? "unknown"} is not 1:1");
        }

        if (targetVersion.Width <= 0 || targetVersion.Height <= 0)
        {
            issues.Add("prepared output is missing display dimensions");
        }

        if (targetVersion.Duration <= TimeSpan.Zero)
        {
            issues.Add("prepared output has missing or invalid duration");
        }

        if (sourceVersion is not null && sourceVersion.Duration > TimeSpan.Zero && targetVersion.Duration > TimeSpan.Zero)
        {
            TimeSpan maxAllowedDelta = TimeSpan.FromTicks(
                Math.Max(
                    DurationTolerance.Ticks,
                    (long)(sourceVersion.Duration.Ticks * DurationToleranceRatio)));

            TimeSpan actualDelta = sourceVersion.Duration.Subtract(targetVersion.Duration).Duration();
            if (actualDelta > maxAllowedDelta)
            {
                issues.Add($"prepared duration delta {actualDelta} exceeds tolerance {maxAllowedDelta}");
            }
        }

        bool sourceHasAudio = HasAudio(sourceVersion);
        MediaStream targetAudio = GetPrimaryAudio(targetVersion);
        if (sourceHasAudio)
        {
            if (targetAudio is null)
            {
                issues.Add("prepared output is missing an audio stream");
            }
            else
            {
                if (!string.Equals(targetAudio.Codec, CopyPrepTranscodeProfile.AudioCodec, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"prepared audio codec {targetAudio.Codec ?? "unknown"} is not {CopyPrepTranscodeProfile.AudioCodec}");
                }

                if (targetAudio.Channels > 0 && targetAudio.Channels != 2)
                {
                    issues.Add($"prepared audio channels {targetAudio.Channels} do not match {CopyPrepTranscodeProfile.AudioChannels}");
                }
            }
        }
        else if (targetAudio is not null)
        {
            issues.Add("prepared output unexpectedly contains audio");
        }

        return issues.Count == 0
            ? CopyPrepOutputValidationResult.Success("Prepared output validated successfully")
            : CopyPrepOutputValidationResult.Failure(issues);
    }

    private static bool HasAudio(MediaVersion version) =>
        GetPrimaryAudio(version) is not null;

    private static MediaStream GetPrimaryVideo(MediaVersion version) =>
        version?.Streams?
            .FirstOrDefault(stream => stream.MediaStreamKind == MediaStreamKind.Video && !stream.AttachedPic);

    private static MediaStream GetPrimaryAudio(MediaVersion version) =>
        version?.Streams?
            .FirstOrDefault(stream => stream.MediaStreamKind == MediaStreamKind.Audio);
}
