using ErsatzTV.FFmpeg.Environment;
using ErsatzTV.FFmpeg.InputOption;

namespace ErsatzTV.FFmpeg.Format;

public class ConcatInputFormat : IInputOption
{
    public EnvironmentVariable[] EnvironmentVariables => [];
    public string[] GlobalOptions => [];

    public string[] InputOptions(InputFile inputFile) =>
    [
        "-f", "concat",
        "-safe", "0",
        "-protocol_whitelist", "file,http,tcp,https,tcp,tls",
        "-probesize", "32",
        "-reconnect", "1",
        "-reconnect_on_http_error", "1",
        "-reconnect_on_network_error", "1",
        "-reconnect_delay_max", "5",
        "-rw_timeout", "10000000"  // 10 seconds read timeout in microseconds
    ];

    public string[] FilterOptions => [];
    public string[] OutputOptions => [];
    public FrameState NextState(FrameState currentState) => currentState;
    public bool AppliesTo(AudioInputFile audioInputFile) => false;

    public bool AppliesTo(VideoInputFile videoInputFile) => false;

    public bool AppliesTo(ConcatInputFile concatInputFile) => true;

    public bool AppliesTo(GraphicsEngineInput graphicsEngineInput) => false;
}
