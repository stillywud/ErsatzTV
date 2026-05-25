using System.Globalization;
using System.Text;

namespace ErsatzTV.Core.FFmpeg;

public record ConcatPlaylist(string Scheme, string Host, string ChannelNumber, string Mode)
{
    public override string ToString()
    {
        // Generate many entries so FFmpeg can seamlessly switch between episodes
        // Each entry will play the current playout item via the HTTP endpoint
        var sb = new StringBuilder();
        sb.AppendLine("ffconcat version 1.0");
        // Add many entries to ensure continuous playback across episode transitions
        string url = $"file http://localhost:{Settings.StreamingPort}/ffmpeg/stream/{ChannelNumber}?mode={Mode}";
        for (int i = 0; i < 100; i++)
        {
            sb.AppendLine(url);
        }
        return sb.ToString();
    }
}
