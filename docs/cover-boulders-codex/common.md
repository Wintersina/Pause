You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this jj/git worktree; do not edit any .cs / .meta / existing file; do not delete or overwrite anything existing; do not commit. New files go ONLY to the output folder named below (generators, prompts and candidates in a `src~` subfolder of it; the trailing ~ matters: Unity ignores it).
Read ./docs/art-style.md (neon pixel art: detailed hand-placed-looking pixel art, 4-6 tone painterly ramps per material with texture, rim light, one electric hue per world on a dark base; NO red anywhere: red means friendly; hot pink only exists in Space) and ./docs/cover-boulders.md section 2 (what a COVER BOULDER is and how the game uses it).

WHAT YOU ARE DRAWING: a COVER BOULDER. A huge, indestructible object that drifts slowly down the lane; the player hides behind it from enemy fire. It must read at a glance as SOLID, HEAVY and OPAQUE (nothing can pass through it). Its silhouette matters most: it is seen from above-ish / side-on (the world is a 2D scroller), bigger than three enemy ships side by side. It is NOT a hazard: it must never look like a weapon, a mine, a hot-pink cue or a telegraph.

DELIVERABLES (exact, into <OUT>):
 1. boulder.png : 1536 x 512 RGBA, THREE variants side by side, one 512 x 512 cell each, transparent background, art centred in its cell (alpha bounding box 60-95% of the cell width, 45-95% of its height, at least 8 px clear margin all round). Three clearly different SILHOUETTES of the same family (see <VARIANTS>), each readable as a hiding place (a broad body with at least one big flat or round face, not a thin spike).
 2. break.png : 2560 x 1536 RGBA, the DESTRUCTION strip. Row r (0..2) = variant r, five 512 x 512 cells per row (frames 0..4), transparent background, same centring and scale as boulder.png:
      frame 0 = EXACTLY the intact variant (same silhouette, same pixels) with its first cracks lit (<CRACK_LIGHT>),
      frame 1 = deep cracks, the first chunks lifting off a few pixels,
      frame 2 = the BURST: it splits into 5-9 big chunks flying outward, <CRACK_LIGHT> blazing through the seams,
      frame 3 = the chunks smaller and more spread, shedding <DEBRIS>,
      frame 4 = the last drifting <DEBRIS> and dust, mostly transparent (8-35% of frame 0's area), fading.
    Every frame stays inside its 512 cell (2 px margin). Chunks are cut from the variant's own texture (same palette, same lighting), not generic grey rubble.
 3. Optional candidates, scripts, notes in <OUT>/src~/ .

LOOK:
 - Light comes from the UPPER LEFT on every variant and every frame (lit faces top-left, shade bottom-right), the same way for all three.
 - A thin PALE OUTLINE: the outermost 2 px of the silhouette is a continuous light edge (value >= .65, saturation <= .45, a pale tint of the world's light: <OUTLINE>), unbroken all round, so the boulder reads against any backdrop. Inside it the body is painted dark-to-mid with real material texture (<MATERIAL>); no flat fills, no posterised few-colour look, rich tonal ramps per material.
 - Hard pixel edges (alpha 0 or 255 almost everywhere, NO soft glow halo, NO blur), nearest-neighbour only when scaling.
 - Palette: <PALETTE>. Keep the whole thing at most as bright as an enemy ship's rim light: it is scenery, not a target. NO pink / magenta / hot-pink anywhere (hue 285-352) unless <WORLD> is Space. NO red, NO UI, no text, no drop shadow, no ground plane, no background.
METHOD (mandatory): use IMAGE GENERATION (the built-in image generation tool) for the painted content: generate several candidates per variant, inspect them, pick the best, then pixel-clean (nearest-neighbour only, no blur, keep rich ramps). Procedural-only results will be rejected. Compose cells programmatically into the final files.
PREVIEWS: never draw mock UI (boxes, buttons, labels); the verify script writes a contact sheet of the art itself on dark #0b0b1a.
VERIFY (mandatory): run `python3 scripts/verify_boulder_art.py <OUT> --world <WORLD> --manifest --sheet <OUT>/preview.png` and fix until every line PASSes (it also writes <OUT>/boulder.json, the hit-circle manifest: do not edit it). Finish with a short summary, the measured numbers and your honest caveats (what is weakest). Do not commit.
