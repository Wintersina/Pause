# Boss overlay audit (low health)

User report: at 2 hearts the Frost boss (and the Verdant boss) shows "the frame overlay" again.

## What was checked

`BossDamageTest.EveryBossOneDrawing` steps a live `BossActor` for every boss (Space, Frost, Verdant, Ember, Tide,
Storm, Desert) at every heart count from 12 down to 1, in these states: entrance, idle, every tell and fire drawing
(the PR #36 per-wave cadence on a manual beat clock, plus every attack of the rotation), hit flash, death handoff and
a damaged retreat. Each step is a renderer census plus a pixel check:

- exactly one enabled `Body` renderer; the only other boss renderers are the hit-flash silhouette (same sprite as the
  body) and the smoke / arcs overlays (from the damage fx sheet);
- the body sprite is the drawing for its (pose, damage stage): the damaged hull while idle, the damaged attack
  drawing while telling / firing (never a pristine pose or hull while damaged);
- pixel level: no damaged attack drawing carries idle-hull pixels over parts its pose moved, and its silhouette is its
  pose's silhouette (0 ghost px).

Result: 105 of 105 rows pass, on all layouts (20-cell Frost / Verdant / Storm / Desert, 35-cell Space / Tide, the
Drake's 576 px cells). Frost at 2 hearts is damage stage 4 (12 hearts: stage 4 from 10 hearts lost).
The test is sensitive: with the damaged-attack-pose lookup removed from `BossActor.SetFrame` every boss fails it.
Table: `docs/boss-overlay-audit-table.md` (rows: boss x hearts left x states seen x max body drawings x max overlays
x ghost px x stale poses).

## What it means

The runtime never draws two boss hulls and the damaged attack drawings are single, correctly registered drawings, so
this report is not a regression of the PR #22 mechanism. What is visible at the lowest stages and is baked into the
art (not touched here):

- `<Key>_damage_fx.png` smoke row (full strength at stage 4): Frost's left plume is two drawings with a hard
  rectangular seam; Verdant's cells carry a faint ghost smudge along the bottom edge; Storm's and Desert's smoke is
  clipped to flat-edged boxes; Frost / Verdant arcs have boxy fragments. These read as a second frame laid over the hull.
- The stage 4 tell / fire drawings are a blend of the pose and the wrecked idle hull (soft double contours on the
  wings and towers), so idle -> tell swaps between two noticeably different looks.

Both need regenerated art (Codex), not code.

## Art fix (branch art/boss-low-health-fx)

The runtime was already one drawing, so the fix is art only (no resize, same grids, pivots and import settings):

- `<Key>_damage_fx.png` for Frost, Verdant, Storm, Desert (cells re-composited deterministically, tools in
  `Pause/Assets/Art/Bosses~/low-health-fx/tools`):
  - Frost smoke: the left plume (two drawings joined by a rectangular seam) is rebuilt from the clean right plume
    mirrored (frames offset so the two plumes do not move in lock step); arcs lose the cut-off boxy slivers.
  - Verdant smoke: cells 2-4 were 50 % cross-fades of two frames and cell 1 / 5 carried a flat faint ghost layer, the left
    plume was clipped by a diagonal / vertical cut. Rebuilt from the two clean drawings (right plume mirrored to the left,
    ghost layer removed, small drifts between frames); the straight boxy arc polylines are redrawn as jagged sap-lightning.
  - Storm / Desert smoke: flat bottoms / boxes are dissolved out (position-stable dither) so the plume fades inside the
    cell; Desert's clipped right plume is rebuilt from the left one; nothing within 12 px of the cell border any more.
  - Space / Tide / Ember fx cells were audited and have none of these defects (left untouched).
- `Resources/BossImpact/attack/<key>_<pose>_stage4.png` and `_stage3.png` (Frost all, Verdant 3 of 7 so far) for Frost, Verdant, Storm,
  Desert (stage 4 done for all 28 poses): regenerated with Codex from the clean pose plus the stage idle hull as references, registered back onto the pose
  cell (silhouette overlap 81-97 %), so idle -> tell changes the pose, not the damage look; Desert keeps its pale rim.
- Tests: `BossDamageTest.DamageFxCellsAreClean` (border margin, straight-edge run, ghost haze, clipped slivers),
  and the attack-pose check now measures staying inside the pose's silhouette, no verbatim copy of the wrong-pose idle
  hull, no translucent second layer and pose overlap, instead of requiring a pixel-identical silhouette.
