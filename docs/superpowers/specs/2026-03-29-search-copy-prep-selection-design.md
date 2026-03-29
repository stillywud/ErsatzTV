# Search Page Copy-Prep Selection Design

Date: 2026-03-29
Project: ErsatzTV
Status: Draft approved in chat, written for review

## Summary

Add a manual copy-prep entry point to the search page for library-scoped searches. The feature will let users see which currently visible video items need copy-prep, select only eligible items, and submit those selected items into the copy-prep queue.

This is intentionally a first-pass, bounded feature. It does not attempt whole-library background analysis, cross-page selection persistence, or container expansion.

## Goals

- Add a manual way to queue existing library items for copy-prep.
- Show users whether each eligible visible item is already copy-ready or still needs copy-prep.
- Allow selection only for items that currently need copy-prep.
- Reuse the existing copy-prep analyzer, queue model, retry semantics, and status model.
- Deduplicate queue operations so users do not create duplicate active work.

## Non-Goals

- No support for generic search pages outside `library_id:<id>`.
- No support for selecting shows, seasons, songs, images, or remote streams.
- No automatic expansion of shows/seasons into episodes.
- No whole-library batch analysis beyond the items currently rendered on the page.
- No cross-page or cross-navigation selection persistence.
- No change to the core copy-prep processing pipeline.

## User Experience

### Entry point

The feature appears only on search pages where the query is a direct library query:

- `/search?query=library_id:<id>`

It does not appear on arbitrary text searches or other search query shapes.

### Visible item states

For currently displayed leaf video items, the UI shows one of two states:

- `copy-ready`
- `needs copy-prep`

These states are derived from the same backend copy-prep analyzer used by scan-time queueing.

### Selectability rules

Only items with `needs copy-prep` are selectable for this flow.

Items marked `copy-ready` are visible but not selectable.

### Action model

The interaction model is selection-based, not whole-result-set based.

The main actions are:

- `Select All Eligible`
- `Add Selected To Copy-Prep`
- `Clear Selection`

`Add Selected To Copy-Prep` only operates on currently visible, currently loaded, currently selected eligible items.

### Scope of selection

Selection scope is limited to the currently visible/rendered items on the current page.

If a result type has more items than the search page currently shows, the user must navigate into the corresponding "See All" page and repeat the action there.

There is no cross-page remembered selection in this first version.

## Supported Media Types

Only these leaf video item types participate in the feature:

- Movies
- Episodes
- Music Videos
- Other Videos

These types are excluded from copy-prep selection in this feature:

- Shows
- Seasons
- Artists
- Songs
- Images
- Remote Streams

## Qualification Logic

The backend remains the source of truth.

### Analyzer usage

Eligibility is determined with the existing analyzer:

- `CopyPrepAnalyzer.Analyze(version, file.Path)`

Mapping:

- `ShouldQueue = false` -> `copy-ready`
- `ShouldQueue = true` -> `needs copy-prep`

### Why the analyzer is reused

This keeps manual queueing behavior aligned with scan-time queueing behavior. Users should not see one rule in the UI and a different rule during scanning.

## Deduplication and Retry Rules

For each selected media item, the backend checks for an existing copy-prep queue item for that media item.

Rules:

- If an existing queue item is in `Queued`, `Processing`, `Prepared`, or `Replaced` -> skip
- If an existing queue item is in `Failed`, `Canceled`, or `Skipped` -> re-queue
- If no queue item exists -> create a new queue item

### Re-queue behavior

Re-queue should reuse the existing queue item rather than creating a duplicate row.

The new bulk command should follow the same reset semantics used by the existing single-item retry flow:

- set status back to `Queued`
- clear terminal timestamps and last error state as appropriate
- update queue timestamps
- add a retry log entry

## Frontend Design

### Search page changes

Primary file:

- `ErsatzTV/Pages/Search.razor`

### Conditional UI

Show copy-prep UI only when the query is a direct library query (`library_id:<id>`).

### Status loading

After the page has loaded its currently displayed leaf video results, request copy-prep selection states for those visible IDs.

The page should maintain a state map keyed by media kind + media item id so card rendering and selection checks can consult copy-prep state cheaply.

### Card rendering

Each supported visible card gets a compact badge or label showing:

- `copy-ready`
- `needs copy-prep`

### Selection behavior

Selection click handling must reject unsupported or ineligible items.

That means:

- unsupported kinds cannot participate
- `copy-ready` items cannot enter the copy-prep selection set
- only `needs copy-prep` items may be selected

### Button area

For library search pages, add:

- `Select All Eligible`
- `Add Selected To Copy-Prep`
- `Clear Selection`

`Add All To Collection` and `Add All To Playlist` remain unchanged.

## Backend Design

### Query: selection states for visible items

Add an application-layer query under copy-prep queries, conceptually:

- `QueryCopyPrepSelectionStates`

Input:

- movie ids
- episode ids
- music video ids
- other video ids

Output:

A list of view models containing at least:

- media item id
- media kind
- display status (`CopyReady` or `NeedsCopyPrep`)
- `IsSelectable`
- optional reason text for future tooltip/debug use

The handler loads the relevant media items, resolves the head version and primary file, and runs `CopyPrepAnalyzer`.

### Command: bulk add selected items to copy-prep

Add an application-layer command under copy-prep commands, conceptually:

- `AddItemsToCopyPrep`

Input:

- movie ids
- episode ids
- music video ids
- other video ids

Output:

A result summary object containing counts such as:

- `QueuedCount`
- `RetriedCount`
- `SkippedCopyReadyCount`
- `SkippedExistingActiveCount`
- `SkippedUnsupportedCount`
- `SkippedMissingCount`

### Command handling flow

For each deduplicated selected item:

1. Resolve the actual media item, head version, and primary file
2. Reject unsupported/missing items
3. Re-run `CopyPrepAnalyzer` as authoritative validation
4. If analyzer says copy-ready, skip
5. Check existing copy-prep queue item state
6. Create a new queued item, or re-queue an existing failed/canceled/skipped item, or skip an active/completed one
7. Record appropriate log entries

## Logging

New manual-search-driven queue actions should be distinguishable from scan-driven queue actions.

Suggested event names:

- `queued_from_search_selection`
- `manual_retry_from_search_selection`

This keeps auditability and debugging clear.

## Error Handling

- Missing media item/version/file -> skip and count in summary
- Unsupported type -> skip and count in summary
- Analyzer says copy-ready -> skip and count in summary
- Existing active/completed queue item -> skip and count in summary
- Partial success is acceptable; the command should return a summary rather than fail the whole operation because one item could not be processed

## Testing Plan

### Application tests: selection state query

Cover:

- items that need copy-prep map to `NeedsCopyPrep`
- items already compatible map to `CopyReady`
- unsupported/missing items are non-selectable or omitted as designed

### Application tests: bulk add command

Cover:

- new eligible items create queued rows
- failed/canceled/skipped items are re-queued
- queued/processing/prepared/replaced items are skipped
- copy-ready items are skipped
- duplicate input ids do not create duplicate work

### UI tests

Cover at least the critical behaviors:

- copy-prep controls appear only for `library_id:<id>` searches
- only `NeedsCopyPrep` items can be selected
- `Select All Eligible` selects only eligible visible items
- `Add Selected To Copy-Prep` invokes the command and surfaces a summary

## Implementation Notes

- Prefer reusing existing multi-select infrastructure in `Search.razor` instead of building a separate selection system.
- Prefer extracting shared retry-state-reset behavior so bulk manual queueing and single-item retry cannot drift.
- Keep the first version constrained and explicit rather than trying to unify every search batch action.

## Risks and Trade-Offs

### Chosen trade-off: visible-items-only

The feature intentionally acts only on visible/currently loaded items. This keeps the UI honest and predictable, but means users must repeat the action on deeper result pages when a result set is truncated on the main search page.

### Chosen trade-off: analyzer on both UI and submit paths

The frontend query gives users explanatory state, but the backend command still re-runs the analyzer. This adds redundant work by design and avoids stale-state mistakes.

## Recommended Next Step

After spec approval, write an implementation plan and then implement in this order:

1. copy-prep selection state query + tests
2. bulk add-to-copy-prep command + tests
3. `Search.razor` UI wiring and selection restrictions
4. verification pass in the running app
