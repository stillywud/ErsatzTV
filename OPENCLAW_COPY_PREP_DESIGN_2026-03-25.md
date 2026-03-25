# ErsatzTV integrated copy-prep design (2026-03-25)

## Goal

Add an internal, queue-driven background preprocessing system so local-library video files that are poor candidates for HLS/IPTV `copy` playback can be normalized once in the background, tracked with lifecycle/audit data, and then switched over for later lower-CPU playback.

This is intended to complement ErsatzTV's existing runtime FFmpeg/transcode pipeline, not replace it.

---

## What was inspected

### Import / scan entry points
- Main app queues library scans through `QueueLibraryScanByLibraryIdHandler`
- Main app scan orchestration runs in `ErsatzTV/Services/ScannerService.cs`
- Local scans execute in the separate scanner process via:
  - `ErsatzTV.Application/MediaSources/Commands/CallLocalLibraryScannerHandler.cs`
  - `ErsatzTV.Scanner/Application/MediaSources/Commands/ScanLocalLibraryHandler.cs`
- Local media materialization / folder traversal happens in:
  - `ErsatzTV.Scanner/Core/Metadata/LocalFolderScanner.cs`
  - `MovieFolderScanner.cs`
  - `TelevisionFolderScanner.cs`
  - `MusicVideoFolderScanner.cs`
  - `OtherVideoFolderScanner.cs`

### Media analysis / probe path
- Local ffprobe/ffmpeg statistics live in `ErsatzTV.Infrastructure/Metadata/LocalStatisticsProvider.cs`
- Media file/version entities live in:
  - `ErsatzTV.Core/Domain/MediaItem/MediaFile.cs`
  - `ErsatzTV.Core/Domain/MediaItem/MediaVersion.cs`
  - `ErsatzTV.Core/Domain/MediaItem/MediaStream.cs`
- Scan flow already refreshes statistics per imported file before metadata/subtitle/chapter updates.

### Background worker / service extension points
- Generic background queue: `ErsatzTV/Services/WorkerService.cs`
- Periodic scheduler: `ErsatzTV/Services/SchedulerService.cs`
- Hosted service registration: `ErsatzTV/Startup.cs`
- Database readiness gate: `ErsatzTV.Core/SystemStartup.cs`

### Config extension points
- Global config keys: `ErsatzTV.Core/Domain/ConfigElementKey.cs`
- FFmpeg settings UI / handlers:
  - `ErsatzTV.Application/FFmpegProfiles/FFmpegSettingsViewModel.cs`
  - `GetFFmpegSettingsHandler.cs`
  - `UpdateFFmpegSettingsHandler.cs`
  - `ErsatzTV/Pages/Settings/FFmpegSettings.razor`

### Database / persistence extension points
- Main EF context: `ErsatzTV.Infrastructure/Data/TvContext.cs`
- Entity configurations: `ErsatzTV.Infrastructure/Data/Configurations/**`
- Migrations are provider-specific in:
  - `ErsatzTV.Infrastructure.Sqlite/Migrations`
  - `ErsatzTV.Infrastructure.MySql/Migrations`

### API / UI extension points
- Existing API controller pattern: `ErsatzTV/Controllers/Api/*.cs`
- Media info / troubleshooting UI already exposes a natural place to add copy-prep visibility later.

---

## MVP design choices

## 1. Scope: local libraries only

MVP only targets **local-library video media**:
- movies
- television episodes
- music videos
- other videos

Not included in MVP:
- Plex / Jellyfin / Emby imported media
- songs
- images
- remote streams

Reason: local files are the safest place to perform deterministic background preprocessing and path replacement without depending on remote media-server mutation APIs.

---

## 2. Queue model

Add two new tables/entities:

### `CopyPrepQueueItem`
Tracks one preprocessing job tied to a specific media item / media file.

Planned fields:
- `Id`
- `MediaItemId`
- `MediaVersionId`
- `MediaFileId`
- `Status`
- `Reason`
- `SourcePath`
- `TargetPath`
- `ArchivePath`
- `WorkingPath`
- `LastLogPath`
- `LastCommand`
- `LastError`
- `LastExitCode`
- `AttemptCount`
- `CreatedAt`
- `UpdatedAt`
- `QueuedAt`
- `StartedAt`
- `CompletedAt`
- `FailedAt`
- `CanceledAt`
- `ReplacedAt`

### `CopyPrepQueueLogEntry`
Append-only lifecycle/audit log entries per queue item.

Planned fields:
- `Id`
- `CopyPrepQueueItemId`
- `CreatedAt`
- `Level`
- `Event`
- `Message`
- `Details`

### Status enum
Use explicit lifecycle states:
- `Queued`
- `Processing`
- `Prepared`
- `Failed`
- `Canceled`
- `Replaced`
- `Skipped`

For the MVP implementation, most successful jobs will end at `Replaced` because preparation and switch-over happen in one flow.

---

## 3. Suitability detection strategy (MVP)

The external standalone tool proved that many HLS-copy failures come from a combination of codec/container/timing/keyframe structure. Some of that is easy to detect from existing ffprobe metadata; some is not.

### MVP rule
Queue a file when existing probe data indicates obvious copy-mode risk, for example:
- video codec is not H.264
- primary audio codec is not AAC
- video pixel format is not `yuv420p`
- SAR is not `1:1`
- stream is interlaced
- extension/container is not MP4/M4V
- video bit depth / stream profile looks unfriendly for broad copy-mode playback

### Intentional limitation
MVP will **not** attempt full GOP/keyframe-gap inspection before queueing. That deeper inspection can be added later by extending probe logic or adding a dedicated analyzer pass.

---

## 4. Where queueing happens

Queueing should happen **inside the scanner flow after local statistics are refreshed**.

Why here:
- the scan path already has the fresh `MediaVersion`/`MediaStream` data
- it runs only when imported/changed media is being processed
- it avoids a second repo-wide sweep just to discover candidates

Planned insertion point:
- in the local video folder scanners, immediately after `UpdateStatistics(...)`

---

## 5. Background processing model

Add a dedicated hosted service in the main app:
- `CopyPrepService : BackgroundService`

Responsibilities:
- wait for DB readiness
- poll the queue for `Queued` items when feature is enabled
- claim work by moving item state to `Processing`
- run FFmpeg preprocessing to a temporary working file
- validate output existence
- perform switch-over / replacement
- refresh stored media statistics
- write lifecycle log entries and update status timestamps

### Why a dedicated service instead of reusing `WorkerService`
`WorkerService` is command/message oriented and serial by nature. Copy-prep needs:
- queue polling
- DB-backed lifecycle state
- configurable concurrency / throttling
- on-disk job logs

A dedicated service keeps this isolated and resumable.

---

## 6. CPU-throttling concept

Add global config items under FFmpeg settings:
- `copy prep enabled`
- `copy prep cpu target percent` (default `50`)
- `copy prep max concurrent jobs` (default `1`)
- `copy prep threads per job` (default `0`, meaning auto)

### MVP behavior
Precise CPU-governor logic is out of scope.

Instead, the service derives FFmpeg thread count from:
- logical processor count
- configured CPU target percent
- max concurrent jobs

So the default behavior is roughly:
- one background job at a time
- thread count biased toward about half the machine's available CPU

This matches the user's requirement that “about 50% CPU” is acceptable even if implemented via concurrency/thread caps rather than exact CPU sampling.

---

## 7. Transcode profile for prepared assets

Use the validated standalone-tool approach as the initial integrated preset:
- output container: MP4
- video: `libx264`
- preset: `medium`
- CRF: `18`
- keep source resolution, but force even dimensions
- keep source fps while forcing CFR
- `setsar=1`
- `yuv420p`
- profile `high`
- level `4.1`
- GOP ~ 1 second
- forced 1-second keyframes
- scene-cut disabled
- B-frames disabled
- audio: AAC, 192k, 48kHz, stereo
- `+faststart`

This is intentionally opinionated for stable HLS copy playback.

---

## 8. Switch / replacement strategy

### Chosen MVP strategy
**Replace the active library file reference after successful prep, while archiving the original file.**

Behavior:
- original source is moved to a managed archive location under app data
- prepared file is moved into the library path
- when the source was non-MP4, the active file becomes `<basename>.mp4`
- database `MediaFile.Path` / `PathHash` is updated accordingly
- media statistics are refreshed from the prepared file

### Why this strategy
It minimizes invasive changes to the rest of the codebase because many existing playback / metadata / troubleshooting paths currently assume the “head” media file is the active one.

This avoids having to retrofit a full alternate-file selection layer across dozens of places in the MVP.

### Tradeoff
This is more operationally opinionated than a pure mapping table. A later phase can add a non-destructive prepared-asset mapping model if desired.

---

## 9. Logging / audit design

Per-item auditing will exist at two levels:

### Database lifecycle log
Short structured entries like:
- queued from scanner
- processing started
- FFmpeg completed
- replacement completed
- refresh statistics completed
- failed / rollback attempted

### Disk log file
Each attempt gets its own FFmpeg/log transcript under a copy-prep logs folder.

The queue item stores the latest log path for inspection via API/UI later.
The queue item should also store the final rendered FFmpeg command line so the web/API layer can expose the exact command that was launched.

### Output validation and reuse
Before treating a prepared asset as usable, the integrated worker should validate it with ffprobe-derived metadata (duration sanity plus expected copy-prep output shape such as H.264/AAC, yuv420p, and SAR 1:1).

If the target file already exists and passes that validation, the worker should reuse it instead of rerunning FFmpeg, then archive the original source and switch the active library path. That keeps recovery behavior aligned with the mature external tool instead of doing unnecessary repeat work.

### Centralized transcode profile
The mature copy-prep FFmpeg defaults should live in a single in-source profile/helper instead of being redefined ad hoc inside the worker. That keeps the validated external-tool behavior aligned with the integrated implementation and makes later tuning/UI exposure safer.

---

## 10. API / UI MVP

### Implement now
- extend FFmpeg settings with copy-prep config fields
- add a minimal API surface for queue inspection / retry

### Defer
- dedicated full queue page
- rich progress UI
- cancel / purge / bulk controls
- per-item live streaming logs in browser

---

## 11. File / component plan

### New domain/data pieces
- `ErsatzTV.Core/Domain/CopyPrep/*`
- `ErsatzTV.Infrastructure/Data/Configurations/CopyPrep/*`
- provider migrations in both migration projects

### Scanner-side queue insertion
- `ErsatzTV.Scanner/Core/Metadata/LocalFolderScanner.cs`
- local video folder scanners

### Main-app processing
- `ErsatzTV/Services/CopyPrepService.cs`
- `ErsatzTV/Startup.cs`
- `ErsatzTV.Core/FileSystemLayout.cs`

### Config/UI/API
- `ConfigElementKey.cs`
- `FFmpegSettingsViewModel.cs`
- FFmpeg settings get/update handlers
- `Pages/Settings/FFmpegSettings.razor`
- minimal `CopyPrepController` + query/command handlers

---

## 12. Known follow-up items after MVP

1. Add deeper keyframe / GOP diagnostics before queueing
2. Add manual enqueue from media details page
3. Add queue details page with lifecycle log viewer
4. Add retry / cancel / purge flows in UI
5. Consider a non-destructive “prepared asset mapping” model instead of in-place active-path replacement
6. Add smarter auto-retry policy with backoff
7. Add remote-media-server support where feasible

---

## Summary

The MVP will:
- discover obvious local-library copy-prep candidates during scan
- persist them to a real DB-backed queue with lifecycle audit rows
- expose global throttling / CPU-target config under FFmpeg settings
- process queued items in a dedicated hosted service
- archive originals, switch active media paths to prepared output, and refresh statistics

This keeps the first implementation grounded in existing ErsatzTV architecture while avoiding a large playback-path refactor up front.
