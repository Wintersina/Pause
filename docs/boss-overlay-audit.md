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
