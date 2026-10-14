You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this jj/git worktree; do not edit any .cs / .meta / existing file; do not delete or overwrite anything existing; do not commit. New files go ONLY to the output folder named below (generators, prompts and candidates in a `src~` subfolder of it; the trailing ~ matters: Unity ignores it).
Read ./docs/art-style.md (palette discipline only: the colour rules below win) (neon pixel art: detailed hand-placed-looking pixel art, 4-6 tone painterly ramps per material with texture, rim light, one electric hue per world on a dark base; NO red anywhere: red means friendly; hot pink only exists in Space) and ./docs/cover-boulders.md section 2 (what a COVER BOULDER is and how the game uses it).

WHAT YOU ARE DRAWING: a COVER BOULDER. A huge, indestructible object that drifts slowly down the lane; the player hides behind it from enemy fire. It must read at a glance as SOLID, HEAVY and OPAQUE (nothing can pass through it). Its silhouette matters most: it is seen from above-ish / side-on (the world is a 2D scroller), bigger than three enemy ships side by side. It is NOT a hazard: it must never look like a weapon, a mine, a hot-pink cue or a telegraph.

DELIVERABLES (exact, into <OUT>):
 1. boulder.png : 2304 x 768 RGBA, THREE variants side by side, one 768 x 768 cell each (HIGH RES: the cell is 768, not the old 512), transparent background, art centred in its cell (alpha bounding box 60-95% of the cell width, 45-95% of its height including the fading base skirt, at least 12 px clear margin all round). Three clearly different SILHOUETTES of the same family (see <VARIANTS>), each readable as a hiding place (a broad body with at least one big flat or round face, not a thin spike).
 2. break.png : 3840 x 2304 RGBA, the DESTRUCTION strip. Row r (0..2) = variant r, five 768 x 768 cells per row (frames 0..4), transparent background, same centring and scale as boulder.png:
      frame 0 = EXACTLY the intact variant (same silhouette, same pixels) with its first cracks lit (<CRACK_LIGHT>),
      frame 1 = deep cracks, the first chunks lifting off a few pixels,
      frame 2 = the BURST: it splits into 5-9 big chunks flying outward, <CRACK_LIGHT> blazing through the seams,
      frame 3 = the chunks smaller and more spread, shedding <DEBRIS>,
      frame 4 = the last drifting <DEBRIS> and dust, mostly transparent (8-35% of frame 0's area), fading.
    Every frame stays inside its 768 cell (3 px margin). Chunks are cut from the variant's own texture (same palette, same lighting), not generic grey rubble.
 3. Optional candidates, scripts, notes in <OUT>/src~/ .

LOOK (revision 2, 2026-10-13: the boulder must BLEND INTO ITS WORLD'S BACKDROP, it is one of the backdrop's own masses drifting into the play field; the user rejected a bright, outlined, pasted-on look):
 - NO OUTLINE, NO RIM LIGHT HALO, NO pale border, no cut-out look: the silhouette is the art's own natural edge. The outer pixels are the same material and tone as the body next to them (a hair darker is fine).
 - SAME VIEW as the backdrop: look at the reference images (the world's backdrop layers, its four variants contact sheet, its landmarks atlas). The backdrop is an oblique, high 3/4 top-down view; paint the boulder in that same view and pixel language (same shading style, same grain, same ramp discipline), NOT a flat side-on cartoon.
 - SAME PALETTE AND VALUE RANGE as the backdrop: sample it. The boulder is as dark and as muted as the world's landmarks (never brighter or more saturated than the backdrop's brightest masses, atmospheric haze lowers contrast and tints it slightly toward the ground colour). Colour temperature of the backdrop. Light from the UPPER LEFT on every variant and every frame, shade bottom-right, the same way the backdrop is lit.
 - The BASE melts into the scene: the foot of the boulder is ringed by the world's own ground cover / debris (<BASE>) that thins out and fades to TRANSPARENT over about 60-90 px (alpha ramps down, dithered, never a hard cut), so it nestles into the backdrop with a fog / ground skirt instead of floating as a sticker. The solid body itself stays fully opaque (alpha 255). No cast shadow, no ground plane, no background.
 - Texture everywhere: real material detail at pixel level, rich ramps per material, no flat fills, no posterised look. HIGH RESOLUTION is a hard requirement: the final cell is 768 px and the painted art must be TRUE 768-cell resolution (about 640-690 px wide per variant). Generate each variant (and each break frame's content) at the image generator's largest size and only ever DOWNSCALE it into the cell (Lanczos); NEVER upscale, never block-upscale a smaller image (the verify script measures 2x/3x/4x block duplication and rejects it).
 - Palette: <PALETTE>. NO pink / magenta / hot-pink anywhere (hue 285-352) unless <WORLD> is Space. NO red, NO UI, no text.
 - Crack light (<CRACK_LIGHT>) is the only bright note: it is allowed to glow, but the intact body stays at backdrop brightness.
METHOD (mandatory): use IMAGE GENERATION (the built-in image generation tool) for the painted content: generate several candidates per variant, inspect them, pick the best, then pixel-clean (nearest-neighbour only, no blur, keep rich ramps). Procedural-only results will be rejected. Compose cells programmatically into the final files.
PREVIEWS: never draw mock UI (boxes, buttons, labels); the verify script writes a contact sheet of the art itself on dark #0b0b1a.
VERIFY (mandatory): run `python3 scripts/verify_boulder_art.py <OUT> --world <WORLD> --manifest --sheet <OUT>/preview.png` and fix until every line PASSes (it also writes <OUT>/boulder.json, the hit-circle manifest: do not edit it). Finish with a short summary, the measured numbers and your honest caveats (what is weakest). Do not commit.

---- WORLD SPECIFICS ----
<WORLD>=Frost
<OUT>=./Pause/Assets/Art/Boulders~/Frost/
<MATERIAL>=glacial blue ice, packed snow, dark bedrock streaks, rime, faceted planes, frozen waterfall veins, a few embedded steel girders from the icebreaker wrecks
<VARIANTS>=(a) a HUGE ICE MOUNTAIN: a broad-shouldered snow-capped crag with two stepped peaks and a flat-ish glacier foot, (b) a SHEARED ICEBERG BLOCK: a tall slab with one vertical glassy cliff face and snow on top, (c) a TWIN-SPIRE CRAG: two fused ice towers with a cave arch at the base. They look like a mountain torn out of the backdrop's ice ranges and set drifting; flat-bottomed, upright (never tilted)
<CRACK_LIGHT>=cold white-cyan light leaking from the cracks (#BFF3FF with a pale #EAFCFF core)
<DEBRIS>=ice shards, snow dust, frozen spray
<BASE>=ice floes, broken pack-ice chunks and snow drift
<PALETTE>=glacier blue-grey (hue 195-215, saturation <= .45, value .10-.80), snow white #EAF6FF, deep navy shadow #0E1A2E, bedrock slate (hue 220, saturation <= .2); the ONE bright note is the cold cyan glow of the cracks
<REFS>=(attached images) the four backdrop variants contact sheet (rows v1..v4; columns sky/far/mid/flow), the landmarks atlas, and v1 mid layer of the world

BACKDROP MATCH: the Frost backdrop is a dark navy-slate oblique view of pack ice (landmark median value ~.17, brightest snow ~.30, almost no saturation; the world's colour is dim blue-grey #2A3A4C .. #5A7088). The boulder keeps the mountain shapes the user loved (two-peak crag / sheared iceberg block / twin-spire crag with cave arch; a REVISION of the attached current boulder.png) but is repainted dark and muted like the landmarks, sitting on a skirt of ice floes like the landmarks do.

Resolution reminder: cell = 768 (python3 scripts/verify_boulder_art.py <OUT> --world Frost --cell 768 --manifest --sheet <OUT>/preview.png).
