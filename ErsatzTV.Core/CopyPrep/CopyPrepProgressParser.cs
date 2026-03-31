using System.Globalization;

namespace ErsatzTV.Core.CopyPrep;

public static class CopyPrepProgressParser
{
    public static CopyPrepProgressSnapshot ParseFile(string logPath, DateTime? lastProgressAt)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return lastProgressAt.HasValue
                ? CopyPrepProgressSnapshot.Empty with { LastProgressAt = lastProgressAt }
                : CopyPrepProgressSnapshot.Empty;
        }

        try
        {
            return ParseLines(File.ReadLines(logPath), lastProgressAt);
        }
        catch (IOException)
        {
            return lastProgressAt.HasValue
                ? CopyPrepProgressSnapshot.Empty with { LastProgressAt = lastProgressAt }
                : CopyPrepProgressSnapshot.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return lastProgressAt.HasValue
                ? CopyPrepProgressSnapshot.Empty with { LastProgressAt = lastProgressAt }
                : CopyPrepProgressSnapshot.Empty;
        }
    }

    internal static CopyPrepProgressSnapshot ParseLines(IEnumerable<string> lines, DateTime? lastProgressAt)
    {
        List<Dictionary<string, string>> blocks = [];
        Dictionary<string, string> current = new(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine) || !rawLine.Contains('='))
            {
                continue;
            }

            string[] parts = rawLine.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            current[parts[0]] = parts[1];

            if (string.Equals(parts[0], "progress", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        Dictionary<string, string> latest = blocks.LastOrDefault();

        if (latest is null)
        {
            return lastProgressAt.HasValue
                ? CopyPrepProgressSnapshot.Empty with { LastProgressAt = lastProgressAt }
                : CopyPrepProgressSnapshot.Empty;
        }

        double?[] speeds = blocks
            .TakeLast(6)
            .Select(block => ParseSpeed(block.GetValueOrDefault("speed")))
            .Where(value => value.HasValue)
            .Cast<double?>()
            .ToArray();

        double? averageSpeed = speeds.Length == 0
            ? null
            : speeds.Average(value => value!.Value);

        return new CopyPrepProgressSnapshot(
            ProcessedFrames: ParseLong(latest.GetValueOrDefault("frame")),
            FramesPerSecond: ParseDouble(latest.GetValueOrDefault("fps")),
            OutputBytes: ParseLong(latest.GetValueOrDefault("total_size")),
            ProcessedDuration: ParseDuration(latest),
            CurrentSpeedMultiplier: ParseSpeed(latest.GetValueOrDefault("speed")),
            AverageSpeedMultiplier: averageSpeed,
            LastProgressAt: lastProgressAt);
    }

    private static long? ParseLong(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : null;

    private static double? ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;

    private static double? ParseSpeed(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.EndsWith('x') ? value[..^1] : value;
        return ParseDouble(normalized);
    }

    private static TimeSpan? ParseDuration(IReadOnlyDictionary<string, string> block)
    {
        if (ParseLong(block.GetValueOrDefault("out_time_us")) is { } microseconds)
        {
            return TimeSpan.FromMilliseconds(microseconds / 1000d);
        }

        if (ParseLong(block.GetValueOrDefault("out_time_ms")) is { } microsecondsFromMsField)
        {
            return TimeSpan.FromMilliseconds(microsecondsFromMsField / 1000d);
        }

        string outTime = block.GetValueOrDefault("out_time");
        return TimeSpan.TryParse(outTime, CultureInfo.InvariantCulture, out TimeSpan parsed)
            ? parsed
            : null;
    }
}
