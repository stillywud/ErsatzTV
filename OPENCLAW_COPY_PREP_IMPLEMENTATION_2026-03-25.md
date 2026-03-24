# Copy-prep MVP implementation notes (2026-03-25)

This note records what was actually implemented after `OPENCLAW_COPY_PREP_DESIGN_2026-03-25.md`.

## Implemented

### Data model
Added new entities:
- `CopyPrepQueueItem`
- `CopyPrepQueueLogEntry`
- `CopyPrepStatus`

Added EF configuration and provider migrations:
- SQLite migration: `20260324174722_Add_CopyPrepQueue`
- MySQL migration: `20260324175629_Add_CopyPrepQueue`

### Scanner integration
Local scan flow now evaluates local video items for obvious HLS-copy incompatibilities and queues them when background copy-prep is enabled.

Current scan integration covers:
- movies
- episodes
- music videos
- other videos

Current heuristic checks include:
- non-H.264 video
- non-AAC primary audio
- non-`yuv420p` pixel format
- non-`1:1` SAR
- interlaced video
- non-MP4/M4V extension
- missing/invalid frame-rate metadata
- >8-bit raw sample depth

### Background worker
Added `ErsatzTV/Services/CopyPrepService.cs`.

Current worker behavior:
- waits for DB readiness
- reads copy-prep config from FFmpeg settings
- claims queued jobs from DB
- preprocesses with FFmpeg to a managed working file
- archives the original file under app data
- replaces the active media file with the prepared result
- updates `MediaFile.Path` / `PathHash`
- refreshes statistics from the prepared file
- writes DB lifecycle log entries and per-attempt disk logs

### Config / UI
Extended FFmpeg settings with:
- `ffmpeg.copy_prep.enabled`
- `ffmpeg.copy_prep.cpu_target_percent`
- `ffmpeg.copy_prep.max_concurrent_jobs`
- `ffmpeg.copy_prep.threads_per_job`

UI surface added to:
- `ErsatzTV/Pages/Settings/FFmpegSettings.razor`

Defaults currently returned by query handler:
- enabled: `false`
- cpu target: `50`
- max concurrent jobs: `1`
- threads per job: `0` (auto-derived)

### API
Added a minimal controller:
- `GET /api/copy-prep`
- `POST /api/copy-prep/{id}/retry`

## Operational notes

### Managed folders
`FileSystemLayout` now includes:
- `CopyPrepFolder`
- `CopyPrepWorkingFolder`
- `CopyPrepArchiveFolder`
- `CopyPrepLogsFolder`

### Output / replacement strategy
Current MVP behavior is intentionally practical, not elegant:
- if source already ends in `.mp4`, the prepared output replaces that path in place after archiving the original
- if source is another extension, the prepared output becomes `<basename>.mp4`
- original source is archived under the app-data copy-prep archive tree

This keeps existing playback code working without a large active-file selection refactor.

## Build / tooling notes

### `global.json`
The repo originally specified SDK version `10.0.0`, which current .NET tooling rejects because feature bands start at `10.0.100`.

This was updated to:
- `10.0.100`

### local tool manifest
Added local tool:
- `dotnet-ef` `10.0.0`

This updates:
- `.config/dotnet-tools.json`

### MySQL migration generation support
To allow migration scaffolding without forcing live server auto-detection, startup code now honors optional config:
- `MySql:ServerVersion`

If set, startup uses that version instead of `ServerVersion.AutoDetect(...)`.
This was used to scaffold the MySQL migration locally.

## Validation performed

Successful full solution build after changes:
- `D:\project\ErsatzTV\build-logs\manual-20260325-015741\03-build.log`

The build flow still temporarily removes the `ErsatzTV.Scanner` project reference from `ErsatzTV\ErsatzTV.csproj` during local/manual builds, matching the existing repo/CI workaround, and restores it afterward.

## Not yet implemented

- dedicated queue UI page
- per-item cancel/purge controls
- manual enqueue from media details page
- deeper GOP/keyframe-gap analyzer before queueing
- smarter retry/backoff policy
- remote media-server copy-prep support
- more sophisticated prepared-asset mapping model

## Good next steps

1. Add a queue/details page in the Blazor UI
2. Add a manual enqueue action from movie/episode/media-card views
3. Improve candidate detection using GOP/keyframe analysis
4. Decide whether long-term replacement should stay path-based or move to an explicit prepared-asset mapping model
5. Add integration tests around queue creation and replacement behavior
