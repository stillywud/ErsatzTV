#!/usr/bin/env python3
import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

VIDEO_EXTS = {'.mp4', '.mkv', '.avi', '.mov', '.m4v', '.ts', '.m2ts', '.wmv'}


def run(cmd, check=True, capture=True):
    p = subprocess.run(cmd, text=True, capture_output=capture)
    if check and p.returncode != 0:
        raise RuntimeError(p.stderr.strip() or p.stdout.strip() or f"command failed: {' '.join(cmd)}")
    return p


def parse_rate(rate: str):
    if not rate or rate == '0/0':
        return None
    if '/' in rate:
        n, d = rate.split('/', 1)
        n = float(n)
        d = float(d)
        if d == 0:
            return None
        return n / d
    return float(rate)


def probe_media(ffprobe: str, path: Path):
    p = run([
        ffprobe,
        '-v', 'error',
        '-show_entries', 'stream=index,codec_name,codec_type,avg_frame_rate,r_frame_rate,width,height,sample_aspect_ratio,display_aspect_ratio:format=duration',
        '-of', 'json',
        str(path),
    ])
    info = json.loads(p.stdout)
    streams = info.get('streams', [])
    video = next((s for s in streams if s.get('codec_type') == 'video'), None)
    audio = next((s for s in streams if s.get('codec_type') == 'audio'), None)
    if not video:
        raise RuntimeError('No video stream found')
    if not audio:
        raise RuntimeError('No audio stream found')
    fps = parse_rate(video.get('avg_frame_rate')) or parse_rate(video.get('r_frame_rate')) or 25.0
    return {'video': video, 'audio': audio, 'fps': fps, 'duration': info.get('format', {}).get('duration')}


def choose_profile(profile: str):
    if profile == 'quality':
        return {'preset': 'medium', 'crf': '18', 'audio_bitrate': '192k'}
    if profile == 'smaller':
        return {'preset': 'medium', 'crf': '21', 'audio_bitrate': '128k'}
    return {'preset': 'medium', 'crf': '20', 'audio_bitrate': '160k'}


def build_vf(resolution_mode: str, fps: float):
    fps_str = f"{fps:.6f}".rstrip('0').rstrip('.')
    if resolution_mode == '1080p':
        return f"scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2,setsar=1,fps={fps_str}"
    return f"scale=trunc(iw/2)*2:trunc(ih/2)*2,setsar=1,fps={fps_str}"


def discover_files(input_dir: Path, recursive: bool):
    it = input_dir.rglob('*') if recursive else input_dir.glob('*')
    return sorted([p for p in it if p.is_file() and p.suffix.lower() in VIDEO_EXTS])


def default_output_dir(input_dir: Path):
    return input_dir.parent / f"{input_dir.name}-hls-copy"


def convert_one(ffmpeg: str, ffprobe: str, src: Path, dst: Path, resolution_mode: str, keep_source_fps: bool, target_fps, profile_name: str, overwrite: bool, dry_run: bool):
    meta = probe_media(ffprobe, src)
    fps = target_fps if target_fps is not None else meta['fps'] if keep_source_fps else (25.0 if meta['fps'] >= 50 else 30.0 if meta['fps'] > 30 else meta['fps'])
    gop = max(1, round(fps))
    profile = choose_profile(profile_name)
    vf = build_vf(resolution_mode, fps)
    dst.parent.mkdir(parents=True, exist_ok=True)
    cmd = [
        ffmpeg, '-y' if overwrite else '-n', '-i', str(src), '-map', '0:v:0', '-map', '0:a:0',
        '-vf', vf, '-c:v', 'libx264', '-preset', profile['preset'], '-crf', profile['crf'],
        '-pix_fmt', 'yuv420p', '-profile:v', 'high', '-level', '4.1', '-g', str(gop), '-keyint_min', str(gop),
        '-sc_threshold', '0', '-bf', '0', '-force_key_frames', 'expr:gte(t,n_forced*1)',
        '-c:a', 'aac', '-b:a', profile['audio_bitrate'], '-ar', '48000', '-ac', '2', '-movflags', '+faststart', str(dst)
    ]
    plan = {
        'src': str(src), 'dst': str(dst), 'source_width': meta['video'].get('width'), 'source_height': meta['video'].get('height'),
        'source_fps': round(meta['fps'], 6), 'target_fps': round(fps, 6), 'profile': profile_name,
        'resolution_mode': resolution_mode, 'gop_frames': gop, 'crf': profile['crf'], 'audio_bitrate': profile['audio_bitrate']
    }
    if dry_run:
        return plan
    print(f"\n=== Converting: {src}\n -> {dst}\n    source={meta['video'].get('width')}x{meta['video'].get('height')} @ {meta['fps']:.6f} fps | target_fps={fps:.6f} | gop={gop}")
    p = subprocess.run(cmd)
    if p.returncode != 0:
        raise RuntimeError(f"ffmpeg failed with exit code {p.returncode}")
    return plan


def main():
    ap = argparse.ArgumentParser(description='Batch preprocess a directory for ErsatzTV copy-mode HLS playback.')
    ap.add_argument('input_dir')
    ap.add_argument('--output-dir')
    ap.add_argument('--ffmpeg', required=True)
    ap.add_argument('--ffprobe', required=True)
    ap.add_argument('--profile', choices=['balanced', 'smaller', 'quality'], default='balanced')
    ap.add_argument('--resolution-mode', choices=['source', '1080p'], default='source')
    ap.add_argument('--keep-source-fps', action='store_true', default=True)
    ap.add_argument('--target-fps', type=float, default=None)
    ap.add_argument('--recursive', action='store_true', default=False)
    ap.add_argument('--overwrite', action='store_true', default=False)
    ap.add_argument('--dry-run', action='store_true', default=False)
    args = ap.parse_args()

    input_dir = Path(args.input_dir)
    if not input_dir.is_dir():
        print(f"input_dir not found: {input_dir}", file=sys.stderr)
        return 2
    output_dir = Path(args.output_dir) if args.output_dir else default_output_dir(input_dir)
    files = discover_files(input_dir, args.recursive)
    if not files:
        print('No video files found.')
        return 1

    print(f"Input dir:   {input_dir}")
    print(f"Output dir:  {output_dir}")
    print(f"Files:       {len(files)}")
    print(f"Profile:     {args.profile}")
    print(f"Resolution:  {args.resolution_mode}")
    print(f"Keep FPS:    {args.keep_source_fps}")
    if args.target_fps is not None:
        print(f"Forced FPS:  {args.target_fps}")

    ok = 0
    failed = []
    plans = []
    for src in files:
        rel = src.relative_to(input_dir)
        dst = output_dir / rel.with_suffix('.mp4')
        if dst.exists() and not args.overwrite:
            print(f"Skipping existing: {dst}")
            continue
        try:
            plan = convert_one(args.ffmpeg, args.ffprobe, src, dst, args.resolution_mode, args.keep_source_fps, args.target_fps, args.profile, args.overwrite, args.dry_run)
            plans.append(plan)
            ok += 1
        except Exception as e:
            failed.append((str(src), str(e)))
            print(f"FAILED: {src}\n  {e}", file=sys.stderr)

    summary = {'input_dir': str(input_dir), 'output_dir': str(output_dir), 'processed': ok, 'failed': len(failed), 'dry_run': args.dry_run, 'failures': failed}
    print('\n=== Summary ===')
    print(json.dumps(summary, indent=2, ensure_ascii=False))
    if args.dry_run:
        plan_file = output_dir.parent / f"{input_dir.name}-hls-copy-plan.json"
        plan_file.write_text(json.dumps(plans, indent=2, ensure_ascii=False), encoding='utf-8')
        print(f"Plan saved to: {plan_file}")
    return 0 if not failed else 3


if __name__ == '__main__':
    sys.exit(main())
