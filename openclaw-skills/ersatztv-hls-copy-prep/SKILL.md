---
name: ersatztv-hls-copy-prep
description: Prepare source files for ErsatzTV HLS/IPTV playback when the goal is to keep low-CPU copy mode (`-c:v copy -c:a copy`). Use when a user wants to avoid live transcoding, when ErsatzTV channels behave differently even with similar settings, or when playback rewind/stutter suggests the source file is not suitable for copy-mode HLS and needs one-time preprocessing or batch conversion.
---

# ErsatzTV HLS Copy Prep

## Overview

Use this skill when the real goal is not “live transcode better”, but “preprocess once so ErsatzTV can stay in copy mode and still play smoothly”.

## What copy-mode HLS requires from the source

When ErsatzTV uses HLS Segmenter with `-c:v copy -c:a copy`, HLS segment boundaries depend on the source file's existing keyframes/GOP structure.

That means the source file should already be close to this shape:

- video codec: H.264
- audio codec: AAC
- stable CFR timing
- dense and regular keyframes
- 1-second GOP or close equivalent
- no B-frames preferred for predictability
- `yuv420p`
- `SAR 1:1`
- keep original fps unless the user explicitly wants frame-rate normalization
- keep original resolution unless the user explicitly wants 1080p normalization

If the source does **not** already look like that, `-hls_time 4` becomes only a suggestion, and clients are much more likely to rewind or jump backward.

## Fast diagnosis

Run the probe script first:

```bash
python scripts/probe_hls_copy_source.py "D:\iptv\show\01.mp4" --ffprobe "D:\apps\ErsatzTV-v26.3.0-win-x64\ffprobe.exe"
```

Treat the source as a bad fit for copy-mode HLS when:

- keyframe gaps are often 6-10+ seconds
- keyframe spacing is highly irregular
- the source is non-standard in shape or timing
- the generated HLS playlist shows uneven segment lengths

Read `references/diagnosis-and-tuning.md` for the reasoning and symptom patterns.

## One-time preprocessing target

Default target when converting a file for later copy-mode HLS playback:

- H.264 video
- AAC audio
- keep source resolution by default
- keep source fps by default, but make it CFR
- 1-second GOP
- forced regular keyframes
- scene-cut disabled
- B-frames disabled
- `yuv420p`
- `SAR 1:1`

Use 1080p output only when the user explicitly wants a uniform library shape.

## Transcode a single source file

Run:

```bash
python scripts/transcode_for_hls_copy.py "D:\iptv\show\01.mp4" "D:\iptv\show-hls-copy\01.mp4" --ffmpeg "D:\apps\ErsatzTV-v26.3.0-win-x64\ffmpeg.exe"
```

This single-file helper is best for testing one representative episode.

## Batch-convert an entire directory

Run:

```bash
python scripts/ersatztv_hls_copy_batch.py "D:\iptv\show" --ffmpeg "D:\apps\ErsatzTV-v26.3.0-win-x64\ffmpeg.exe" --ffprobe "D:\apps\ErsatzTV-v26.3.0-win-x64\ffprobe.exe"
```

That command:

- scans the directory for common video file types
- preserves source fps by default
- preserves source resolution by default
- outputs converted files to a sibling directory named `<input>-hls-copy`

Useful options:

- `--profile balanced|smaller|quality`
- `--resolution-mode source|1080p`
- `--recursive`
- `--overwrite`
- `--dry-run`

Recommended default for most libraries:

```bash
python scripts/ersatztv_hls_copy_batch.py "D:\iptv\show" --ffmpeg "D:\apps\ErsatzTV-v26.3.0-win-x64\ffmpeg.exe" --ffprobe "D:\apps\ErsatzTV-v26.3.0-win-x64\ffprobe.exe" --profile balanced --resolution-mode source
```

## Confirm the output is really copy-friendly

Probe the converted file again:

```bash
python scripts/probe_hls_copy_source.py "D:\iptv\show-hls-copy\01.mp4" --ffprobe "D:\apps\ErsatzTV-v26.3.0-win-x64\ffprobe.exe"
```

A good result looks like:

- H.264 / AAC
- same or intentionally chosen output resolution
- same or intentionally chosen fps
- keyframe gaps consistently near `1.0`

## Guardrails

- Prefer one representative episode first before batch-converting an entire series.
- Keep the original file until the converted file has been validated.
- Do not assume the ErsatzTV profile name tells the truth; inspect runtime logs if needed.
- Do not optimize for minimal filesize before achieving stable regular keyframes.

## Resources

### scripts/

- `scripts/probe_hls_copy_source.py` — inspect stream metadata and keyframe-gap behavior
- `scripts/transcode_for_hls_copy.py` — preprocess one source file into a copy-friendly MP4
- `scripts/ersatztv_hls_copy_batch.py` — batch-convert a directory for copy-mode HLS playback

### references/

- `references/diagnosis-and-tuning.md` — deeper explanation of why copy-mode HLS fails on poor sources and what symptoms to expect
