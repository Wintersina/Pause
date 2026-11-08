#!/usr/bin/env python3
"""Exports the website's leaderboard ship icons: one small PNG atlas of every non-premium ship hull (idle
drawing 0, intact row) in every skin it has.

  python3 scripts/export-leaderboard-ship-icons.py <out.png>     (needs Pillow)

Atlas layout (the site's pause/leaderboard.js reads it): a CELL x CELL grid, row = ship id - 1 (ids 1..15; the
premium ship 16 is never exported), column = skin index (0 = Stock .. 4); a skin a ship lacks is left empty and
the missing columns repeat the stock hull (the game falls back to stock too). Source: Pause/Assets/Art/Resources/ShipArt/<Key>/<Key>[_<Skin>].png|.bytes, a
2304 x 768 sheet of 256 px cells (9 columns x 3 damage rows), cropped to the ship's own sprite rect from
<Key>_data.json, fitted into the cell and quantised to a 64-colour palette to keep the file small (~50 KB budget).
"""
import json, os, sys
from PIL import Image

CELL = 32
SKINS = 5
SHIPS = 15
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Pause', 'Assets', 'Art', 'Resources', 'ShipArt')

def main(out):
    atlas = Image.new('RGBA', (CELL * SKINS, CELL * SHIPS), (0, 0, 0, 0))
    for key in sorted(os.listdir(ROOT)):
        data_path = os.path.join(ROOT, key, key + '_data.json')
        if not os.path.exists(data_path):
            continue
        data = json.load(open(data_path))
        if data['id'] < 1 or data['id'] > SHIPS:
            continue   # premium (16) or unknown
        r = data['rect']
        stock = None
        for skin_index, skin in enumerate(data['skins'][:SKINS]):
            base = os.path.join(ROOT, key, key if skin_index == 0 else key + '_' + skin['name'])
            path = base + '.png' if skin_index == 0 else base + '.bytes'
            if not os.path.exists(path):
                continue   # filled with the stock hull below, as the game does
            sheet = Image.open(path).convert('RGBA')
            # cell (0, 0): png rows run top to bottom; rect.y counts up from the cell bottom
            box = (r['x'], 256 - r['y'] - r['h'], r['x'] + r['w'], 256 - r['y'])
            hull = sheet.crop(box)
            scale = min((CELL - 2) / hull.width, (CELL - 2) / hull.height)
            size = (max(1, round(hull.width * scale)), max(1, round(hull.height * scale)))
            hull = hull.resize(size, Image.LANCZOS)
            if skin_index == 0:
                stock = hull
            atlas.paste(hull, (skin_index * CELL + (CELL - size[0]) // 2, (data['id'] - 1) * CELL + (CELL - size[1]) // 2))
        for skin_index in range(len(data['skins'][:SKINS]), SKINS):   # a ship with fewer skins: stock fills the rest
            if stock is not None:
                atlas.paste(stock, (skin_index * CELL + (CELL - stock.width) // 2, (data['id'] - 1) * CELL + (CELL - stock.height) // 2))
    q = atlas.quantize(colors=64, method=Image.FASTOCTREE, dither=Image.NONE)
    q.save(out, optimize=True)
    print(out, os.path.getsize(out), 'bytes')

if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'pause-ships.png')
