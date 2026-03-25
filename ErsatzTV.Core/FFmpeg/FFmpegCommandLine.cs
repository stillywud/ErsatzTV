namespace ErsatzTV.Core.FFmpeg;

public static class FFmpegCommandLine
{
    public static string Format(string executable, IEnumerable<string> arguments)
    {
        var allParts = new List<string> { Quote(executable) };
        allParts.AddRange(arguments.Select(Quote));
        return string.Join(" ", allParts);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.IndexOfAny([' ', '\t', '\n', '\r', '"']) >= 0
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
