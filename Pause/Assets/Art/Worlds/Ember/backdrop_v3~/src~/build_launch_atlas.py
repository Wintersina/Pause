#!/usr/bin/env python3
"""Bakes Ember's four launch-site strips into the `docks` atlas (docks.png/.json) from Codex's masters (read-only inputs).

Inputs  (Codex, untouched): Art/Enemies/Elite/Ember/new_docks_lights~/<site>_launch_v2.png (six cells, one site each: crucible_silo,
        lava_moat_bunker, brass_gantry_cradle, kiln_mouth), in the natural play order the user approved ("the gates close"):
        0 closed, 1 closed + one small amber warning lamp (never shown in game: the ship's own launch light is the tell), 2-3 opening,
        4 fully open, 5 closing (half shut). Shaded segmented iron / brass doors, no flash / flare / halo / glow burst / smoke.
        (The first delivery, *_launch.png, had crude flat black panels in cells 0, 1, 2, 5 and played backwards; it is superseded.)
Output: Art/Backgrounds/Resources/Worlds/Ember/Backdrop3/docks.png + docks.json   (BackdropAtlas manifest, y bottom-up)

Cells   <tag>_00..05 for crucible, bunker, brassgantry, kilnmouth -- all six cells in order, each shrunk 3x (768 -> 256 px, the way the other
        Ember sites are drawn: ~1.5 u wide), LANCZOS on premultiplied alpha so no dark fringe; PAD transparent pixels (>= the 6x6 ASTC block)
        between cells. About 0.7 MB of ASTC 6x6 against the Ember texture budget (WorldBackdropTest.EmberTextureBudgetBytes).

    python3 build_launch_atlas.py [<site source dir> [<atlas out dir>]]   (from Pause/Assets)
"""
import json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', '..'))
src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ASSETS, 'Art/Enemies/Elite/Ember/new_docks_lights~')
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ASSETS, 'Art/Backgrounds/Resources/Worlds/Ember/Backdrop3')

SITES = [('crucible', 'crucible_silo'), ('bunker', 'lava_moat_bunker'), ('brassgantry', 'brass_gantry_cradle'), ('kilnmouth', 'kiln_mouth')]
CELLS, SHRINK, PAD = 6, 3, 6
SOURCE = tuple(range(CELLS))   # every cell, in play order
NAMES = tuple(range(CELLS))    # name 00..05 -> cell


def shrink(cell, w, h):
    """LANCZOS on premultiplied alpha, then back to straight alpha."""
    a = np.asarray(cell, dtype=np.float32) / 255.0
    pm = a.copy()
    pm[..., :3] *= a[..., 3:4]
    ch = [Image.fromarray(pm[..., i], 'F').resize((w, h), Image.LANCZOS) for i in range(4)]
    r = np.stack([np.asarray(c) for c in ch], axis=-1)
    al = np.clip(r[..., 3:4], 0, 1)
    rgb = np.where(al > 1e-4, r[..., :3] / np.maximum(al, 1e-4), 0)
    return Image.fromarray((np.clip(np.concatenate([rgb, al], axis=-1), 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')


rows = []
for tag, name in SITES:
    strip = Image.open(os.path.join(src, name + '_launch_v2.png')).convert('RGBA')
    sw, sh = strip.size[0] // CELLS, strip.size[1]
    assert strip.size[0] == CELLS * sw, (name, strip.size)
    w, h = round(sw / SHRINK), round(sh / SHRINK)
    rows.append((tag, [shrink(strip.crop((j * sw, 0, (j + 1) * sw, sh)), w, h) for j in SOURCE], w, h))

colw = max(r[2] for r in rows)
W = PAD + len(SOURCE) * (colw + PAD)
H = PAD + sum(h + PAD for _, _, _, h in rows)
sheet = Image.new('RGBA', (W, H), (0, 0, 0, 0))
sprites = []
y = PAD
for tag, cells, w, h in rows:
    xs = []
    for i, c in enumerate(cells):
        x = PAD + i * (colw + PAD)
        sheet.paste(c, (x, y))
        xs.append(x)
    for n, ci in enumerate(NAMES):
        sprites.append({'n': '%s_%02d' % (tag, n), 'x': xs[ci], 'y': H - y - h, 'w': w, 'h': h})   # Unity rects: y from the bottom
    y += h + PAD
sheet.save(os.path.join(out, 'docks.png'), optimize=True)
with open(os.path.join(out, 'docks.json'), 'w') as f:
    json.dump({'pixelScale': 1.0, 'sheetW': W, 'sheetH': H, 'sprites': sprites}, f, indent=1)
print('docks.png', W, 'x', H, '=', W * H / 1e6, 'Mpx', [(t, w, h) for t, _, w, h in rows])
