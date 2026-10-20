#!/bin/bash
# usage: gen_cells.sh <key> "<palette brief>" [cells...]  -- needs src/<key>_hero.png (approved cell-0 repaint, transparent)
# For each cell i: Codex repaints original cell i (src/<key>_c<i>_in.png, 4x) in the hero's exact palette/design -> src/<key>_c<i>_new.png
D="$(cd "$(dirname "$0")" && pwd)"; K="$1"; BRIEF="$2"; shift 2; CELLS="${@:-1 2 3 4 5 6}"
run() { i=$1
PROMPT="Two attached images. Image 1 (${K}_c${i}_in.png) is ONE frame of a game enemy flipbook: its pose, camera, silhouette, effects and framing. Image 2 (${K}_hero.png) is the approved NEW colour design of the same enemy. Use your image generation tool to repaint image 1's frame in the exact new design of image 2: identical palette, materials, part colours and finish as image 2 (${BRIEF}) while keeping image 1's pose, part positions, silhouette, framing, size and any attack/hit/damage effects exactly, recoloured into the same new palette family (effects stay cold ice-white/cyan, small amber where image 2 has amber). Same crisp pixel-art style. Transparent square background, same centring and scale as image 1. NO outline of any kind (no pale rim, white border, glow halo, dark ring). FORBIDDEN: pink, magenta, red, text, shadow, background. ${EXTRA} One image only. Save the RGBA PNG to ${D}/src/${K}_c${i}_new.png (copy from the generated images folder if needed) and reply with just the path."
printf "%s" "$PROMPT" | ~/.local/bin/codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C "$D/src" -i "$D/src/${K}_c${i}_in.png" -i "$D/src/${K}_hero.png" - > "$D/src/${K}_c${i}.log" 2>&1
}
for i in $CELLS; do run $i & while [ $(jobs -r | wc -l) -ge 3 ]; do sleep 5; done; done; wait
