using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.CopyPrep;

namespace ErsatzTV.Core.CopyPrep;

public static class CopyPrepAnalyzer
{
    private static readonly System.Collections.Generic.HashSet<string> CopyFriendlyExtensions =
    [
        ".mp4",
        ".m4v"
    ];

    public static CopyPrepDecision Analyze(MediaVersion version, string path)
    {
        if (version is null)
        {
            return new CopyPrepDecision(false, string.Empty, []);
        }

        MediaStream videoStream = version.Streams?
            .FirstOrDefault(stream => stream.MediaStreamKind is MediaStreamKind.Video && !stream.AttachedPic);

        if (videoStream is null)
        {
            return new CopyPrepDecision(false, string.Empty, []);
        }

        MediaStream audioStream = version.Streams?
            .FirstOrDefault(stream => stream.MediaStreamKind is MediaStreamKind.Audio);

        List<string> reasons = [];

        string extension = Path.GetExtension(path ?? string.Empty);
        if (!CopyFriendlyExtensions.Contains(extension))
        {
            reasons.Add($"container/extension {extension} is not MP4/M4V");
        }

        if (!string.Equals(videoStream.Codec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"video codec {videoStream.Codec ?? "unknown"} is not H.264");
        }

        if (audioStream is not null &&
            !string.Equals(audioStream.Codec, "aac", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"audio codec {audioStream.Codec ?? "unknown"} is not AAC");
        }

        if (!string.IsNullOrWhiteSpace(videoStream.PixelFormat) &&
            !string.Equals(videoStream.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"pixel format {videoStream.PixelFormat} is not yuv420p");
        }

        if (!string.IsNullOrWhiteSpace(version.SampleAspectRatio) &&
            !string.Equals(version.SampleAspectRatio, "1:1", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add($"sample aspect ratio {version.SampleAspectRatio} is not 1:1");
        }

        if (version.VideoScanKind is VideoScanKind.Interlaced)
        {
            reasons.Add("video is interlaced");
        }

        if (videoStream.BitsPerRawSample > 8)
        {
            reasons.Add($"bit depth {videoStream.BitsPerRawSample}-bit is above 8-bit");
        }

        if (string.IsNullOrWhiteSpace(version.RFrameRate) || version.RFrameRate == "0/0")
        {
            reasons.Add("frame-rate metadata is missing or invalid");
        }

        return reasons.Count == 0
            ? new CopyPrepDecision(false, string.Empty, [])
            : new CopyPrepDecision(true, string.Join("; ", reasons), reasons);
    }
}
