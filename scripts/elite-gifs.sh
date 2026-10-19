#!/bin/bash
# elite-gifs.sh -- turn EliteCapture's frame folders into one GIF and one MP4 per elite.
#
#   scripts/elite-gifs.sh <frames-dir> [out-dir]
#
# <frames-dir> holds one folder per clip (NNNN.png frames at 15 fps, as `EliteCapture.CaptureSpace` writes them); the GIFs go to
# [out-dir] (default /Users/sina/Developer/Pause/.claude/elite-gifs/space) as <clip>.gif, plus <clip>.mp4 (libx264 crf 24, yuv420p,
# faststart, even dimensions: the GIF is 48 colours and a few MB, the MP4 the one to share). Needs ffmpeg (brew install ffmpeg).
# ELITE_MP4_ONLY=1 skips the GIFs (the Ember captures are MP4 only). Ember: elite-gifs.sh <frames-dir> /Users/sina/Developer/Pause/.claude/elite-gifs/ember
set -euo pipefail
src="${1:?usage: elite-gifs.sh <frames-dir> [out-dir]}"
out="${2:-/Users/sina/Developer/Pause/.claude/elite-gifs/space}"
command -v ffmpeg >/dev/null || { echo "ffmpeg not found: brew install ffmpeg" >&2; exit 1; }
mkdir -p "$out"
for d in "$src"/*/; do
  name=$(basename "$d")
  ls "$d"/0000.png >/dev/null 2>&1 || continue
  if [ -z "${ELITE_MP4_ONLY:-}" ]; then
  ffmpeg -y -loglevel error -framerate 15 -i "$d/%04d.png" \
    -vf "scale=540:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=48:stats_mode=diff[p];[b][p]paletteuse=dither=none:diff_mode=rectangle" \
    -loop 0 "$out/$name.gif"
  echo "$out/$name.gif ($(du -h "$out/$name.gif" | cut -f1))"
  fi
  ffmpeg -y -loglevel error -framerate 15 -i "$d/%04d.png" \
    -vf "crop=trunc(iw/2)*2:trunc(ih/2)*2" \
    -c:v libx264 -crf 24 -pix_fmt yuv420p -movflags +faststart "$out/$name.mp4"
  echo "$out/$name.mp4 ($(du -h "$out/$name.mp4" | cut -f1))"
done
