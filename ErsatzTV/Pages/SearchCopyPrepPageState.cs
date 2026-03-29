using System.Text.RegularExpressions;
using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.MediaCards;

namespace ErsatzTV.Pages;

public static partial class SearchCopyPrepPageState
{
    [GeneratedRegex(@"^library_id:\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LibraryScopedQueryRegex();

    public static bool IsLibraryScopedQuery(string query) =>
        !string.IsNullOrWhiteSpace(query) && LibraryScopedQueryRegex().IsMatch(query.Trim());

    public static bool CanSelect(CopyPrepSelectionStateViewModel state) =>
        state is not null &&
        state.IsSelectable &&
        state.Status == CopyPrepSelectionStatus.NeedsCopyPrep;

    public static IEnumerable<MediaCardViewModel> GetSelectableCards(
        IEnumerable<MediaCardViewModel> visibleCards,
        IReadOnlyDictionary<int, CopyPrepSelectionStateViewModel> stateByMediaItemId) =>
        visibleCards.Where(card =>
            stateByMediaItemId.TryGetValue(card.MediaItemId, out CopyPrepSelectionStateViewModel state) &&
            CanSelect(state));
}
