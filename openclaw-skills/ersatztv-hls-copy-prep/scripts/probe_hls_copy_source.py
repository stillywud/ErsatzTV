#!/usr/bin/env python3
import argparse
import json
import shutil
import statistics
import subprocess
import sys
from pathlib import Path


def run(cmd):
    p = subprocess.run(cmd, capture_output=True, text=True)
    if p.returncode != 0:
        raise RuntimeError(p.stderr.strip() or f"command failed: {' '.join(cmd)}")
    return p.stdout


def ffprobe_json(ffprobe, path):
    out = run([
        ffprobe,
        '-v', 'error',
        '-show_entries', 'format=duration:stream=index,codec_name,codec_type,avg_frame_rate,r_frame_rate,profile,width,height,pix_fmt,sample_aspect_ratio,display_aspect_ratio,channels,sample_rate',
        '-of', 'json',
        str(path)
    ])
    return json.loads(out)


def keyframe_times(ffprobe, path, limit=None):
    out = run([
        ffprobe,
        '-v', 'error',
        '-select_streams', 'v:0',
        '-skip_frame', 'nokey',
        '-show_entries', 'frame=best_effort_timestamp_time,key_frame,pict_type',
        '-of', 'csv=p=0',
        str(path)
    ])
    times = []
    for line in out.splitlines():
        parts = [p.strip() for p in line.split(',') if p.strip()]
        if len(parts) >= 2:
            try:
                times.append(float(parts[1]))
            except ValueError:
                pass
        if limit and len(times) >= limit:
            break
    return times


def summarize_gaps(times):
    if len(times) < 2:
        return None
    gaps = [round(times[i + 1] - times[i], 3) for i in range(len(times) - 1)]
    return {
        'count': len(gaps),
        'min': min(gaps),
        'max': max(gaps),
        'mean': round(statistics.mean(gaps), 3),
        'median': round(statistics.median(gaps), 3),
        'sample_first_20': gaps[:20],
    }


def recommendation(gaps):
    if gaps is None:
        return 'Not enough keyframes detected to judge copy-mode HLS suitability.'
    if gaps['max'] >= 8 or gaps['mean'] >= 5.5:
        return 'Likely poor fit for copy-mode HLS. Preprocess to fixed GOP / denser keyframes.'
    if gaps['max'] <= 4.5 and gaps['mean'] <= 4.2:
        return 'Good fit for copy-mode HLS. Source keyframes are already regular.'
    return 'Borderline. Test generated live.m3u8 segment durations before trusting copy mode.'


def main():
    ap = argparse.ArgumentParser(description='Probe whether a source file is suitable for ErsatzTV copy-mode HLS.')
    ap.add_argument('input', help='Video file to inspect')
    ap.add_argument('--ffprobe', default=None, help='Path to ffprobe executable')
    ap.add_argument('--limit-keyframes', type=int, default=120, help='Maximum keyframes to sample')
    args = ap.parse_args()

    ffprobe = args.ffprobe or shutil.which('ffprobe')
    if not ffprobe:
        print('ffprobe not found; pass --ffprobe <path>', file=sys.stderr)
        sys.exit(2)

    path = Path(args.input)
    if not path.exists():
        print(f'input not found: {path}', file=sys.stderr)
        sys.exit(2)

    meta = ffprobe_json(ffprobe, path)
    times = keyframe_times(ffprobe, path, args.limit_keyframes)
    gaps = summarize_gaps(times)

    result = {
        'file': str(path),
        'streams': meta.get('streams', []),
        'duration': meta.get('format', {}).get('duration'),
        'keyframe_sample_count': len(times),
        'keyframe_gap_summary': gaps,
        'recommendation': recommendation(gaps),
    }
    print(json.dumps(result, indent=2, ensure_ascii=False))


if __name__ == '__main__':
    main()
