# ErsatzTV HLS Copy Mode Diagnosis and Tuning

## Core finding

When an ErsatzTV channel uses HLS Segmenter with `-c:v copy -c:a copy`, segment boundaries are constrained by the source file's existing keyframes/GOP structure.

That means:

- `-hls_time 4` is only a target, not a guarantee
- sparse or irregular keyframes produce long and uneven HLS segments
- IPTV clients are more likely to rewind, rebuffer, or appear to jump backward

## Typical symptoms

Use this skill when the user reports things like:

- playback runs for a few seconds and jumps back ~10 seconds
- only some channels are smooth even though channel settings look similar
- startup has one brief replay/restart, then playback becomes stable
- HLS playlists show uneven `#EXTINF` durations like 10s, 9.5s, 7s, 1s

## What to inspect first

1. Channel logs
   - confirm the channel is using HLS segmenter
   - confirm ffmpeg is using `-c:v copy -c:a copy`
2. Generated HLS playlist
   - inspect `live.m3u8`
   - stable copy-friendly sources should produce regular segment durations
3. Source file structure
   - codec, resolution, frame rate
   - keyframe spacing and regularity

## Strong indicators that the source is the problem

- source keyframes are 6-10+ seconds apart
- generated HLS has long uneven segments
- channels with denser keyframes are much smoother using the same profile
- the profile name implies transcoding, but logs still show `copy`

## Recommended preprocessing target for copy-mode HLS

Default target for most libraries:

- video: H.264
- audio: AAC
- resolution: preserve source resolution unless the user explicitly wants 1080p normalization
- frame rate: preserve source fps, but make it CFR
- GOP: 1 second (`-g` and `-keyint_min` matched to source fps)
- scene cut: disabled (`-sc_threshold 0`)
- B-frames: disabled (`-bf 0`)
- keyframes: forced every second
- pixel format: `yuv420p`
- SAR: `1:1`

Use 1080p normalization only when the user explicitly wants a uniform library shape or when device compatibility benefits from it.

## Practical operating advice

- Prefer preprocessing once over live transcoding if the goal is low server usage.
- Prefer source-resolution preprocessing first; it usually controls size growth better.
- Test one representative episode before batch-converting a whole series.
- Prefer ASCII-only temporary paths and names on Windows for manual experiments.
- Avoid direct database surgery unless necessary; the safer default is a separate library/folder and a dedicated test channel.

## What not to over-index on

Do not blame scheduling first when:

- channels use the same HLS segmenter mode
- the ffmpeg profile is still effectively `copy`
- only some sources are unstable

In that situation, the source file's GOP structure is usually the primary lever.
