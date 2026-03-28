using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Core.Domain.CopyPrep;

namespace ErsatzTV.Pages;

public record CopyPrepSummaryViewModel(
    int Queued,
    int Running,
    int Failed,
    int CompletedToday,
    TimeSpan? AverageDuration);

public static class CopyPrepPageState
{
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
        CopyPrepStatus.Queued => 5,
        CopyPrepStatus.Processing => 50,
        CopyPrepStatus.Prepared => 80,
        CopyPrepStatus.Replaced => 100,
        CopyPrepStatus.Failed => 100,
        CopyPrepStatus.Canceled => 100,
        CopyPrepStatus.Skipped => 100,
        _ => 0
    };

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
}
