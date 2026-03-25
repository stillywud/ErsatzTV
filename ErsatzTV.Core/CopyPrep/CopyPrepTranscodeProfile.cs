using System.Globalization;

namespace ErsatzTV.Core.CopyPrep;

public static class CopyPrepTranscodeProfile
{
    public const string VideoCodec = "libx264";
    public const string VideoPreset = "medium";
    public const string VideoCrf = "18";
    public const string PixelFormat = "yuv420p";
    public const string VideoProfile = "high";
    public const string VideoLevel = "4.1";
    public const int KeyframeSeconds = 1;
    public const string AudioCodec = "aac";
    public const string AudioBitrate = "192k";
    public const string AudioSampleRate = "48000";
    public const string AudioChannels = "2";
    public const string FastStart = "+faststart";
    public const int DefaultFrameRate = 25;

    public static string[] BuildArguments(
        string sourcePath,
        string destinationPath,
        string sourceFrameRate,
        bool hasAudio,
        int threadsPerJob)
    {
        double frameRate = NormalizeFrameRate(sourceFrameRate);
        string filter = BuildVideoFilter(frameRate);
        int gop = Math.Max(
            1,
            (int)Math.Round(frameRate * KeyframeSeconds, MidpointRounding.AwayFromZero));

        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel", "info",
            "-stats_period", "5",
            "-progress", "pipe:1",
            "-i", sourcePath,
            "-map", "0:v:0",
            "-map", "0:a:0?",
            "-sn",
            "-dn",
            "-vf", filter,
            "-c:v", VideoCodec,
            "-preset", VideoPreset,
            "-crf", VideoCrf,
            "-pix_fmt", PixelFormat,
            "-profile:v", VideoProfile,
            "-level", VideoLevel,
            "-g", gop.ToString(CultureInfo.InvariantCulture),
            "-keyint_min", gop.ToString(CultureInfo.InvariantCulture),
            "-sc_threshold", "0",
            "-bf", "0",
            "-force_key_frames", $"expr:gte(t,n_forced*{KeyframeSeconds})"
        };

        if (hasAudio)
        {
            arguments.AddRange(
            [
                "-c:a", AudioCodec,
                "-b:a", AudioBitrate,
                "-ar", AudioSampleRate,
                "-ac", AudioChannels
            ]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(
        [
            "-movflags", FastStart,
            "-threads", threadsPerJob.ToString(CultureInfo.InvariantCulture),
            destinationPath
        ]);

        return arguments.ToArray();
    }

    public static string BuildVideoFilter(double frameRate) =>
        $"scale=trunc(iw/2)*2:trunc(ih/2)*2,setsar=1,fps={FormatFrameRate(frameRate)}";

    public static double NormalizeFrameRate(string frameRate)
    {
        if (string.IsNullOrWhiteSpace(frameRate) || frameRate == "0/0")
        {
            return DefaultFrameRate;
        }

        if (frameRate.Contains('/'))
        {
            string[] parts = frameRate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out double numerator) &&
                double.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out double denominator) &&
                denominator > 0)
            {
                return numerator / denominator;
            }
        }

        return double.TryParse(frameRate, NumberStyles.Number, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : DefaultFrameRate;
    }

    public static string FormatFrameRate(double frameRate) =>
        frameRate.ToString("0.######", CultureInfo.InvariantCulture);
}
