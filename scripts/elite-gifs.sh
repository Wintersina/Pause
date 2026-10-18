#!/bin/bash
# elite-gifs.sh -- turn EliteCapture's frame folders into one GIF per elite.
#
#   scripts/elite-gifs.sh <frames-dir> [out-dir]
#
# <frames-dir> holds one folder per clip (NNNN.png frames at 15 fps, as `EliteCapture.CaptureSpace` writes them); the GIFs go to
# [out-dir] (default /Users/sina/Developer/Pause/.claude/elite-gifs/space) as <clip>.gif. Needs ffmpeg (brew install ffmpeg).
set -euo pipefail
src="${1:?usage: elite-gifs.sh <frames-dir> [out-dir]}"
out="${2:-/Users/sina/Developer/Pause/.claude/elite-gifs/space}"
command -v ffmpeg >/dev/null || { echo "ffmpeg not found: brew install ffmpeg" >&2; exit 1; }
mkdir -p "$out"
for d in "$src"/*/; do
  name=$(basename "$d")
  ls "$d"/0000.png >/dev/null 2>&1 || continue
  ffmpeg -y -loglevel error -framerate 15 -i "$d/%04d.png" \
    -vf "scale=540:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=48:stats_mode=diff[p];[b][p]paletteuse=dither=none:diff_mode=rectangle" \
    -loop 0 "$out/$name.gif"
  echo "$out/$name.gif ($(du -h "$out/$name.gif" | cut -f1))"
done
