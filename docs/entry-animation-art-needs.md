# Run-entry animation: art that would improve it

Every run started from the menu now plays an ENTRY before the pilot has
control (`WorldEntry`): a planet's own planetfall, or, in Space (and for the
first real run after the tutorial), a PORTAL ARRIVAL. The planetfall uses the
existing planetfall art unchanged (nothing needed). The portal arrival is
built from the existing authored portal frames only
(`Resources/Vfx/teleport_portal_atlas.png`, 1254 x 1254, 4 x 4 cells) plus
procedural scale / spin / tint / a shock ring, and it works as it is. Nothing
below is required; it is what would make it look authored instead of
procedural.

## What plays today

| Start | Entry | Length |
|---|---|---|
| Space (menu PLAY, START WORLD = Space, dev pick Space, first run after the tutorial) | portal arrival: the gateway opens at the ship's start (the portal's own three frame layers, Space's portal colour), the ship comes out of it small and grows to full size onto the finger, a shock ring leaves, the portal spins shut (frames in reverse) | 2.6 s |
| Frost / Verdant / Ember / Tide | that planet's planetfall as the run's entry: starts in Space's sky, the planet is already on station, the ship commits and dives through plasma and two cloud decks, the world switches in under the clouds, break-through flash | 7.0 s of timeline at x1.25 (5.6 s) |
| REPLAY after a death | none, quick restart | 0 |

## Art that would improve the PORTAL ARRIVAL

All files go to `Pause/Assets/Art/Resources/Vfx/` (Resources, `Vfx/`), PNG,
RGBA, transparent background, pixel-art style of the existing portal atlas
(hard edges, no blur, a 4 to 6 colour palette per sheet, no anti-aliased halo
beyond 2 px), centred in every cell. Use the world's portal colours as a
neutral bright cyan-white / violet ramp: the game tints the sheet per world
(`WorldTheme.portalColor`), so draw it in near-white and cool grey, not in a
world colour.

1. `portal_open_atlas.png` : 1280 x 1280, 4 x 4 cells of 320 x 320, 16 frames
   read left to right, top to bottom. The portal OPENING at the spawn point:
   frame 0 a single point of light, frames 1 to 6 a ring tearing open and
   growing, frames 7 to 10 the vortex swirling at full size (this is the
   section that loops while the ship emerges), frames 11 to 15 the same
   vortex at full size with a bright rim flare fading out. The ring is
   180 px across at full size, centred at (160, 160) in each cell. Replaces the
   scaled-up first frame of `teleport_portal_atlas`.
2. `portal_close_atlas.png` : 1280 x 960, 4 x 3 cells of 320 x 320, 12
   frames. The portal CLOSING: the full vortex (frame 0) collapsing to a
   point (frame 8), then a 4-frame fading spark burst (9 to 11). Replaces the
   current "play the open frames backwards while scaling down".
3. `portal_emerge_flash.png` : 1280 x 256, one row of 5 cells of 256 x 256.
   A white-cyan additive burst for the instant the ship's nose crosses the
   vortex: a thin horizontal lens streak growing then fading (cell 0 to 4).
   Drawn near-white; additive blend.
4. `portal_wake_streaks.png` : 512 x 1024, tileable vertically, speed lines
   falling off the ship as it flies out (soft cyan-white on transparent).
   Currently the stars' scroll is boosted instead (`PortalArrivalTimeline.Boost`).

Wiring: `PortalArrival` would load these in `TeleportPortalSprites` (a second
loader, same slicing helper) instead of reusing the 16-frame atlas; the
timeline (`PortalArrivalTimeline`) and the rest do not change.

## Art that would improve the PLANET ENTRY

Nothing is missing: the planetfall art already covers it. One optional
improvement: the entry starts with the planet already on station above the
ship (`Planetfall.SpawnEntry`). A short "launch pad" start, the ship rising
from below the screen toward the planet for 1 s before the commit, would need
no art at all (code only) and is not drawn here.

## Optional: a reverse planetfall for a REPLAY

A replay is quick on purpose (see below). If a very short entry were wanted
for it too, a 1 s `Planetfall` variant (only the last 2 s of the descent) is
code only.

## Run-restart decision (documented)

- "Starts a level from the menu" (menu PLAY at any furthest world, the START
  WORLD column, the developer's world picker, the first run after the tutorial
  whether finished or skipped) always plays the entry.
- REPLAY after a death (`buttonClicks.replay` pins the run's world:
  `WorldManager.PinnedReplayWorld`) restarts at once: the pilot has just watched
  the entry; it would be 3 to 6 s of waiting on every retry.
- Death, then MENU, then PLAY is a menu start: it plays.
- The tutorial scene has no `WorldManager`, so it has no entry.
- No skip button / tap-skip: the planetfall flow has none and the brief said not
  to add UI. The planetfall entry runs at x1.25 (`Planetfall.EntryTimeScale`)
  and the portal arrival is 2.6 s.
- The entry runs on the world's clock like every transition: a lifted finger
  freezes it (the first touch of a run starts it, as the first touch always
  started a run).
- The 8 s calm start (`enmiesOnBoard.CalmArrivalSeconds`) starts at the
  hand-off: the window is held back while the entry plays.
