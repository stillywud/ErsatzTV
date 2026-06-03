using System.Globalization;
using System.Text;

namespace ErsatzTV.Core.FFmpeg;

public record ConcatPlaylist(string Scheme, string Host, string ChannelNumber, string Mode)
{
    public override string ToString()
    {
        return $"ffconcat version 1.0\nfile http://localhost:{Settings.StreamingPort}/ffmpeg/stream/{ChannelNumber}?mode={Mode}";
    }
}
