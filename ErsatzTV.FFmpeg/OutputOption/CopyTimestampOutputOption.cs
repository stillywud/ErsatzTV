namespace ErsatzTV.FFmpeg.OutputOption;

public class CopyTimestampOutputOption : OutputOption
{
    public override string[] OutputOptions => new[] { "-copyts" };
}
