#!/bin/bash
# Usage: docs/cover-boulders-codex/run_world.sh <World>   (run from the repo/workspace root; ONE codex exec at a time machine-wide)
set -e
W="$1"
D=docs/cover-boulders-codex
OUT="Pause/Assets/Art/Boulders~/$W"
mkdir -p "$OUT/src~"
while pgrep -f "bin/codex exec" >/dev/null; do sleep 20; done   # one codex exec at a time machine-wide
{ cat $D/common.md; echo; echo "---- WORLD SPECIFICS ----"; cat $D/worlds/$W.md; echo; echo "Resolution reminder: cell = 768 (python3 scripts/verify_boulder_art.py <OUT> --world $W --cell 768 --manifest --sheet <OUT>/preview.png)."; } > "$OUT/src~/prompt.md"
R=Pause/Assets/Art/Backgrounds/Resources/Worlds/$W/Backdrop3
cp "docs/cover-boulders-codex/refs/${W}_variants.jpg" "$OUT/src~/ref_variants.jpg"
REFS="-i $OUT/src~/ref_variants.jpg -i $R/landmarks.png -i $R/v1/mid.png"
[ -f "$OUT/src~/orig_boulder.png" ] && REFS="$REFS -i $OUT/src~/orig_boulder.png"
~/.local/bin/codex exec $REFS --skip-git-repo-check -C "$PWD" -s workspace-write -c model_reasoning_effort='"high"' -o "$OUT/src~/last.txt" - < "$OUT/src~/prompt.md" > "$OUT/src~/codex.log" 2>&1
