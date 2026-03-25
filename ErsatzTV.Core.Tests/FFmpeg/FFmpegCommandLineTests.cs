using ErsatzTV.Core.FFmpeg;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Core.Tests.FFmpeg;

[TestFixture]
public class FFmpegCommandLineTests
{
    [Test]
    public void Format_Should_QuoteExecutableAndArguments_WhenNeeded()
    {
        string commandLine = FFmpegCommandLine.Format(
            @"C:\Program Files\FFmpeg\ffmpeg.exe",
            ["-i", @"D:\Media Files\episode 01.mkv", "-vf", "fps=30000/1001"]);

        commandLine.ShouldBe("\"C:\\Program Files\\FFmpeg\\ffmpeg.exe\" -i \"D:\\Media Files\\episode 01.mkv\" -vf fps=30000/1001");
    }
}
