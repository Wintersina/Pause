You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this jj/git worktree; do not edit any .cs / .meta / existing file; do not delete or overwrite anything existing; do not commit. New files go ONLY to the output folder named below (generators, candidates and notes in a `src~` subfolder of it; the trailing ~ matters: Unity ignores it).
Read ./docs/art-style.md (neon pixel art: detailed hand-placed-looking pixel art, 4-6 tone painterly ramps with texture, rim light, one electric hue per world on a dark base; NO red anywhere: red means friendly; hot pink only exists in Space) and ./docs/world-weather-hazards.md sections 3-5 and 12 (what a weather hazard is and how the game uses it).

WHAT YOU ARE DRAWING: the visible BODY of two WORLD HAZARDS and the AMBIENT WEATHER PARTICLES of one world. A hazard is a rare timed weather event that SHOVES or PULLS ships and bullets (it never damages): a tornado, a sand wall, a whirlpool. It must read at a glance as WEATHER (moving air / water / dust), clearly NOT a weapon, a mine, an enemy or an attack telegraph: no hot cores, no beams, no cannon-like shapes. It is drawn behind the ships and bullets, so it must stay SEMI-TRANSPARENT in its middle (ships and bullets stay readable through it: keep the mean alpha of the body at most about 55%), airy at the centre, denser and more detailed towards the rims. Its brightest values stay below an enemy ship's rim light.

DELIVERABLES (exact, into <OUT>):
 1. hazard.png : 3072 x 1536 RGBA, 6 columns x 3 rows of 512 x 512 cells, transparent background.
      row 0 = hazard A, an animated LOOP of 6 frames (the loop must close: frame 5 flows into frame 0),
      row 1 = hazard B, a LOOP of 6 frames,
      row 2 cells 0-1 = hazard A WARNING (2 frames), cells 2-3 = hazard B WARNING (2 frames); cells 4-5 stay EMPTY.
    The WARNING is the telegraph shown for ~2 s before the hazard arrives: a faint, thin, airy preview of the same weather (a few streaks, a spiral of specks, a ring of foam), about 25-40% of the body's density, same colours, so the player learns "that thing is coming". It is NOT a box, an arrow, a label, an outline frame or any UI.
    Frame content is described per hazard in <HAZARDS>. Art is centred in its cell with at least 6 px clear margin (a FRONT hazard excepted, see below).
 2. ambient.png : 2048 x 512 RGBA, 8 columns x 2 rows of 256 x 256 cells. Four particle KINDS (see <AMBIENT>), four variants each, row-major: cells 0-3 = kind 0, 4-7 = kind 1, 8-11 = kind 2, 12-15 = kind 3. Each particle is drawn small and centred (about 20-110 px of the 256 cell: the game scales it down to a few pixels / tenths of a unit), crisp, a pale rim on the lit edge, never glowing.
 3. Optional candidates, scripts, notes in <OUT>/src~/ .
A FRONT hazard (a wall that sweeps across the lane, flagged in <HAZARDS>) is drawn as a horizontal band cross-section that FILLS the cell width and TILES HORIZONTALLY (the leftmost and rightmost pixel columns of each frame must match), tapering to transparent at the top and bottom edges of its content, vertically centred; its content may use the whole cell height.

LOOK:
 - Light comes from the UPPER LEFT. Pixel art with hard pixel edges where the material is solid (debris, ice crystals, leaves, spray droplets); airy parts may use dithered semi-transparency, but NO blur, NO soft glow halo, nearest-neighbour only when scaling.
 - Palette: <PALETTE>. NO pink / magenta (hue 285-352) unless <WORLD> is Space. NO red. No UI, no text, no ground plane, no background, no drop shadow.
METHOD (mandatory): use IMAGE GENERATION (the built-in image generation tool) for the painted content: generate several candidates per hazard, inspect them, pick the best, then pixel-clean (nearest-neighbour only, keep rich ramps). Procedural-only results will be rejected. Compose cells programmatically into the final files.
PREVIEWS: never draw mock UI; the verify script writes a contact sheet of the art itself on dark #0b0b1a.
VERIFY (mandatory): run `python3 scripts/verify_weather_art.py <OUT> --world <WORLD> --fronts <FRONTS> --sheet <OUT>/preview.png` and fix until every line PASSes. Finish with a short summary, the measured numbers and your honest caveats (what is weakest). Do not commit.
