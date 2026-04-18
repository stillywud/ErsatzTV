namespace ErsatzTV.FFmpeg.OutputOption;

public class AvoidNegativeTsOutputOption : OutputOption
{
    public override string[] OutputOptions => ["-avoid_negative_ts", "make_zero"];
}
