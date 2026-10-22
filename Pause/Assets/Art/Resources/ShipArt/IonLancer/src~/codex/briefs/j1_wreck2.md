You are making game art for "Pause" (Unity, vertical 2D shooter). Work ONLY inside this directory tree; do not edit any .cs/.meta/.json or any existing file; do not delete anything; do not commit.
New files go ONLY to ./Pause/Assets/Art/Resources/ShipArt/IonLancer/src~/codex/ (the trailing ~ matters: Unity ignores it). Put helpers in its tools/ subfolder.
Read ./docs/art-style.md first (neon pixel art).

REFERENCES (attached images, in order): (1) key_state0.png: the approved INTACT IonLancer, 1024x1024 on flat #00FF00. (2) ref_bar_wreck.png: another ship's wreck, ONLY to show how single damage features are painted (shattered canopy, torn wingtip with a glowing broken edge, exposed ribs and wires on a wing, scorched engine pods). Copy how those features are painted, NOT the amount: it is too busy for us.

JOB: write TWO files, key_state1_v3.png (DAMAGED, a clear middle step) and key_state2_v3.png (WRECK), each 1024 x 1024 on flat #00FF00. Both are derived from key_state0.png so the silhouette (non-green mask) is IDENTICAL to key_state0 pixel for pixel; all damage is painted INSIDE the intact silhouette as solid opaque pixels (no translucent or ghosted overlays).
THE KEY RULE: a FEW BOLD, DISTINCT, READABLE damage features, NOT an all-over texture. At least HALF of the hull stays CLEAN, intact, readable cobalt armour with its panel plating. Do NOT cover the hull evenly with lava cracks, burn texture or noise; no overall darkening or dirt wash.
 key_state2_v3.png (WRECK) features, each a bold clear shape:
   1. canopy cracked: bone-white crack lines across the glass, glass dimmed;
   2. LEFT wing: one big blown panel (dark cavity, exposed ribs, a few sparking cyan/amber wires);
   3. RIGHT wing: the wingtip torn off in a jagged bite with a glowing orange broken edge, plus one smaller blown panel with ribs;
   4. one scorched, cracked patch on the nose / fuselage with a short glowing orange crack;
   5. both engine pods: scorch marks and one orange glow tear each; one canard scorched;
   6. a handful of embers/sparks.
 key_state1_v3.png (DAMAGED) = roughly HALF of those features, on BOTH sides, not only the left wing: the left wing blown panel (smaller than in the wreck, with ribs and a few wires), a smaller scorch patch + a few hairline cracks on the RIGHT wing, canopy intact with at most one hairline crack, light scorch on the engine pods. Clearly milder than key_state2_v3 and clearly worse than key_state0; the damage that exists in state 1 stays at the same place in state 2 (state 2 = state 1 + more).
Style: same painted hi-res neon pixel density as key_state0 (hard-stepped pixels, no blur); cobalt/ion-blue identity kept; no outline or halo, no pink or magenta, no flames outside the hull, no green on the ship.
Use IMAGE GENERATION for the painted content (several candidates, pick the best, nearest-neighbour clean-up only, force the silhouette to key_state0's). Never just darken or overlay noise.
Write tools/check_wreck2.py: prints 1024x1024 for both files; silhouette difference vs key_state0 (< 0.5%); % of silhouette pixels differing from key_state0 by > 60 summed RGB for state 1 (target 5-12%) and state 2 (target 10-20%; a result above 24% is TOO BUSY and must be redrawn); left-half and right-half damage % for state 1 (each > 2%) and state 2 (each > 5%); the damage of state 2 over state 1 > 4%. FIX AND REDRAW until it passes; print the table. Save preview_wreck2.png (key_state0, key_state1_v3, key_state2_v3 side by side on #0b0b1a, also at 2x). Finish with honest caveats.
