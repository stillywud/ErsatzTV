using ErsatzTV.FFmpeg.OutputFormat;

namespace ErsatzTV.FFmpeg;

public record FFmpegState(
    bool SaveReport,
    HardwareAccelerationMode DecoderHardwareAccelerationMode,
    HardwareAccelerationMode EncoderHardwareAccelerationMode,
    Option<string> VaapiDriver,
    Option<string> VaapiDevice,
    Option<TimeSpan> Start,
    Option<TimeSpan> Finish,
    bool DoNotMapMetadata,
    Option<string> MetadataServiceProvider,
    Option<string> MetadataServiceName,
    Option<string> MetadataAudioLanguage,
    Option<string> MetadataSubtitleLanguage,
    Option<string> MetadataSubtitleTitle,
    OutputFormatKind OutputFormat,
    Option<string> HlsPlaylistPath,
    Option<string> HlsSegmentTemplate,
    Option<string> HlsInitTemplate,
    Option<string> HlsSegmentOptions,
    TimeSpan PtsOffset,
    Option<int> ThreadCount,
    Option<int> MaybeQsvExtraHardwareFrames,
    bool IsSongWithProgress,
    bool IsHdrTonemap,
    string TonemapAlgorithm,
    bool IsTroubleshooting)
{
    public int QsvExtraHardwareFrames => MaybeQsvExtraHardwareFrames.IfNone(64);

    public static FFmpegState Concat(bool saveReport, string channelName) =>
        new(
            saveReport,
            HardwareAccelerationMode.None,
            HardwareAccelerationMode.None,
            Option<string>.None,
            Option<string>.None,
            Option<TimeSpan>.None,
            Option<TimeSpan>.None,
            true, // do not map metadata
            "ErsatzTV",
            channelName,
            Option<string>.None,
            Option<string>.None,
            Option<string>.None,
            OutputFormatKind.MpegTs,
            Option<string>.None,
            Option<string>.None,
            Option<string>.None,
            Option<string>.None,
            TimeSpan.Zero,
            Option<int>.None,
            Option<int>.None,
            false,
            false,
            "linear",
            false);

    public static FFmpegState ConcatWithHls(
        bool saveReport,
        string channelName,
        OutputFormatKind outputFormat,
        Option<string> hlsPlaylistPath,
        Option<string> hlsSegmentTemplate,
        Option<string> hlsInitTemplate,
        Option<string> hlsSegmentOptions) =>
        new(
            saveReport,
            HardwareAccelerationMode.None,
            HardwareAccelerationMode.None,
            Option<string>.None,
            Option<string>.None,
            Option<TimeSpan>.None,
            Option<TimeSpan>.None,
            true, // do not map metadata
            "ErsatzTV",
            channelName,
            Option<string>.None,
            Option<string>.None,
            Option<string>.None,
            outputFormat,
            hlsPlaylistPath,
            hlsSegmentTemplate,
            hlsInitTemplate,
            hlsSegmentOptions,
            TimeSpan.Zero,
            Option<int>.None,
            Option<int>.None,
            false,
            false,
            "linear",
            false);
}
