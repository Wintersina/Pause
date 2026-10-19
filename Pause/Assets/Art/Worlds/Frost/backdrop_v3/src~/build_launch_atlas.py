#!/usr/bin/env python3
"""Bakes Frost's four new launch-site strips into the `docks` atlas (docks.png/.json) from Codex's masters (read-only inputs).

Inputs  (Codex, untouched): Art/Enemies/Elite/Frost/new_docks_lights~/<site>_launch_v2.png
        (six cells, 768 px tall: 0 closed, 1 closed + amber lamp (not shown in game), 2 opening, 3 opening further, 4 open, 5 closing;
        NO flash / flare / halo / ring in any of them) for ice_locked_offshore_rig, cryo_silo, glacier_cave_hangar, icebreaker_deck.
        The first three are 768 px wide cells, the icebreaker's are 576 (a long hull in a narrow floe).
Output: Art/Backgrounds/Resources/Worlds/Frost/Backdrop3/docks.png + docks.json   (BackdropAtlas manifest, y bottom-up)

ORDER   Codex's v2 strips are in the manifest's order already, so the atlas keeps their natural order (no re-ordering, pixels untouched):
        0 closed, 1 closed + one small amber warning lamp (not shown in game: the ship's own blinking launch light is the tell),
        2 doors opening, 3 opening further, 4 fully open (hold), 5 closing (half shut).
        EXCEPT the rig (SITE_ORDER): its v2 strip has the open grate in cell 0, so it is re-ordered by how open the doors are.

Cells   rig_00..05, silo_00..05, cave_00..05, icebreaker_00..05 -- every cell shrunk 3x (768 -> 256 px tall / wide, the way the
        other Frost landmarks and sites are drawn: ~1.5 u wide, ~250 px on a phone; LANCZOS on premultiplied alpha so no dark
        fringe) -- the full 768 px masters would be ~9 MB of the Frost texture budget (WorldBackdropTest.FrostTextureBudgetBytes),
        these ~0.7 MB.  PAD transparent pixels (>= the 6x6 ASTC block) between cells so no compressed block mixes two of them.

The pad points (where the elite comes out) are in FrostTuning.SitePads, as fractions of the cell from its centre (the build's
build_art.py SITES table: .5 across, .5 / .519 / .4375 / .514 down). FrostBackdropTest / FrostLaunchSiteTest check each sits on
the drawing.

    python3 build_launch_atlas.py [<site source dir> [<atlas out dir>]]   (from Pause/Assets)
"""
import json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..', '..'))
src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ASSETS, 'Art/Enemies/Elite/Frost/new_docks_lights~')
out = sys.argv[2] if len(sys.argv) > 2 else os.path.join(ASSETS, 'Art/Backgrounds/Resources/Worlds/Frost/Backdrop3')

SITES = [('rig', 'ice_locked_offshore_rig'), ('silo', 'cryo_silo'), ('cave', 'glacier_cave_hangar'), ('icebreaker', 'icebreaker_deck')]
CELLS, SHRINK, PAD = 6, 3, 6
OPEN_ORDER = [0, 1, 2, 3, 4, 5]   # natural order (v2 strips)
# the rig's v2 strip is not in manifest order (cell 0 is already the open grate, cell 3 the most shut): by openness it reads 3, 2, 1, 4 with 5 half shut
SITE_ORDER = {'rig': [3, 0, 2, 1, 4, 5]}


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
    rows.append((tag, [shrink(strip.crop((j * sw, 0, (j + 1) * sw, sh)), w, h) for j in SITE_ORDER.get(tag, OPEN_ORDER)], w, h))

W = PAD + CELLS * (max(r[2] for r in rows) + PAD)
H = PAD + sum(h + PAD for _, _, _, h in rows)
sheet = Image.new('RGBA', (W, H), (0, 0, 0, 0))
sprites = []
y = PAD
for tag, cells, w, h in rows:
    for i, c in enumerate(cells):
        x = PAD + i * (max(r[2] for r in rows) + PAD)
        sheet.paste(c, (x, y))
        sprites.append({'n': '%s_%02d' % (tag, i), 'x': x, 'y': H - y - h, 'w': w, 'h': h})   # Unity rects: y from the bottom
    y += h + PAD
sheet.save(os.path.join(out, 'docks.png'), optimize=True)
with open(os.path.join(out, 'docks.json'), 'w') as f:
    json.dump({'pixelScale': 1.0, 'sheetW': W, 'sheetH': H, 'sprites': sprites}, f, indent=1)
print('docks.png', W, 'x', H, '=', W * H / 1e6, 'Mpx', [(t, w, h) for t, _, w, h in rows])
