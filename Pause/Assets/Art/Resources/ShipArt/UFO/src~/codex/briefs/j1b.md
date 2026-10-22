You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this directory tree; do not edit any .cs/.meta/.json or any existing file; do not delete anything; do not commit.
New files go ONLY to ./Pause/Assets/Art/Resources/ShipArt/UFO/src~/codex/ (the trailing ~ matters: Unity ignores it). Put helpers in its tools/ subfolder.
Read ./docs/art-style.md first (neon pixel art). The player ship "UFO" has just been redrawn in hi-res neon pixel art (same style as the premium ship "Paragon").

REFERENCES (attached images, in order): (1) key_state0.png, (2) key_state1.png, (3) key_state2.png: the APPROVED key poses (intact, damaged, wreck), 1024 x 1024 on flat #00FF00. Keep EXACTLY that ship, scale, palette, pixel density and damage; only the stated change differs.
RULES: use IMAGE GENERATION for the painted content, then nearest-neighbour clean-up only (no blur). Never just recolour or warp a reference with a script. Flat #00FF00 background, no shadows/text/grid. No pink or magenta. No flames. No thick outline or halo. No green on the ship.

JOB J1b: POSE SHEETS. Make three files hull_redo_state0.png, hull_redo_state1.png, hull_redo_state2.png (for key_state0 / 1 / 2 respectively). Each is a 3 x 2 grid of equal square cells (3 x 1024 wide, 2 x 1024 tall), flat #00FF00, saucer centred at the SAME scale and position as its key. Cell order, row-major:
 0 LIGHTS OFF: the key pose with all eight rim lights (gold stars and red diamonds) DARK (unlit dark violet/navy panel pixels), the hub ring dimmer and the canopy glow slightly dimmer. Everything else identical, damage identical.
 1 GLINT 1: the key pose with a bright white diagonal glint streak (3 to 4 pixel-steps wide, lower left to upper right) across the LEFT half of the hex-gem canopy plus a bone-white sparkle at its tip. Canopy only; hull unchanged.
 2 GLINT 2: the streak moved to the RIGHT half of the canopy, thinner and fainter; rim lights dark like cell 0.
 3, 4, 5: leave completely empty (just the green). Banking is made by a script, do not draw banks.
In states 1 and 2 the damage must stay on exactly the same panels, pixel for pixel, as the key.
Write tools/check_j1b.py that, for each file, prints: % of pixels differing from the key (threshold 60 on summed RGB) for cells 0-2, and the % of canopy pixels (teal glass region) that differ for cells 1-2. Cell 1 must differ from the key in >= 0.8% of ALL pixels and cell 2 must differ from cell 1; cell 0 >= 0.4%; and all cells' silhouette must equal the key's silhouette exactly (0 pixels different). FIX AND REDRAW until every number passes; print the table in your final message. Also save preview_j1b.png (the three files side by side on dark #0b0b1a). If you cannot satisfy a threshold, say exactly which and why.
