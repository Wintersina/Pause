#!/usr/bin/env python3
"""Bakes Space's launch-dock atlas (docks.png/.json) from Codex's masters (read-only inputs).

Inputs  (Codex, untouched): Art/Enemies/Elite/Space/new_docks_lights~/<dock>_launch.png
        (six 1024 px wide cells: 0 idle, 1 lights blink on, 2 doors / iris opening, 3 flare, 4 fully open, 5 closing)
        for orbital_shipyard_gantry, carrier_hangar, ring_gate, derelict_dock.
Output: Art/Backgrounds/Resources/Worlds/Space/Backdrop/docks.png + docks.json   (BackdropAtlas manifest, y bottom-up)

Cells   gantry_00..05, carrier_00..05, gate_00..05, derelict_00..05 -- each launch cell shrunk 4x to 256 px wide (LANCZOS on
        premultiplied alpha), the way a dock is drawn on screen (about 1.4-1.8 u wide, ~170-280 px on a phone): the full 1024 px
        masters would be ~11 MB of the Space texture budget (WorldBackdropTest.SpaceTextureBudgetBytes), these are ~0.6 MB.
        PAD transparent pixels (>= the 6x6 ASTC block) between cells so no compressed block mixes two of them.

The pad points (where the elite comes out) are in SpaceDocks.cs, as fractions of the cell from the dock manifest's pixel
coordinates: SpaceDockTest checks each sits on the drawing.

    python3 build_docks_atlas.py [<elite source dir> [<atlas out dir>]]   (from Pause/Assets)
"""
import json, os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ASSETS, 'Art/Enemies/Elite/Space/new_docks_lights~')
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ASSETS, 'Art/Backgrounds/Resources/Worlds/Space/Backdrop')

DOCKS = [('gantry', 'orbital_shipyard_gantry'), ('carrier', 'carrier_hangar'), ('gate', 'ring_gate'), ('derelict', 'derelict_dock')]
CELLS, CELL_W, SRC_W, PAD = 6, 256, 1024, 6

rows = []
for tag, name in DOCKS:
    strip = Image.open(os.path.join(src, name + '_launch.png')).convert('RGBA')
    h = strip.size[1]
    assert strip.size[0] == CELLS * SRC_W, (name, strip.size)
    ch = round(h * CELL_W / SRC_W)
    rows.append((tag, [strip.crop((i * SRC_W, 0, (i + 1) * SRC_W, h)).resize((CELL_W, ch), Image.LANCZOS) for i in range(CELLS)], ch))

W = PAD + CELLS * (CELL_W + PAD)
H = PAD + sum(ch + PAD for _, _, ch in rows)
sheet = Image.new('RGBA', (W, H), (0, 0, 0, 0))
sprites = []
y = PAD
for tag, cells, ch in rows:
    for i, c in enumerate(cells):
        x = PAD + i * (CELL_W + PAD)
        sheet.paste(c, (x, y))
        sprites.append({'n': '%s_%02d' % (tag, i), 'x': x, 'y': H - y - ch, 'w': CELL_W, 'h': ch})   # Unity rects: y from the bottom
    y += ch + PAD
sheet.save(os.path.join(out, 'docks.png'), optimize=True)
with open(os.path.join(out, 'docks.json'), 'w') as f:
    json.dump({'pixelScale': 1.0, 'sheetW': W, 'sheetH': H, 'sprites': sprites}, f, indent=1)
print('docks.png', W, 'x', H, '=', W * H / 1e6, 'Mpx')
