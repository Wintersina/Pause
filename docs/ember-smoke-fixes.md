# Ember smoke: diagnosis and art fix list

Report: "smoke from the volcano not lined up right, smoke cut off at the top, some frames cut off at the top".

## Root causes

1. CODE (fixed): draw order. Loops took `plate order + 1` of their OWN pool. Pools sort pipes -4, fires -2, ground/sites 0, so every plume of a fire piece (lavafountain, eruptionscar, coalbed, scorched = the eruption columns) and of a pipe sat BELOW the plates of the ground pool (landmarks). Where a plume rose behind a tower/refinery plate higher up the screen, the plate sliced it off flat along its edge. Measured over 45 s of flight: Ember 247 of 512 sampled loops hidden (48%), Verdant 51/336, Frost (site plates +3 over ground loops +1) 168/338. Fix: every pool's loops now draw above every ground plate (Ember/Verdant/Tide pipes +5, fires +3; Frost ground loops +4). Same-world screenshots: scratchpad `preview/ember_smoke/before_after_v4_40.png`.
2. ART (Codex): a detached hatched flat strip hovering above the top of many Ember loop frames (the leftover top row of the crop; reads as smoke "cut off" with its cap floating free).
3. ART (Codex): `weather.png` `smokepall_00` (the ash pall hung over every eruption) is flat-cut along its top (row 52, 92 px wide, 56/255 alpha); `ashgust_01` top-left corner cut diagonally.
4. ART (Codex): Verdant `smoke_a` and `wildsmoke_b` are painted into the right edge of the 256 cell (x=254), so the sprite rect cuts them vertically.
5. ART, the "not lined up": Ember `smoke_a` swings its whole body from 64 cell px left of the foot to 45 px right between frames (smoke_b -42..+30, eruptsmoke_a -25..+15); foot flames of `smoke_a` shift 15 px and frames 02/06 have twin feet. Not clipping, a lean that flips each loop. Emitter points themselves are fine (see below).

## What is NOT wrong (measured)

- No Ember loop frame (smoke, eruption, lavafire, leaks, lights) has alpha in the top 2 rows or sides. Min top margin 42 px (smoke_a), min side margin 6 px (smoke_a_01 left, eruptsmoke_a_03 right). Frost: none either.
- Loop sprites are `FullRect`, the Backdrop shader feather only applies to plates, no clip rect touches loops, no UV clamp issue.
- Emitter placement (rendered diff of the frame with/without each plume, 8 frames, both mirrors, size 1.7): plume base within -11..+15 px horizontally of the host point (steam_vent 0.5 px, eruptsmoke_a -7..+10 px = the art's stem wobble of up to 9 cell px), mirrored identical. points.json crater points sit on art (EmberBackdropTest guards it). There are no emitters on the ground-tile volcanoes (v3 cones, v4 caldera); plumes only exist on pieces.

## Violations table (guard: `BackdropLoopGuardTest`, prints `[LOOPGUARD]` lines)

| World | Atlas | Frames | Problem |
|---|---|---|---|
| Ember | eruption | eruptsmoke_b_00..07 | flat sliver 56-68 x 6-8 px at y 64 (04: y 107), floating above plume |
| Ember | lavafire | fountain_00..07 | sliver ~51 x 4 at y 82-85 |
| Ember | leaks | ember_rain_00..03 | sliver ~33 x 5 at y 93-94 |
| Ember | leaks | lava_bubble_00..03 | sliver ~27 x 6 at y 130-131 |
| Ember | lights | strobe_white_01..03 | sliver ~40 x 3-6 at y 163-165 |
| Ember | weather | smokepall_00 | flat top edge row 52, 92 px |
| Ember | weather | ashgust_01 | top-left corner cut |
| Verdant | smoke | smoke_a_00..07 | art to x=254, right margin 1 (up to 40 px of column at the edge) |
| Verdant | firesmoke | wildsmoke_b_00..07 | same, right margin 1-2 (up to 36 px) |
| Frost, Tide | all | none (Tide loop atlases not delivered yet; guard skips them) |

Foot drift (bottom 8 rows of the main blob, cell px): Ember smoke_a 122-137, smoke_b 121-135, eruptsmoke_a 126-132, fountain 121-124, others <= 1; Verdant wildsmoke_a 124-136, flame_front 123-178, flame_patch 115-134; Frost steam_vent 117-129. Target: <= 2 px.

## Codex fix list (margin rule: >= 6 px clear at top and sides, plume foot anchored at (128, 236), foot drift <= 2 px)

Do not repaint the plumes; edit the existing frames.
- Delete the detached strip in every frame listed above (any connected blob that is >= 24 px wide, <= 12 px tall and not touching the main plume). Keep everything else pixel-identical.
- `weather.png` smokepall_00: soften the flat top, feather the top 14 rows of the cloud into round puffs (alpha stays <= 56); ashgust_01: round off the top-left corner.
- Verdant `smoke.png` smoke_a_00..07 and `firesmoke.png` wildsmoke_b_00..07: shrink each frame 85-90% about the foot (128, 234) or shift left so the rightmost opaque column is <= 249; re-run `measure_points.py`.
- Ember `smoke.png` smoke_a: keep the foot flame on x=128.6 in every frame (frames 02/06 single foot) and limit the body lean to roughly +-25 px at the top of the column so it leans the same side (right, as the wind) in all 8 frames; same for smoke_b (+-20) and eruptsmoke_a.
- After any art change: rerun `Assets/Art/Worlds/Ember/backdrop_v3~/src~/measure_points.py` (and the Verdant equivalent) and `BackdropLoopGuardTest`; shrink `KnownEdge` / `KnownSliver` in that test to match.
