You are making game art for "Pause" (Unity, vertical 2D shooter). Work ONLY inside this directory tree; do not edit any existing file (except you MAY overwrite key_state2.png in the output folder); do not delete anything else; do not commit.
New files go ONLY to ./Pause/Assets/Art/Resources/ShipArt/Hulls/src~/ligher/codex/ (the trailing ~ matters). Helpers go in its tools/ subfolder.
Read ./docs/art-style.md first (neon pixel art).

REFERENCES (attached, in order): (1) key_state0.png: the approved intact player ship "Ligher", 1024 x 1024 on flat #00FF00. (2) approved_key_state1.png: the approved lightly DAMAGED state (keep its look as the milder step). (3) mild_key_state2.png: the current wreck draft: painted style is right but it is FAR TOO MILD, it does not read as a wreck. (4) paragon_damaged.png: the quality target for damage.

JOB J1d: REDRAW ONLY THE WRECK, key_state2.png (1024 x 1024, flat #00FF00), same ship, pose, scale and pixel-art rendering as key_state0, outer SILHOUETTE IDENTICAL to key_state0 pixel for pixel (afterwards force: every pixel outside key_state0's silhouette becomes #00FF00; any green pixel inside becomes the dark cavity colour #1A0E0A).
Use IMAGE GENERATION (edit key_state0 / mild_key_state2 with much heavier painted damage), several candidates, pick the best, nearest-neighbour clean-up only. NO scripted overlay polygons, no translucent dark veils, no flat brown/black quads. Everything painted into the plating in the same pixel art:
 - the whole hull, nose to tail, LEFT and RIGHT halves, both fins: 40-55% of the hull surface visibly destroyed: 6-9 blown plates with ragged edges and dark cavities showing exposed ribs, cables, circuit boards; big hard-stepped burn scars and char (near-black brown, with dull rust and a few glowing orange cracks) across most of the remaining plating;
 - nose cap torn open, nose gem cracked and chipped;
 - the CANOPY crystal clearly cracked: bone-white crack lines across the teal glass, one facet shattered/dark, glow dimmed;
 - both tail fins bent and shredded (ragged dark bites inside the silhouette), fin lights smashed;
 - the two red slot lights partly smashed/flickering;
 - sparking cyan/amber arcs on both sides, glowing orange tears, a few embers; nozzle smashed and scorched.
It must clearly be a wrecked version of the same ship, much worse than approved_key_state1 on both sides. No outline/halo, no pink/magenta, no flames, NO GREEN on the ship.
Write tools/check_j1d.py printing: silhouette diff vs key_state0 (must be 0.000%), % of silhouette pixels changed vs state 0 (> 40%), vs approved_key_state1 (> 25%), and the left-half and right-half change % separately (each > 35%). Fix and redraw until it passes. Update preview_j1.png (state 0, approved state 1, new state 2 on #0b0b1a). Finish with honest caveats.
