#!/bin/zsh
# usage: run.sh v2 a
export PATH=$HOME/.local/bin:$PATH
W=/Users/sina/Developer/Pause-worktrees/frost-896
D=$W/Pause/Assets/Art/Worlds/Frost/backdrop_v3/src~/896
v=$1; p=$2
case $v in
v2) DESC="the HARBOR: a frozen shipyard seen from above: dry docks, quays and jetties, moored hulks, cranes, warehouses and tanks on broken pack ice, dark open water leads between the floes";;
v3) DESC="the REFINERY: a snowfield industrial plant seen from above: pipework runs, round tanks, cooling towers, rail lines and gantries, frozen ponds, snow-dusted rocky ridges and fir-like spires";;
v1) DESC="the PACK ICE FIELD: a vast field of broken arctic pack ice seen from above: big snow-topped ice floes with layered faceted ice cliff edges, small floes, bright blue meltwater leads and glowing cracks, tiny oil-rig derricks and walkways here and there";;
v4) DESC="the ICE CANYON: a labyrinth of tall blocky glacier ice plateaus and sheer layered ice cliffs seen from above, glowing blue crevasses and frozen waterfalls between the blocks, a few small steel catwalks and platforms";;
esac
cat > $D/${v}_${p}.md <<EOF
You are making ONE game-art ground painting for "Pause", a vertical phone-portrait 2D shooter, FROST world (an arctic ice planet, orthographic top-down aerial view): $DESC.

Reference image 1 is the previous version of this exact ground (shown here already brightened). REPAINT IT at much higher native resolution with RICHER pixel-level detail: KEEP THE SAME COMPOSITION (every building, pipe, quay, floe, pond and ridge stays in the same place and shape, running off every edge exactly as in the reference: no vignette, no border), but add real detail: readable ice facets, snow drifts, cracks, rivets, panel seams, small fine props.

BRIGHTNESS AND PALETTE: paint it BRIGHT, CLEAR and CLEAN, lighter than the reference, with NO black and NO near-black anywhere (the darkest pixel is a saturated mid blue such as #1F4E9C, never navy-black or grey-black): snow and ice in cool whites and pale blues (frost white, ice blue, steel blue, slate), metal in steel grey-blue with only small touches of desaturated brass, water and crevasses in vivid saturated blue (#2C74D6 to #1F4E9C, never black). Average value about 0.55, brightest snow never above 0.92. Cool blue-white family only. No dark fog, no shadows pooled in corners, no dark overlay, no vignette, no black voids, no dithered checkerboard or screen-door grid texture.

STYLE: crisp hi-res PIXEL ART: hard-edged pixel clusters on a consistent small pixel grid (art pixels of about 2 by 2 output pixels), limited smooth-free colour ramps, NO anti-aliased blur, NO soft airbrushed gradients, NO photographic blur, NO outlines or black borders around objects. ABSOLUTELY NO pink, magenta or purple. The central 40 percent of the width stays calm and lower in contrast (a clear lane): the busiest detail and biggest shapes are in the outer 30 percent on each side.

HARD NO: horizon, sky, ships flying, UI, text, frame, watermark, people, animals. Do not edit any other file, do not commit.
Paint ONE opaque SQUARE image at the largest size the tool allows (about 1254x1254) and save it to the exact output path at the end.
Output: write the final PNG to $D/${v}_${p}.png
EOF
$W/scripts/codex-art.sh $D $D/${v}_${p}.md $D/${v}_${p}.last $D/${v}_${p}_ref.png > $D/${v}_${p}.log 2>&1
