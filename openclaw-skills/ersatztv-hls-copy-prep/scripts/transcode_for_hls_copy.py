#!/usr/bin/env python3
import argparse
import shutil
import subprocess
import sys
from pathlib import Path


def main():
    ap = argparse.ArgumentParser(description='One-time preprocess for ErsatzTV copy-mode HLS.')
    ap.add_argument('input', help='Source video file')
    ap.add_argument('output', help='Output MP4 path')
    ap.add_argument('--ffmpeg', default=None, help='Path to ffmpeg executable')
    ap.add_argument('--width', type=int, default=1920)
    ap.add_argument('--height', type=int, default=1080)
    ap.add_argument('--fps', type=int, default=25)
    ap.add_argument('--crf', type=int, default=19)
    ap.add_argument('--preset', default='fast')
    ap.add_argument('--audio-bitrate', default='160k')
    args = ap.parse_args()

    ffmpeg = args.ffmpeg or shutil.which('ffmpeg')
    if not ffmpeg:
        print('ffmpeg not found; pass --ffmpeg <path>', file=sys.stderr)
        sys.exit(2)

    src = Path(args.input)
    dst = Path(args.output)
    if not src.exists():
        print(f'input not found: {src}', file=sys.stderr)
        sys.exit(2)
    dst.parent.mkdir(parents=True, exist_ok=True)

    gop = args.fps  # 1-second GOP
    vf = (
        f'scale={args.width}:{args.height}:force_original_aspect_ratio=decrease,'
        f'pad={args.width}:{args.height}:(ow-iw)/2:(oh-ih)/2,'
        f'setsar=1,fps={args.fps}'
    )

    cmd = [
        ffmpeg,
        '-y',
        '-i', str(src),
        '-map', '0:v:0',
        '-map', '0:a:0',
        '-vf', vf,
        '-c:v', 'libx264',
        '-preset', args.preset,
        '-crf', str(args.crf),
        '-pix_fmt', 'yuv420p',
        '-profile:v', 'high',
        '-level', '4.1',
        '-g', str(gop),
        '-keyint_min', str(gop),
        '-sc_threshold', '0',
        '-bf', '0',
        '-force_key_frames', f'expr:gte(t,n_forced*1)',
        '-c:a', 'aac',
        '-b:a', args.audio_bitrate,
        '-ar', '48000',
        '-ac', '2',
        '-movflags', '+faststart',
        str(dst),
    ]

    print('Running:')
    print(' '.join(cmd))
    rc = subprocess.call(cmd)
    sys.exit(rc)


if __name__ == '__main__':
    main()
