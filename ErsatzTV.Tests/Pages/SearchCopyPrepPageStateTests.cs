using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.MediaCards;
using ErsatzTV.Core.Domain;
using ErsatzTV.Pages;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Pages;

[TestFixture]
public class SearchCopyPrepPageStateTests
{
    [Test]
    public void Should_detect_library_query_scope()
    {
        SearchCopyPrepPageState.IsLibraryScopedQuery("library_id:15").ShouldBeTrue();
        SearchCopyPrepPageState.IsLibraryScopedQuery(" title:foo ").ShouldBeFalse();
    }

    [Test]
    public void Should_allow_selection_only_for_selectable_needs_copy_prep_items()
    {
        var eligible = new CopyPrepSelectionStateViewModel(
            101,
            "movie",
            CopyPrepSelectionStatus.NeedsCopyPrep,
            true,
            "container/extension .mkv is not MP4/M4V");
        var ready = new CopyPrepSelectionStateViewModel(
            102,
            "movie",
            CopyPrepSelectionStatus.CopyReady,
            false,
            string.Empty);

        SearchCopyPrepPageState.CanSelect(eligible).ShouldBeTrue();
        SearchCopyPrepPageState.CanSelect(ready).ShouldBeFalse();
    }

    [Test]
    public void Should_return_only_selectable_cards_for_select_all_eligible()
    {
        MediaCardViewModel[] cards =
        [
            new(1, "Needs Work", string.Empty, "Needs Work", string.Empty, MediaItemState.Normal, true),
            new(2, "Ready", string.Empty, "Ready", string.Empty, MediaItemState.Normal, true),
            new(3, "Unknown", string.Empty, "Unknown", string.Empty, MediaItemState.Normal, true)
        ];

        IReadOnlyDictionary<int, CopyPrepSelectionStateViewModel> stateByMediaItemId =
            new Dictionary<int, CopyPrepSelectionStateViewModel>
            {
                [1] = new(1, "movie", CopyPrepSelectionStatus.NeedsCopyPrep, true, "needs copy prep"),
                [2] = new(2, "movie", CopyPrepSelectionStatus.CopyReady, false, string.Empty)
            };

        SearchCopyPrepPageState.GetSelectableCards(cards, stateByMediaItemId)
            .Select(card => card.MediaItemId)
            .ShouldBe([1]);
    }
}
