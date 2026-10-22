#!/usr/bin/env python3
"""Writes the UFO briefs (j1, j1b, skin_*) next to this script. Adapted from src~/neoncomet/codex/briefs."""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = "./Pause/Assets/Art/Resources/ShipArt/UFO/src~/codex/"

HEAD = ('You are making game art for "Pause" (Unity, vertical 2D shooter, phone portrait). Work ONLY inside this directory '
        'tree; do not edit any .cs/.meta/.json or any existing file; do not delete anything; do not commit.\n'
        'New files go ONLY to ' + OUT + ' (the trailing ~ matters: Unity ignores it). Put helpers in its tools/ subfolder.\n'
        'Read ./docs/art-style.md first (neon pixel art). ')

J1 = HEAD + '''We are RESTYLING the player ship "UFO" from an old flat-cartoon look into the same art style as our premium ship "Paragon".

REFERENCES (attached images, in order):
 (1) paragon_ship_alone.png: the STYLE TARGET. Hi-res neon pixel art: densely painted hull with many small panel plates, panel seams, rivets, rim lights on the edges, 5-6 tone colour ramps with strong contrast, glowing cyan-blue canopy, saturated neon cores, crisp stair-stepped pixel edges, no blur. Look at HOW the hull is painted (tiny armour plates, lit edges, deep shadow panels), not at its shape.
 (2) paragon_damaged.png: how Paragon looks DAMAGED: blown panel with dark cavity, exposed ribs and wires, scorch, hairline cracks, burn scars.
 (3) ref_old_state0.png: the OLD UFO, intact, flat-cartoon style. Its SHAPE and identity are what you keep: a flying saucer seen from directly above (it spins in game, so it is a round-ish 12-sided disc): an outer rim ring of violet armour plates split by panel seams into 12 segments, an inner concentric ring line, a dark gunmetal hexagonal hub in the centre holding a teal-cyan hex-gem canopy, and eight small rim lights: gold four-point star lights and red diamond lights around the edge.
 (4) ref_old_state1.png and (5) ref_old_state2.png: the old damaged and wreck states, only a rough guide for WHERE damage goes.

JOB J1: THE THREE KEY POSES. Make three files in the output folder, each ONE image of exactly 1024 x 1024 px on flat pure chroma green #00FF00, the saucer centred, filling about 95% of the width (same position and extents as ref_old_state0.png, which is the framing to match; the new silhouette must stay within about 3% of the old one's width and height, same dodecagon-ish round disc family):
  key_state0.png  = UFO INTACT, redrawn in Paragon's painted style.
  key_state1.png  = DAMAGED: state 0 plus EXACTLY these two damage zones (and nothing else changed anywhere): (a) the LEFT rim, around 20% in from the left edge and 57% down the image (centre about x=205, y=585 px): one rim armour plate blown off showing a dark cavity with exposed ribs and sparking amber/cyan wires, soot streaks around it; (b) the LOWER-RIGHT rim, around x=705, y=750 px: a smaller scorched, cracked rim plate with a smoulder scar.
  key_state2.png  = HEAVILY DAMAGED: everything of state 1, PLUS exactly these zones: (c) the UPPER-RIGHT rim (around x=820, y=320): a jagged torn-open rim section with a glowing orange edge inside the silhouette; (d) the LOWER-LEFT rim (around x=320, y=810): a second panel blown open with arcing cyan/amber wires; (e) the CENTRE: the hex-gem canopy cracked (bone-white crack lines across the glass) and the hub scorched.
DAMAGE MUST STAY BOLD AND READABLE: a few large distinct features, NOT an all-over crack or lava texture. At least half of the hull must remain clean intact plating in state 2. All damage is painted INSIDE the intact silhouette: the outer silhouette of all three images must be identical pixel for pixel, and everything outside those zones must be IDENTICAL to state 0 (draw state 0 first, then derive 1 and 2 by editing only the zones; do not repaint the whole picture per state). No ghostly translucent overlays, no flat brown sticker polygons.
STYLE RULES:
- Use IMAGE GENERATION for the painted content (generate several candidates, inspect, pick the best), then clean up with nearest-neighbour only (no blur, no smoothing). Procedural-only / filter-only results are rejected. Never just recolour or warp a reference with a script: redraw.
- Painted like Paragon: panel plating detail, rim lights on the edges facing up-left, deep shading in the seams, glowing neon hex-gem canopy (cyan-teal, white-hot core, a bright highlight), lit hub ring, saturated cores. Rich contrast, no flat fills.
- NO thick cartoon outline and no halo/border line around the ship: only the thin natural dark edge shading Paragon has where the hull meets the background.
- Colour identity: violet armour (ramp from near-black indigo through #3A2478 and #7A52DC to #BBA4FF highlights), teal-cyan hex-gem canopy, gunmetal dark navy hub, gold star lights and red diamond lights on the rim (keep the eight lights). NO pink or magenta anywhere (pink is reserved for the Space world and enemies), so keep the violet clearly blue-violet. No green anywhere on the ship (the background is green and is keyed out): no green in the canopy either, use teal-cyan #1FB5B9 / #6EF2EE with white.
- No flames or exhaust. No shadows on the background, no text, no UI boxes, no grid lines.
Also write tools/check_j1.py that checks the three files are 1024x1024, prints the bounding box of the non-green pixels of each, the % of silhouette pixels that differ between state 0 and states 1/2 (must be < 1.5%), the % of pixels differing in colour between state 0 and each damaged state (must be > 2% for state 1 and > 6% for state 2, and below 35% for state 2 so the hull stays mostly clean), and that state 1's differences lie inside zones (a),(b) only. Fix and redraw until it passes, then print the table in your final message. Save preview_j1.png (the three images side by side on dark #0b0b1a).
Finish with a short summary of what you wrote and honest caveats.
'''

J1B = HEAD + '''The player ship "UFO" has just been redrawn in hi-res neon pixel art (same style as the premium ship "Paragon").

REFERENCES (attached images, in order): (1) key_state0.png, (2) key_state1.png, (3) key_state2.png: the APPROVED key poses (intact, damaged, wreck), 1024 x 1024 on flat #00FF00. Keep EXACTLY that ship, scale, palette, pixel density and damage; only the stated change differs.
RULES: use IMAGE GENERATION for the painted content, then nearest-neighbour clean-up only (no blur). Never just recolour or warp a reference with a script. Flat #00FF00 background, no shadows/text/grid. No pink or magenta. No flames. No thick outline or halo. No green on the ship.

JOB J1b: POSE SHEETS. Make three files hull_redo_state0.png, hull_redo_state1.png, hull_redo_state2.png (for key_state0 / 1 / 2 respectively). Each is a 3 x 2 grid of equal square cells (3 x 1024 wide, 2 x 1024 tall), flat #00FF00, saucer centred at the SAME scale and position as its key. Cell order, row-major:
 0 LIGHTS OFF: the key pose with all eight rim lights (gold stars and red diamonds) DARK (unlit dark violet/navy panel pixels), the hub ring dimmer and the canopy glow slightly dimmer. Everything else identical, damage identical.
 1 GLINT 1: the key pose with a bright white diagonal glint streak (3 to 4 pixel-steps wide, lower left to upper right) across the LEFT half of the hex-gem canopy plus a bone-white sparkle at its tip. Canopy only; hull unchanged.
 2 GLINT 2: the streak moved to the RIGHT half of the canopy, thinner and fainter; rim lights dark like cell 0.
 3, 4, 5: leave completely empty (just the green). Banking is made by a script, do not draw banks.
In states 1 and 2 the damage must stay on exactly the same panels, pixel for pixel, as the key.
Write tools/check_j1b.py that, for each file, prints: % of pixels differing from the key (threshold 60 on summed RGB) for cells 0-2, and the % of canopy pixels (teal glass region) that differ for cells 1-2. Cell 1 must differ from the key in >= 0.8% of ALL pixels and cell 2 must differ from cell 1; cell 0 >= 0.4%; and all cells' silhouette must equal the key's silhouette exactly (0 pixels different). FIX AND REDRAW until every number passes; print the table in your final message. Also save preview_j1b.png (the three files side by side on dark #0b0b1a). If you cannot satisfy a threshold, say exactly which and why.
'''

SKIN = HEAD + '''The player ship is "UFO", redrawn in hi-res neon pixel art (violet saucer armour, teal hex-gem canopy, gunmetal hub, gold star and red diamond rim lights).

REFERENCES (attached images, in order): (1) skinref_state0.png, (2) skinref_state1.png, (3) skinref_state2.png: the FINAL approved flipbook, three 3 x 3 sheets (intact, damaged, wreck), 9 poses each in the order key pose / bob up / bob up lights off / glint 1 / glint 2 lights off / bob down / bank left / bank right / hit flash. Each is 1536 x 1536 (cells 512 px).
RULES: no pink or magenta anywhere. No flames. No outline or halo around the ship. Flat pure #00FF00 background; no green on the ship.

JOB SKIN-%(name)s: a COLOURWAY of the finished flipbook. Redraw each of the three sheets in the "%(name)s" colourway and write skin_%(name)s_state0.png, skin_%(name)s_state1.png, skin_%(name)s_state2.png (same 3 x 3 layout, each cell 1024 x 1024 i.e. 3072 x 3072 per file, flat #00FF00 background, every pose at the SAME position and scale as the reference sheet). THE DRAWING MUST STAY IDENTICAL cell for cell (same silhouette, panel lines, damage, cracks, glints, rim light positions): ONLY COLOURS CHANGE, and the damage must sit on exactly the same panels in every cell of a state:
%(colour)s
Everything else (hex-gem canopy glass, hub, ink edge, glints, and ALL damage effects: sparks, glowing orange tears, embers, soot) stays as in the reference. The hit-flash cell (the last cell) stays bone-white.
Use IMAGE GENERATION to repaint each sheet from the reference (several candidates, pick the truest to the reference), then clean with nearest-neighbour. Do NOT simply hue-shift with a script: the result must be a painted redraw, pixel-faithful to the drawing (same plating texture, panel lines and rim lights, just in the new colours with proper ramp shading).
Write tools/check_skin_%(name)s.py that, for each of the three files and each of the 9 cells, prints: silhouette (non-green mask) difference vs the reference cell (must be < 1.5%%), difference over the canopy/blue region (must be < 6%%), and the fraction of all pixels that changed (must be > %(minchg)s%% except the hit-flash cell). FIX AND REDRAW until every number passes; print the summary in your final message. Finish with honest caveats.
'''

SKINS = {
    "Night": ("- the violet ARMOUR (all violet plating, with its darker shading and highlights) becomes deep midnight indigo-navy metal: base #2A2E6B, shadow #1A1F45, rim-light highlights bright cyan #6EF2EE along the upper-left edges of the plates (neon edge lighting) and a few pale blue-white highlights. Keep the gold star and red diamond rim lights exactly as they are.", 12),
    "Sodium": ("- the violet ARMOUR becomes sodium-lamp orange metal: base #F2862B, shadow #A9481A, highlights #FFB43C with a few pale cream highlights; plating texture and seams kept. Keep the canopy teal, and keep the gold star and red diamond lights.", 12),
    "Bone": ("- the violet ARMOUR becomes bone-white ceramic plating: base #F4EAD4, shadow #B9AE98, crisp warm-grey seams and shading, a few pure white highlights. Keep the canopy teal and the gold star and red diamond lights.", 12),
    "Abductor": ("- SPECIAL SPLIT LIVERY. A clean VERTICAL split down the exact centre of the saucer (the vertical line through the hub centre, x = 512 of the cell): the LEFT half of the armour stays the stock violet (base #7A52DC, shadow #3A2478, highlights #BBA4FF) and the RIGHT half of the armour is repainted teal (base #1FB5B9, shadow #0F5E6A, highlights #6EF2EE) with its own re-shaded plating. One clean straight division, not mottled patches, no gradient, no blended zone; the hub and its canopy are NOT split (they keep their reference colours). Damage scars keep their colours on both halves.", 3),
}

for name, text in (("j1", J1), ("j1b", J1B)):
    open(os.path.join(HERE, name + ".md"), "w").write(text)
for name, (colour, minchg) in SKINS.items():
    open(os.path.join(HERE, "skin_%s.md" % name), "w").write(SKIN % dict(name=name, colour=colour, minchg=minchg))
print("briefs written")
