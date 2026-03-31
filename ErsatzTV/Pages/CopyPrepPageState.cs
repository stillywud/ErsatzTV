using System.Globalization;
using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Core.Domain.CopyPrep;

namespace ErsatzTV.Pages;

public record CopyPrepSummaryViewModel(
    int Queued,
    int Running,
    int Failed,
    int CompletedToday,
    TimeSpan? AverageDuration);

public enum CopyPrepDisplayStatus
{
    Queued,
    Starting,
    Running,
    Done,
    Failed
}

public static class CopyPrepPageState
{
    private static readonly TimeSpan PossiblyStalledAfter = TimeSpan.FromSeconds(60);

    public static IEnumerable<CopyPrepQueueItemViewModel> ApplyFilters(
        IEnumerable<CopyPrepQueueItemViewModel> items,
        string search,
        CopyPrepStatus? status,
        string library)
    {
        IEnumerable<CopyPrepQueueItemViewModel> result = items;

        string normalizedSearch = search?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            result = result.Where(item =>
                item.DisplayName.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.SourcePath.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        if (status.HasValue)
        {
            result = result.Where(item => item.Status == status.Value);
        }

        string normalizedLibrary = library?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedLibrary))
        {
            result = result.Where(item =>
                string.Equals(item.LibraryName, normalizedLibrary, StringComparison.OrdinalIgnoreCase));
        }

        return result.OrderByDescending(item => item.UpdatedAt);
    }

    public static CopyPrepSummaryViewModel BuildSummary(IEnumerable<CopyPrepQueueItemViewModel> items, DateTime now)
    {
        CopyPrepQueueItemViewModel[] materializedItems = items.ToArray();

        TimeSpan[] durations = materializedItems
            .Select(GetDuration)
            .Where(duration => duration.HasValue)
            .Select(duration => duration!.Value)
            .ToArray();

        TimeSpan? averageDuration = durations.Length == 0
            ? null
            : TimeSpan.FromTicks(durations.Sum(duration => duration.Ticks) / durations.Length);

        return new CopyPrepSummaryViewModel(
            Queued: materializedItems.Count(item => item.Status == CopyPrepStatus.Queued),
            Running: materializedItems.Count(item => item.Status == CopyPrepStatus.Processing),
            Failed: materializedItems.Count(item => item.Status == CopyPrepStatus.Failed),
            CompletedToday: materializedItems.Count(item => GetTerminalTimestamp(item)?.Date == now.Date),
            AverageDuration: averageDuration);
    }

    public static CopyPrepQueueItemViewModel ResolveSelectedItem(
        IEnumerable<CopyPrepQueueItemViewModel> filteredItems,
        int? selectedId)
    {
        CopyPrepQueueItemViewModel[] materializedItems = filteredItems.ToArray();
        return selectedId.HasValue
            ? materializedItems.FirstOrDefault(item => item.Id == selectedId.Value) ?? materializedItems.FirstOrDefault()
            : materializedItems.FirstOrDefault();
    }

    public static bool CanRetry(CopyPrepStatus status) =>
        status is CopyPrepStatus.Failed or CopyPrepStatus.Canceled or CopyPrepStatus.Skipped;

    public static bool CanCancel(CopyPrepStatus status) =>
        status is CopyPrepStatus.Queued or CopyPrepStatus.Processing;

    public static int GetPseudoProgressPercent(CopyPrepStatus status) => status switch
    {
        CopyPrepStatus.Queued => 0,
        CopyPrepStatus.Processing => 0,
        CopyPrepStatus.Prepared => 100,
        CopyPrepStatus.Replaced => 100,
        CopyPrepStatus.Failed => 100,
        CopyPrepStatus.Canceled => 100,
        CopyPrepStatus.Skipped => 100,
        _ => 0
    };

    public static CopyPrepDisplayStatus GetDisplayStatus(CopyPrepQueueItemViewModel item, DateTime nowUtc) => item.Status switch
    {
        CopyPrepStatus.Queued => CopyPrepDisplayStatus.Queued,
        CopyPrepStatus.Processing when item.Progress.LastProgressAt is null => CopyPrepDisplayStatus.Starting,
        CopyPrepStatus.Processing => CopyPrepDisplayStatus.Running,
        CopyPrepStatus.Prepared or CopyPrepStatus.Replaced => CopyPrepDisplayStatus.Done,
        _ => CopyPrepDisplayStatus.Failed
    };

    public static bool IsPossiblyStalled(CopyPrepQueueItemViewModel item, DateTime nowUtc) =>
        item.Status == CopyPrepStatus.Processing &&
        item.Progress.LastProgressAt is { } lastProgressAt &&
        nowUtc - lastProgressAt > PossiblyStalledAfter;

    public static double GetProgressBarValue(CopyPrepQueueItemViewModel item, DateTime nowUtc) =>
        GetDisplayStatus(item, nowUtc) switch
        {
            CopyPrepDisplayStatus.Queued => 0d,
            CopyPrepDisplayStatus.Starting => 0d,
            CopyPrepDisplayStatus.Done when item.Progress.Percent is null => 100d,
            _ => item.Progress.Percent ?? 0d
        };

    public static string FormatProgressPercent(CopyPrepQueueItemViewModel item, DateTime nowUtc) =>
        $"{GetProgressBarValue(item, nowUtc):0.00}%";

    public static string FormatProcessedDuration(CopyPrepQueueItemViewModel item)
    {
        string processed = item.Progress.ProcessedDuration.HasValue
            ? item.Progress.ProcessedDuration.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : "00:00:00";

        return item.Progress.TotalDuration.HasValue
            ? $"{processed} / {item.Progress.TotalDuration.Value:hh\\:mm\\:ss}"
            : processed;
    }

    public static string FormatEta(CopyPrepQueueItemViewModel item) =>
        item.Progress.EstimatedRemaining.HasValue
            ? item.Progress.EstimatedRemaining.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : "Calculating";

    public static string FormatSpeed(CopyPrepQueueItemViewModel item)
    {
        string speed = item.Progress.AverageSpeedMultiplier.HasValue
            ? $"{item.Progress.AverageSpeedMultiplier.Value:0.00}x"
            : "Calculating";
        string fps = item.Progress.FramesPerSecond.HasValue
            ? $" / {item.Progress.FramesPerSecond.Value:0.0} fps"
            : string.Empty;
        return speed + fps;
    }

    public static string FormatFrames(CopyPrepQueueItemViewModel item) =>
        item.Progress.ProcessedFrames.HasValue && item.Progress.EstimatedTotalFrames.HasValue
            ? $"{item.Progress.ProcessedFrames.Value:N0} / {item.Progress.EstimatedTotalFrames.Value:N0}"
            : item.Progress.ProcessedFrames.HasValue
                ? $"{item.Progress.ProcessedFrames.Value:N0}"
                : "—";

    public static string FormatOutputSize(CopyPrepQueueItemViewModel item) =>
        item.Progress.OutputBytes.HasValue ? FormatBytes(item.Progress.OutputBytes.Value) : "—";

    public static string FormatLastProgressAge(CopyPrepQueueItemViewModel item, DateTime nowUtc)
    {
        if (!item.Progress.LastProgressAt.HasValue)
        {
            return "No updates yet";
        }

        TimeSpan age = nowUtc - item.Progress.LastProgressAt.Value;
        return age.TotalMinutes >= 1
            ? $"{(int)age.TotalMinutes}m {age.Seconds}s ago"
            : $"{age.Seconds}s ago";
    }

    public static TimeSpan? GetDuration(CopyPrepQueueItemViewModel item)
    {
        if (!item.StartedAt.HasValue)
        {
            return null;
        }

        DateTime? endedAt = GetTerminalTimestamp(item);
        return endedAt.HasValue ? endedAt.Value - item.StartedAt.Value : null;
    }

    public static string FormatDuration(CopyPrepQueueItemViewModel item) => FormatDuration(GetDuration(item));

    public static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "—";
        }

        TimeSpan value = duration.Value;
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}h {value.Minutes}m";
        }

        if (value.TotalMinutes >= 1)
        {
            return $"{Math.Max(1, (int)value.TotalMinutes)}m";
        }

        return $"{Math.Max(0, (int)value.TotalSeconds)}s";
    }

    public static DateTime? GetTerminalTimestamp(CopyPrepQueueItemViewModel item) => item.Status switch
    {
        CopyPrepStatus.Replaced => item.ReplacedAt ?? item.CompletedAt,
        _ => item.CompletedAt ?? item.ReplacedAt
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex += 1;
        }

        return $"{value:0.0} {units[unitIndex]}";
    }
}
