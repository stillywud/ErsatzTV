# Search Copy-Prep Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add library-search-only copy-prep eligibility badges and a selection-based `Add Selected To Copy-Prep` flow for currently visible leaf video results.

**Architecture:** Reuse the existing copy-prep analyzer and queue semantics instead of creating a parallel manual pipeline. Add one read-side application query for visible-card eligibility, one write-side application command for selected-item queueing/requeueing, one small page-state helper for UI rules, and minimal `Search.razor`/`MediaCard.razor` wiring.

**Tech Stack:** ASP.NET Core Blazor, MudBlazor, MediatR, Entity Framework Core, NUnit, Shouldly

---

## File Structure

### Create
- `ErsatzTV.Application/CopyPrep/CopyPrepSelectionStateViewModel.cs` — view model for visible-card eligibility state
- `ErsatzTV.Application/CopyPrep/AddItemsToCopyPrepResult.cs` — result summary for the bulk add command
- `ErsatzTV.Application/CopyPrep/Queries/QueryCopyPrepSelectionStates.cs` — MediatR query contract for visible-card eligibility lookup
- `ErsatzTV.Application/CopyPrep/Queries/QueryCopyPrepSelectionStatesHandler.cs` — handler that loads current versions/files and runs `CopyPrepAnalyzer`
- `ErsatzTV.Application/CopyPrep/Commands/AddItemsToCopyPrep.cs` — MediatR command contract for selected-item bulk queueing
- `ErsatzTV.Application/CopyPrep/Commands/AddItemsToCopyPrepHandler.cs` — handler that re-checks analyzer eligibility and applies dedupe/requeue rules
- `ErsatzTV/Pages/SearchCopyPrepPageState.cs` — page-state helper for library-scope detection, visible-item aggregation, and selection restrictions
- `ErsatzTV.Tests/Pages/SearchCopyPrepPageStateTests.cs` — pure page-state tests
- `ErsatzTV.Tests/Services/QueryCopyPrepSelectionStatesHandlerTests.cs` — application query tests against in-memory sqlite db
- `ErsatzTV.Tests/Services/AddItemsToCopyPrepHandlerTests.cs` — application command tests against in-memory sqlite db

### Modify
- `ErsatzTV/Pages/Search.razor` — load visible copy-prep states, show badges, restrict selection, add new buttons, call new command
- `ErsatzTV/Shared/MediaCard.razor` — render an optional compact status badge below/above the title without changing existing card behavior
- `ErsatzTV.Application/CopyPrep/Commands/RetryCopyPrepQueueItemHandler.cs` — optionally extract shared reset logic if needed during implementation
- `ErsatzTV.Tests/Services/CopyPrepCancellationTests.cs` — optional source of reusable sqlite test factory patterns; do not change unless sharing a test helper is clearly worth it

### Key Boundaries
- `QueryCopyPrepSelectionStatesHandler` is read-only and answers “what should the user see/select?”
- `AddItemsToCopyPrepHandler` is write-only and answers “what should actually be queued/re-queued now?”
- `SearchCopyPrepPageState` owns UI-specific policy, so `Search.razor` does not fill up with conditional logic
- `MediaCard.razor` stays generic by accepting optional badge parameters instead of embedding copy-prep rules

---

### Task 1: Add copy-prep visible-state query contracts and tests

**Files:**
- Create: `ErsatzTV.Application/CopyPrep/CopyPrepSelectionStateViewModel.cs`
- Create: `ErsatzTV.Application/CopyPrep/Queries/QueryCopyPrepSelectionStates.cs`
- Create: `ErsatzTV.Application/CopyPrep/Queries/QueryCopyPrepSelectionStatesHandler.cs`
- Test: `ErsatzTV.Tests/Services/QueryCopyPrepSelectionStatesHandlerTests.cs`

- [ ] **Step 1: Write the failing query tests**

```csharp
using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.CopyPrep.Queries;
using ErsatzTV.Core.Domain;
using ErsatzTV.Core.Domain.MediaCollections;
using ErsatzTV.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Services;

[TestFixture]
public class QueryCopyPrepSelectionStatesHandlerTests
{
    [Test]
    public async Task Should_mark_mkv_movie_as_needs_copy_prep()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            path: @"D:\media\movies\example.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new QueryCopyPrepSelectionStatesHandler(factory);

        List<CopyPrepSelectionStateViewModel> result = await sut.Handle(
            new QueryCopyPrepSelectionStates([movieId], [], [], []),
            CancellationToken.None);

        result.ShouldContain(item =>
            item.MediaItemId == movieId &&
            item.Status == CopyPrepSelectionStatus.NeedsCopyPrep &&
            item.IsSelectable);
    }

    [Test]
    public async Task Should_mark_copy_ready_mp4_as_copy_ready_and_not_selectable()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            path: @"D:\media\movies\example.mp4",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new QueryCopyPrepSelectionStatesHandler(factory);

        List<CopyPrepSelectionStateViewModel> result = await sut.Handle(
            new QueryCopyPrepSelectionStates([movieId], [], [], []),
            CancellationToken.None);

        result.ShouldContain(item =>
            item.MediaItemId == movieId &&
            item.Status == CopyPrepSelectionStatus.CopyReady &&
            item.IsSelectable == false);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~QueryCopyPrepSelectionStatesHandlerTests`

Expected: FAIL with missing `CopyPrepSelectionStateViewModel`, `QueryCopyPrepSelectionStates`, and handler types.

- [ ] **Step 3: Write the minimal query contracts and handler**

```csharp
namespace ErsatzTV.Application.CopyPrep;

public enum CopyPrepSelectionStatus
{
    CopyReady,
    NeedsCopyPrep
}

public record CopyPrepSelectionStateViewModel(
    int MediaItemId,
    string MediaKind,
    CopyPrepSelectionStatus Status,
    string Label,
    bool IsSelectable,
    string Reason);
```

```csharp
namespace ErsatzTV.Application.CopyPrep.Queries;

public record QueryCopyPrepSelectionStates(
    List<int> MovieIds,
    List<int> EpisodeIds,
    List<int> MusicVideoIds,
    List<int> OtherVideoIds)
    : IRequest<List<CopyPrepSelectionStateViewModel>>;
```

```csharp
public class QueryCopyPrepSelectionStatesHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<QueryCopyPrepSelectionStates, List<CopyPrepSelectionStateViewModel>>
{
    public async Task<List<CopyPrepSelectionStateViewModel>> Handle(
        QueryCopyPrepSelectionStates request,
        CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var result = new List<CopyPrepSelectionStateViewModel>();

        result.AddRange(await ProjectMovies(dbContext, request.MovieIds, cancellationToken));
        result.AddRange(await ProjectEpisodes(dbContext, request.EpisodeIds, cancellationToken));
        result.AddRange(await ProjectMusicVideos(dbContext, request.MusicVideoIds, cancellationToken));
        result.AddRange(await ProjectOtherVideos(dbContext, request.OtherVideoIds, cancellationToken));

        return result;
    }

    private static CopyPrepSelectionStateViewModel Project(string mediaKind, int mediaItemId, MediaVersion version, string path)
    {
        CopyPrepDecision decision = CopyPrepAnalyzer.Analyze(version, path);
        bool needsCopyPrep = decision.ShouldQueue;

        return new CopyPrepSelectionStateViewModel(
            mediaItemId,
            mediaKind,
            needsCopyPrep ? CopyPrepSelectionStatus.NeedsCopyPrep : CopyPrepSelectionStatus.CopyReady,
            needsCopyPrep ? "needs copy-prep" : "copy-ready",
            needsCopyPrep,
            decision.Summary);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~QueryCopyPrepSelectionStatesHandlerTests`

Expected: PASS with 2 passing tests.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add \
  ErsatzTV.Application/CopyPrep/CopyPrepSelectionStateViewModel.cs \
  ErsatzTV.Application/CopyPrep/Queries/QueryCopyPrepSelectionStates.cs \
  ErsatzTV.Application/CopyPrep/Queries/QueryCopyPrepSelectionStatesHandler.cs \
  ErsatzTV.Tests/Services/QueryCopyPrepSelectionStatesHandlerTests.cs
git -C D:\project\ErsatzTV commit -m "feat: add search copy-prep selection state query"
```

---

### Task 2: Add bulk selected-item copy-prep command with dedupe and retry semantics

**Files:**
- Create: `ErsatzTV.Application/CopyPrep/AddItemsToCopyPrepResult.cs`
- Create: `ErsatzTV.Application/CopyPrep/Commands/AddItemsToCopyPrep.cs`
- Create: `ErsatzTV.Application/CopyPrep/Commands/AddItemsToCopyPrepHandler.cs`
- Modify: `ErsatzTV.Application/CopyPrep/Commands/RetryCopyPrepQueueItemHandler.cs` (only if extracting a shared queue-item reset helper reduces duplication cleanly)
- Test: `ErsatzTV.Tests/Services/AddItemsToCopyPrepHandlerTests.cs`

- [ ] **Step 1: Write the failing command tests**

```csharp
using ErsatzTV.Application.CopyPrep.Commands;
using ErsatzTV.Core.Domain.CopyPrep;
using ErsatzTV.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace ErsatzTV.Tests.Services;

[TestFixture]
public class AddItemsToCopyPrepHandlerTests
{
    [Test]
    public async Task Should_queue_new_eligible_item_and_write_search_selection_log()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            path: @"D:\media\movies\queued-from-search.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");

        var sut = new AddItemsToCopyPrepHandler(factory);

        AddItemsToCopyPrepResult result = await sut.Handle(
            new AddItemsToCopyPrep([movieId], [], [], []),
            CancellationToken.None);

        result.QueuedCount.ShouldBe(1);
        result.RetriedCount.ShouldBe(0);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        dbContext.CopyPrepQueueItems.ShouldContain(item => item.MediaItemId == movieId && item.Status == CopyPrepStatus.Queued);
        dbContext.CopyPrepQueueLogEntries.ShouldContain(log => log.Event == "queued_from_search_selection");
    }

    [Test]
    public async Task Should_requeue_failed_item_without_creating_duplicate_row()
    {
        await using var factory = await TestDbContextFactory.Create();
        int movieId = await factory.SeedMovie(
            path: @"D:\media\movies\retry-me.mkv",
            videoCodec: "h264",
            audioCodec: "aac",
            pixelFormat: "yuv420p",
            sampleAspectRatio: "1:1",
            frameRate: "25/1");
        int queueItemId = await factory.SeedQueueItemForMediaItem(movieId, CopyPrepStatus.Failed);

        var sut = new AddItemsToCopyPrepHandler(factory);

        AddItemsToCopyPrepResult result = await sut.Handle(
            new AddItemsToCopyPrep([movieId], [], [], []),
            CancellationToken.None);

        result.QueuedCount.ShouldBe(0);
        result.RetriedCount.ShouldBe(1);

        await using TvContext dbContext = await factory.CreateDbContextAsync(CancellationToken.None);
        dbContext.CopyPrepQueueItems.Count(item => item.MediaItemId == movieId).ShouldBe(1);
        dbContext.CopyPrepQueueItems.Single(item => item.Id == queueItemId).Status.ShouldBe(CopyPrepStatus.Queued);
        dbContext.CopyPrepQueueLogEntries.ShouldContain(log => log.Event == "manual_retry_from_search_selection");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~AddItemsToCopyPrepHandlerTests`

Expected: FAIL with missing command/result/handler types.

- [ ] **Step 3: Write the minimal bulk command implementation**

```csharp
namespace ErsatzTV.Application.CopyPrep;

public record AddItemsToCopyPrepResult(
    int QueuedCount,
    int RetriedCount,
    int SkippedCopyReadyCount,
    int SkippedExistingActiveCount,
    int SkippedUnsupportedCount,
    int SkippedMissingCount);
```

```csharp
namespace ErsatzTV.Application.CopyPrep.Commands;

public record AddItemsToCopyPrep(
    List<int> MovieIds,
    List<int> EpisodeIds,
    List<int> MusicVideoIds,
    List<int> OtherVideoIds)
    : IRequest<AddItemsToCopyPrepResult>;
```

```csharp
public class AddItemsToCopyPrepHandler(IDbContextFactory<TvContext> dbContextFactory)
    : IRequestHandler<AddItemsToCopyPrep, AddItemsToCopyPrepResult>
{
    public async Task<AddItemsToCopyPrepResult> Handle(AddItemsToCopyPrep request, CancellationToken cancellationToken)
    {
        await using TvContext dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        int queued = 0;
        int retried = 0;
        int skippedCopyReady = 0;
        int skippedExisting = 0;
        int skippedUnsupported = 0;
        int skippedMissing = 0;

        foreach (SelectedMediaItem item in await LoadSelectedMediaItems(dbContext, request, cancellationToken))
        {
            if (!item.IsSupported)
            {
                skippedUnsupported++;
                continue;
            }

            if (!item.Exists)
            {
                skippedMissing++;
                continue;
            }

            CopyPrepDecision decision = CopyPrepAnalyzer.Analyze(item.Version, item.Path);
            if (!decision.ShouldQueue)
            {
                skippedCopyReady++;
                continue;
            }

            CopyPrepQueueItem existing = await dbContext.CopyPrepQueueItems
                .FirstOrDefaultAsync(queueItem => queueItem.MediaItemId == item.MediaItemId, cancellationToken);

            if (existing is null)
            {
                await QueueNewItem(dbContext, item, decision, cancellationToken);
                queued++;
                continue;
            }

            if (existing.Status is CopyPrepStatus.Failed or CopyPrepStatus.Canceled or CopyPrepStatus.Skipped)
            {
                ResetForRetry(existing);
                dbContext.CopyPrepQueueLogEntries.Add(new CopyPrepQueueLogEntry
                {
                    CopyPrepQueueItemId = existing.Id,
                    CreatedAt = DateTime.UtcNow,
                    Level = "Information",
                    Event = "manual_retry_from_search_selection",
                    Message = "Queue item manually re-queued from search selection"
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                retried++;
                continue;
            }

            skippedExisting++;
        }

        return new AddItemsToCopyPrepResult(queued, retried, skippedCopyReady, skippedExisting, skippedUnsupported, skippedMissing);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~AddItemsToCopyPrepHandlerTests`

Expected: PASS with the new queueing/retry tests green.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add \
  ErsatzTV.Application/CopyPrep/AddItemsToCopyPrepResult.cs \
  ErsatzTV.Application/CopyPrep/Commands/AddItemsToCopyPrep.cs \
  ErsatzTV.Application/CopyPrep/Commands/AddItemsToCopyPrepHandler.cs \
  ErsatzTV.Tests/Services/AddItemsToCopyPrepHandlerTests.cs
git -C D:\project\ErsatzTV commit -m "feat: add bulk selected-item copy-prep command"
```

---

### Task 3: Add pure Search page state helper for library-scope and selection rules

**Files:**
- Create: `ErsatzTV/Pages/SearchCopyPrepPageState.cs`
- Test: `ErsatzTV.Tests/Pages/SearchCopyPrepPageStateTests.cs`

- [ ] **Step 1: Write the failing page-state tests**

```csharp
using ErsatzTV.Application.CopyPrep;
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
        SearchCopyPrepPageState.IsLibraryScopedQuery("title:foo").ShouldBeFalse();
    }

    [Test]
    public void Should_allow_selection_only_for_needs_copy_prep_items()
    {
        var eligible = new CopyPrepSelectionStateViewModel(101, "movie", CopyPrepSelectionStatus.NeedsCopyPrep, "needs copy-prep", true, "mkv");
        var ready = new CopyPrepSelectionStateViewModel(102, "movie", CopyPrepSelectionStatus.CopyReady, "copy-ready", false, string.Empty);

        SearchCopyPrepPageState.CanSelect(eligible).ShouldBeTrue();
        SearchCopyPrepPageState.CanSelect(ready).ShouldBeFalse();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~SearchCopyPrepPageStateTests`

Expected: FAIL because `SearchCopyPrepPageState` does not exist.

- [ ] **Step 3: Write the minimal page-state helper**

```csharp
using ErsatzTV.Application.CopyPrep;
using ErsatzTV.Application.MediaCards;
using System.Text.RegularExpressions;

namespace ErsatzTV.Pages;

public static class SearchCopyPrepPageState
{
    private static readonly Regex LibraryQueryRegex = new(@"^library_id:\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsLibraryScopedQuery(string query) =>
        !string.IsNullOrWhiteSpace(query) && LibraryQueryRegex.IsMatch(query.Trim());

    public static bool CanSelect(CopyPrepSelectionStateViewModel state) =>
        state is not null && state.IsSelectable && state.Status == CopyPrepSelectionStatus.NeedsCopyPrep;

    public static IEnumerable<MediaCardViewModel> GetSelectableCards(
        IEnumerable<MediaCardViewModel> visibleCards,
        IReadOnlyDictionary<int, CopyPrepSelectionStateViewModel> stateByMediaItemId) =>
        visibleCards.Where(card =>
            stateByMediaItemId.TryGetValue(card.MediaItemId, out CopyPrepSelectionStateViewModel state) &&
            CanSelect(state));
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~SearchCopyPrepPageStateTests`

Expected: PASS with the library-scope and selectability tests green.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add \
  ErsatzTV/Pages/SearchCopyPrepPageState.cs \
  ErsatzTV.Tests/Pages/SearchCopyPrepPageStateTests.cs
git -C D:\project\ErsatzTV commit -m "feat: add search copy-prep page state helper"
```

---

### Task 4: Wire badges and selection-based copy-prep flow into the search page

**Files:**
- Modify: `ErsatzTV/Shared/MediaCard.razor`
- Modify: `ErsatzTV/Pages/Search.razor`
- Test: reuse `ErsatzTV.Tests/Pages/SearchCopyPrepPageStateTests.cs` for helper logic; use targeted manual verification for the Razor page if no Razor-component test harness exists

- [ ] **Step 1: Add failing coverage for the new helper behavior used by the page**

```csharp
[Test]
public void Should_return_only_selectable_cards_for_select_all_eligible()
{
    var cards = new MediaCardViewModel[]
    {
        new MovieCardViewModel { MediaItemId = 1, Title = "Needs Work" },
        new MovieCardViewModel { MediaItemId = 2, Title = "Ready" }
    };

    IReadOnlyDictionary<int, CopyPrepSelectionStateViewModel> stateByMediaItemId =
        new Dictionary<int, CopyPrepSelectionStateViewModel>
        {
            [1] = new(1, "movie", CopyPrepSelectionStatus.NeedsCopyPrep, "needs copy-prep", true, "mkv"),
            [2] = new(2, "movie", CopyPrepSelectionStatus.CopyReady, "copy-ready", false, string.Empty)
        };

    SearchCopyPrepPageState.GetSelectableCards(cards, stateByMediaItemId)
        .Select(card => card.MediaItemId)
        .ShouldBe([1]);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~SearchCopyPrepPageStateTests`

Expected: FAIL until `GetSelectableCards` and any required card fixtures are in place.

- [ ] **Step 3: Modify `MediaCard.razor` and `Search.razor` minimally**

```razor
@* MediaCard.razor *@
@if (!string.IsNullOrWhiteSpace(StatusBadgeText))
{
    <MudChip T="string"
             Size="Size.Small"
             Color="StatusBadgeColor"
             Variant="Variant.Filled"
             Style="margin: 8px auto 0 auto; display: flex; width: fit-content;">
        @StatusBadgeText
    </MudChip>
}

@code {
    [Parameter] public string StatusBadgeText { get; set; }
    [Parameter] public Color StatusBadgeColor { get; set; } = Color.Default;
}
```

```csharp
// Search.razor @code additions
private readonly Dictionary<int, CopyPrepSelectionStateViewModel> _copyPrepStates = [];

private bool ShowCopyPrepUi() => SearchCopyPrepPageState.IsLibraryScopedQuery(_query);

private async Task LoadCopyPrepStates()
{
    if (!ShowCopyPrepUi())
    {
        _copyPrepStates.Clear();
        return;
    }

    List<int> movieIds = _movies?.Cards.Select(card => card.MediaItemId).ToList() ?? [];
    List<int> episodeIds = _episodes?.Cards.Select(card => card.MediaItemId).ToList() ?? [];
    List<int> musicVideoIds = _musicVideos?.Cards.Select(card => card.MediaItemId).ToList() ?? [];
    List<int> otherVideoIds = _otherVideos?.Cards.Select(card => card.MediaItemId).ToList() ?? [];

    List<CopyPrepSelectionStateViewModel> states = await Mediator.Send(
        new QueryCopyPrepSelectionStates(movieIds, episodeIds, musicVideoIds, otherVideoIds),
        CancellationToken);

    _copyPrepStates.Clear();
    foreach (CopyPrepSelectionStateViewModel state in states)
    {
        _copyPrepStates[state.MediaItemId] = state;
    }
}

private async Task AddSelectedToCopyPrep(MouseEventArgs _)
{
    AddItemsToCopyPrepResult result = await Mediator.Send(
        new AddItemsToCopyPrep(
            SelectedItems.OfType<MovieCardViewModel>().Select(card => card.MovieId).ToList(),
            SelectedItems.OfType<TelevisionEpisodeCardViewModel>().Select(card => card.EpisodeId).ToList(),
            SelectedItems.OfType<MusicVideoCardViewModel>().Select(card => card.MusicVideoId).ToList(),
            SelectedItems.OfType<OtherVideoCardViewModel>().Select(card => card.OtherVideoId).ToList()),
        CancellationToken);

    Snackbar.Add($"Queued {result.QueuedCount}, retried {result.RetriedCount}, skipped active {result.SkippedExistingActiveCount}, skipped ready {result.SkippedCopyReadyCount}", Severity.Success);
    ClearSelection();
    await LoadCopyPrepStates();
}
```

```razor
@* Search.razor button area additions *@
@if (ShowCopyPrepUi())
{
    <MudButton Class="ml-3"
               Variant="Variant.Filled"
               Color="Color.Primary"
               StartIcon="@Icons.Material.Filled.DoneAll"
               OnClick="@(_ => SelectAllPageItems(SearchCopyPrepPageState.GetSelectableCards(GetVisibleLeafCards(), _copyPrepStates)))">
        Select All Eligible
    </MudButton>

    <MudButton Class="ml-3"
               Variant="Variant.Filled"
               Color="Color.Secondary"
               StartIcon="@Icons.Material.Filled.Queue"
               Disabled="@(SelectedItems.Count == 0)"
               OnClick="@AddSelectedToCopyPrep">
        Add Selected To Copy-Prep
    </MudButton>
}
```

- [ ] **Step 4: Run tests and perform a focused manual page verification**

Run:
- `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~SearchCopyPrepPageStateTests`
- `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~QueryCopyPrepSelectionStatesHandlerTests`
- `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter FullyQualifiedName~AddItemsToCopyPrepHandlerTests`

Then manually verify in the running app:
- open `/media/libraries`
- click a library search icon so the URL is `/search?query=library_id:<id>`
- confirm `copy-ready` and `needs copy-prep` badges render only for visible supported cards
- confirm `copy-ready` cards do not enter selection
- confirm `Select All Eligible` selects only `needs copy-prep` cards
- confirm `Add Selected To Copy-Prep` shows a summary snackbar and queue entries appear on `/copy-prep`

Expected: tests PASS and manual UI behavior matches the approved spec.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add \
  ErsatzTV/Shared/MediaCard.razor \
  ErsatzTV/Pages/Search.razor \
  ErsatzTV/Pages/SearchCopyPrepPageState.cs \
  ErsatzTV.Tests/Pages/SearchCopyPrepPageStateTests.cs
git -C D:\project\ErsatzTV commit -m "feat: add search page copy-prep selection flow"
```

---

### Task 5: Run full verification and clean up the branch

**Files:**
- Modify: none expected
- Verify: `ErsatzTV.Tests/ErsatzTV.Tests.csproj`
- Verify: running app at `http://localhost:8409`

- [ ] **Step 1: Run the targeted copy-prep and page-state tests together**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj --filter "FullyQualifiedName~CopyPrep|FullyQualifiedName~SearchCopyPrepPageStateTests"`

Expected: PASS with the new query, command, and page-state tests green.

- [ ] **Step 2: Run the full test project**

Run: `dotnet test D:\project\ErsatzTV\ErsatzTV.Tests\ErsatzTV.Tests.csproj`

Expected: PASS with `Test Run Successful`.

- [ ] **Step 3: Smoke test the queue end-to-end in the app**

```text
1. Open a library search page.
2. Confirm at least one visible item shows `needs copy-prep`.
3. Select one eligible item.
4. Click `Add Selected To Copy-Prep`.
5. Open `/copy-prep`.
6. Confirm a queued or retried item exists with a `queued_from_search_selection` or `manual_retry_from_search_selection` log entry.
```

Expected: queued item appears without duplicate active work for already-queued/replaced items.

- [ ] **Step 4: Inspect git diff and prepare the branch for review**

Run:
- `git -C D:\project\ErsatzTV status --short`
- `git -C D:\project\ErsatzTV log --oneline -5`

Expected: only intended feature files are modified and the last commits correspond to the plan tasks.

- [ ] **Step 5: Commit any final polish**

```bash
git -C D:\project\ErsatzTV add -A
git -C D:\project\ErsatzTV commit -m "test: verify search copy-prep selection flow" || echo "No final changes to commit"
```

---

## Spec Coverage Check

- Library-search-only scope: covered by Task 3 and Task 4 query gating
- Visible-item-only selection: covered by Task 3 helper and Task 4 button wiring
- Leaf-video-only support: covered by Task 1 and Task 2 handler inputs
- `copy-ready` vs `needs copy-prep` badge state: covered by Task 1 query and Task 4 card rendering
- Selection-only submit path: covered by Task 4 `AddSelectedToCopyPrep`
- Deduplication/retry rules: covered by Task 2 handler tests and implementation
- Summary feedback: covered by Task 4 snackbar behavior
- Tests: covered by Tasks 1–5

## Placeholder Scan

- No `TODO` or `TBD`
- All new files are named explicitly
- All commands are concrete
- All implementation steps include concrete code or explicit manual verification steps

## Type Consistency Check

- Query type: `QueryCopyPrepSelectionStates`
- View model: `CopyPrepSelectionStateViewModel`
- Enum: `CopyPrepSelectionStatus`
- Command type: `AddItemsToCopyPrep`
- Result type: `AddItemsToCopyPrepResult`
- UI helper: `SearchCopyPrepPageState`

These names are used consistently throughout the plan.
