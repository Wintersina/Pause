# Codex prompt: portal arrival art (run-entry animation)

Run in a worktree, `codex exec`, then open the PNGs for review before wiring.
Context: Pause is a portrait 2D space shooter, HD neon pixel art (see
`docs/art-style.md`, the north star is the Codex Space concept render:
indigo / magenta nebula, ringed city planet, rusted pipe rails). The existing
portal is `Pause/Assets/Art/Resources/Vfx/teleport_portal_atlas.png` (1254 x
1254, 4 x 4 cells, a spiralling vortex ring): match its pixel style, line
weight and palette exactly. The game tints the portal per world at runtime, so
draw it in near-white and cool grey-cyan, not in a world colour.

Task: make four PNG assets for a PORTAL ARRIVAL: at the start of a run a
portal opens where the player's ship will be, the ship flies out of it toward
the viewer, and the portal closes. Everything is RGBA with a transparent
background, hard pixel edges, no blur, no anti-aliased halo wider than 2 px,
vortex centred in every cell. Save into `Pause/Assets/Art/Resources/Vfx/`:

1. `portal_open_atlas.png`, 1280 x 1280, a 4 x 4 grid of 320 x 320 cells, 16
   frames read left to right, top to bottom. The portal OPENING: frame 0 a
   single point of light; frames 1 to 6 a ring tearing open and growing;
   frames 7 to 10 the vortex swirling at full size (180 px across, centred
   at (160, 160) in each cell; these four must loop seamlessly 7 -> 10 -> 7);
   frames 11 to 15 the same full-size vortex with a bright rim flare that fades out.
2. `portal_close_atlas.png`, 1280 x 960, a 4 x 3 grid of 320 x 320 cells, 12
   frames. The portal CLOSING: the full-size vortex (frame 0, identical to
   open frame 10) collapsing to a point by frame 8, then frames 9 to 11 a
   fading burst of 6 to 10 small sparks.
3. `portal_emerge_flash.png`, 1280 x 256, one row of 5 cells of 256 x 256: a
   near-white additive burst for the instant a ship crosses the vortex, a thin
   horizontal lens streak growing (cells 0 to 2) then fading (3 to 4).
4. `portal_wake_streaks.png`, 512 x 1024, tileable vertically (top row meets
   bottom row seamlessly): thin speed lines, soft cyan-white on transparent,
   denser toward the centre column, falling off toward both side edges.

Also write `portal_arrival_manifest.json` next to the PNGs with, per file: its
size, grid, frame count, the vortex centre and radius in pixels and which
frames loop. Verify each PNG with a script (sizes, alpha present, cell
centring within 2 px, loop frames 7 and 10 differing by less than a few pixels)
and put the checker in `Pause/Assets/Art/Resources/Vfx/src~/`. Do not touch any
other file, and do not run Unity.
