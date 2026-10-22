#!/usr/bin/env python3
"""Rebuilds a player hull in the Paragon style from Codex's paintings.

    python3 build_restyle.py NeonComet [--no-skins] [--no-flame] [--no-codex]

Generic version of paragon/build_paragon.py for the old flat-cartoon hulls
(restyle pass, Oct 2026). Per ship it reads  src~/<key lower>/codex/ :

    key_state{0,1,2}.png         1024 px key poses on #00FF00 (intact / damaged / wreck)
    hull_redo_state{0,1,2}.png   3x2 cells: lights off, glint 1, glint 2, bank L, bank R
    skin_<Name>_state{0,1,2}.png 3x3 repainted flipbooks (optional; palette fallback)
    flame_cruise.png / flame_boost.png  N cells 1:4 (optional)

and writes the 2304 x 768 sheet (9 columns x 3 damage rows, 256 px cells, same
layout as every hull), the skin sheets (Resources/ShipArt/Skins/<Key>_<Skin>.bytes),
the exhaust strips and  <key>/data.json  (rect, ppu, nozzles, emitters) which
patch_restyle.py writes into the C# tables. Only keying / fitting / assembling
here: all painting is Codex's. The old rect width (world size) is kept.
"""
import importlib.util
import io
import itertools
import json
import os
import re
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("bp", os.path.join(HERE, "paragon", "build_paragon.py"))
bp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bp)

CELL, COLS, STATES = bp.CELL, bp.COLS, bp.STATES
HULLS = bp.HULLS
SKINS_DIR = bp.SKINS_DIR
GREEN = np.array([0, 255, 0], dtype=np.uint8)
KEYRGB = GREEN

KINDS = {"Sparks": "sparks", "Arc": "arc", "Smoke": "smoke", "Flame": "flame", "Leak": "leak", "Smolder": "smolder"}


def parse_emitters(text):
    return [(int(a), KINDS[b], float(c), float(d))
            for a, b, c, d in re.findall(r"E\((\d), (\w+), ([\d.]+)f, ([\d.]+)f\)", text)]


# per ship: old hull width in the 256 cell (the world size the ship keeps), old emitters (kept
# positions; nudged onto paint), skin names, flame bands (the ShipExhaustStyle row) and atlas slots
SHIPS = {
    "Paranoid": dict(
        id=10, hull_w=232, world_w=243 / 810.0, nozzles=2, noz_scale=(.72, .72), magenta_key=True,
        noz_cells=[(35, 206, .72), (222, 206, .72)],
        emitters=parse_emitters(
            "E(1, Sparks, 45.7f, 39.8f), E(1, Smoke, 17.7f, 92.8f), E(1, Smolder, 45.7f, 39.8f), "
            "E(2, Arc, 90.1f, 65.2f), E(2, Flame, 110.3f, 92.8f), E(2, Leak, 64.0f, 97.4f), "
            "E(2, Smolder, 107.4f, 61.7f), E(2, Smolder, 90.1f, 65.2f), E(2, Smolder, 16.9f, 81.3f), "
            "E(2, Smolder, 114.0f, 80.2f), E(2, Smolder, 46.7f, 73.9f), E(2, Smolder, 114.2f, 32.5f)"),
        skins=[("Night", "night"), ("Sodium", "sodium"), ("Bone", "bone"), ("Signal", "stock")],
        flame=dict(x=(0, 300), y=698, frames=6,
                   bands=dict(outer=(0x5D, 0xBB, 0x3A), mid=(0x1F, 0xB5, 0xB9), core=(0xB6, 0xEE, 0x8A),
                              dark=(0x2A, 0x5E, 0x1E), accent=(0xF4, 0xEA, 0xD4))),
    ),
    "NeonComet": dict(
        id=1, hull_w=232, world_w=243 / 759.375, nozzles=2, noz_scale=(.72, .72),
        emitters=parse_emitters(
            "E(1, Sparks, 32.6f, 82.6f), E(1, Smoke, 52.2f, 106.4f), E(1, Smolder, 32.8f, 82.3f), "
            "E(2, Arc, 107.2f, 92.3f), E(2, Flame, 75.8f, 104.6f), E(2, Leak, 69.9f, 92.3f), "
            "E(2, Smolder, 108.4f, 92.6f), E(2, Smolder, 82.2f, 90.1f), E(2, Smolder, 55.2f, 97.6f), "
            "E(2, Smolder, 75.2f, 61.3f), E(2, Smolder, 53.2f, 61.9f)"),
        skins=[("Night", "night"), ("Sodium", "sodium"), ("Bone", "bone"), ("Kaneda", "stock")],
        flame=dict(x=(0, 300), y=1280, frames=6,
                   bands=dict(outer=(0xD8, 0x23, 0x2C), mid=(0xF2, 0x86, 0x2B), core=(0xF4, 0xEA, 0xD4),
                              dark=(0x86, 0x12, 0x1F), accent=(0x6E, 0xF2, 0xEE))),
    ),
}


MAGENTA_KEY = False     # ships whose hull is green (Paranoid) are painted on flat #FF00FF instead


def key_magenta(img):
    """Chroma key for #FF00FF backgrounds (green hulls): no despill needed (nothing on the hull is pink)."""
    a = np.array(img).astype(int)
    dist = np.sqrt((a[..., 0] - 255) ** 2 + a[..., 1] ** 2 + (a[..., 2] - 255) ** 2)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    bg = (dist < 120) | ((r - g > 70) & (b - g > 70))
    out = np.dstack([np.clip(a, 0, 255).astype(np.uint8), np.where(bg, 0, 255).astype(np.uint8)])
    out[bg] = 0
    return Image.fromarray(out)


def is_green(rgb):
    v = rgb.astype(np.int16)
    r, g, b = v[..., 0], v[..., 1], v[..., 2]
    if MAGENTA_KEY:
        return ((r - g > 70) & (b - g > 70)) | ((r > 200) & (b > 200) & (g < 90))
    return (g > 100) & (g > r * 1.55) & (g > b * 1.4) & (g - r > 65) & (g - b > 65)


def hit_flash(rgb):
    """Bone-white hull washed from the key pose (keeps ink + a hint of panel lines)."""
    hull = ~is_green(rgb)
    peak = rgb.astype(np.int16).max(axis=2)
    out = np.empty_like(rgb)
    out[:] = KEYRGB
    out[hull] = (244, 234, 212)
    out[hull & (peak < 45)] = (6, 8, 20)
    out[hull & (peak >= 45) & (peak < 85)] = (92, 88, 91)
    out[hull & (peak >= 85) & (peak < 135)] = (175, 164, 151)
    out[hull & (peak > 235)] = (255, 250, 232)
    return out


def roll_y(img, dy):
    arr = np.array(img)
    out = np.zeros_like(arr)
    if dy < 0:
        out[:dy] = arr[-dy:]
    elif dy > 0:
        out[dy:] = arr[:-dy]
    else:
        out = arr
    return Image.fromarray(out)


def build_sheet(cfg, codex):
    keys = [Image.open(os.path.join(codex, f"key_state{s}.png")).convert("RGB") for s in range(3)]
    k0 = np.array(bp.key_green(keys[0]))[..., 3]
    ys, xs = np.nonzero(k0)
    bw = xs.max() - xs.min() + 1
    scale = cfg["hull_w"] / bw
    anchor = ((xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2)
    print(f"  key bbox width {bw}px -> scale {scale:.4f}")
    dy_px = 4                                    # idle bob = 2 canvas units = 4 cell px
    per_state = []
    for st in range(3):
        key_cell = bp.fit_cells([keys[st]], 1024, scale, anchor)[0]
        flash_src = Image.fromarray(hit_flash(np.array(keys[st])))
        flash = bp.fit_cells([flash_src], 1024, scale, anchor)[0]
        rp = os.path.join(codex, f"hull_redo_state{st}.png")
        redo = None
        if os.path.exists(rp) and "--no-redo" not in sys.argv:
            rcells, rn = bp.codex_cells(rp, 3, 2)
            redo = bp.fit_cells(rcells[:5], rn, scale * 1024 / rn, (anchor[0] * rn / 1024, anchor[1] * rn / 1024))
        pieces = []
        for c in range(COLS):
            if c == 0:
                pieces.append(key_cell)
            elif c == 1:
                pieces.append(roll_y(key_cell, -dy_px))
            elif c == 5:
                pieces.append(roll_y(key_cell, dy_px))
            elif c == 8:
                pieces.append(flash)
            elif redo is not None:
                pieces.append({2: lambda: roll_y(redo[0], -dy_px), 3: lambda: redo[1], 4: lambda: redo[2],
                               6: lambda: redo[3], 7: lambda: redo[4]}[c]())
            elif c == 2:
                pieces.append(roll_y(key_cell, -dy_px))
            elif c in (3, 4):
                pieces.append(key_cell)
            else:
                pieces.append(bp.warp(key_cell, 0, -1 if c == 6 else 1))
        per_state.append(pieces)
    sheet = Image.new("RGBA", (CELL * COLS, CELL * STATES), (0, 0, 0, 0))
    for st in range(3):
        for c in range(COLS):
            base = np.array(per_state[0][c])
            cur = np.array(per_state[st][c])
            if st:
                m0 = base[..., 3] > 0
                fill = m0 & (cur[..., 3] == 0)
                cur[fill] = base[fill]
                cur[~m0] = 0
                cur[..., 3] = base[..., 3]       # the intact alpha, exactly
            sheet.alpha_composite(Image.fromarray(cur), (c * CELL, st * CELL))
    return sheet


def measure(sheet, cfg):
    arr = np.array(sheet)[..., 3]
    x0 = y0 = 10 ** 6
    x1 = y1 = -1
    for s in range(STATES):
        for c in range(COLS):
            ys, xs = np.nonzero(arr[s * CELL:(s + 1) * CELL, c * CELL:(c + 1) * CELL])
            x0, y0 = min(x0, int(xs.min())), min(y0, int(ys.min()))
            x1, y1 = max(x1, int(xs.max()) + 1), max(y1, int(ys.max()) + 1)
    x0, y0, x1, y1 = max(0, x0 - 1), max(0, y0 - 1), min(CELL, x1 + 1), min(CELL, y1 + 1)
    w, h = x1 - x0, y1 - y0
    ppu = w / cfg["world_w"]
    rest = arr[:CELL, :CELL] > 200
    # nozzles: each engine pod's bottom lip = the outermost column groups of the lowest painted rows
    noz = []
    if cfg.get("noz_cells"):
        # explicit nozzle lip centres in the rest cell (px): (x, y_bottom_row, scale); y is walked up onto paint
        pts = [(px, py, k) for px, py, k in cfg["noz_cells"]]
    else:
        low = int(np.nonzero(rest.any(axis=1))[0].max())
        cols = np.nonzero(rest[low - 1:low + 1].any(axis=0))[0]
        groups = np.split(cols, np.nonzero(np.diff(cols) > 3)[0] + 1)
        assert len(groups) == cfg["nozzles"], groups
        pts = [(int(round((g[0] + g[-1]) / 2)), low - 3, k) for g, k in zip(groups, cfg["noz_scale"])]
    for px, py, k in pts:
        while not rest[py, px]:
            py -= 1
        noz.append((px + .5 - x0, y1 - (py + .5), k))

    def on_paint(st, uu, vv, margin=2):
        for c, s_ in itertools.product(range(COLS), range(st, STATES)):
            dyu = bp.IDLE[c][0] if c < 6 else 0
            bank = -1 if c == 6 else (1 if c == 7 else 0)
            x, y = uu, vv + dyu
            if bank:
                d = x - 64
                near = (d < 0) == (bank < 0)
                k = .80 if near else .95
                x = 64 + d * k + 1.5 * bank
                y += (1.2 if near else -.6) * abs(d) / 60
            px, py = int(c * CELL + x * 2), int(s_ * CELL + y * 2)
            if px < 4 or py < 4 or arr[py - margin:py + margin + 1, px - margin:px + margin + 1].min() <= 200:
                return False
        return True

    E = []
    for st, kind, uu, vv in cfg["emitters"]:
        best = None
        for r in range(0, 20):
            for dx, dy in itertools.product(range(-r, r + 1), repeat=2):
                if max(abs(dx), abs(dy)) != r:
                    continue
                if on_paint(st, uu + dx, vv + dy):
                    best = (round(uu + dx, 1), round(vv + dy, 1))
                    break
            if best:
                break
        assert best, (kind, uu, vv)
        E.append((st, kind, *best))
    return dict(rect=[x0, CELL - y1, w, h], ppu=round(ppu, 4),
                nozzles=[(round(a, 1), round(b, 1), k) for a, b, k in noz], emitters=E)


def recolour(sheet, mode):
    """Scripted palette shift of the red armour (fallback + silhouette guard for Codex skins)."""
    arr = np.array(sheet)
    hsv = np.array(Image.fromarray(arr[..., :3]).convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    hd = np.minimum(h, 255 - h)
    red = (hd < 20) & (s > 100) & (v > 60) & (arr[..., 3] > 0)
    newh, news, newv = h, s, v
    if mode == "night":
        newh, news, newv = 170 + 0 * h, s * .8, v * .55
    elif mode == "sodium":
        newh, news, newv = 17 + 0 * h, s * .9, np.clip(v * 1.05 + 10, 0, 255)
    elif mode == "bone":
        newh, news, newv = 30 + 0 * h, s * .12, np.clip(v * 1.15 + 40, 0, 255)
    hsv[..., 0] = np.where(red, newh, h)
    hsv[..., 1] = np.where(red, news, s)
    hsv[..., 2] = np.where(red, newv, v)
    out = Image.fromarray(np.clip(hsv, 0, 255).astype(np.uint8), "HSV").convert("RGB")
    res = np.dstack([np.array(out), arr[..., 3]])
    res[res[..., 3] == 0] = 0
    return Image.fromarray(res)


def write_skin_refs(sheet, codex):
    """3 x 3 reference sheets of the FINAL flipbook (cells 512 px, green bg) for the skin briefs."""
    for st in range(3):
        out = Image.new("RGB", (1536, 1536), tuple(int(v) for v in KEYRGB))
        for c in range(COLS):
            cell = sheet.crop((c * CELL, st * CELL, (c + 1) * CELL, (st + 1) * CELL)).resize((512, 512), Image.NEAREST)
            bg = Image.new("RGBA", (512, 512), tuple(int(v) for v in KEYRGB) + (255,))
            bg.alpha_composite(cell)
            out.paste(bg.convert("RGB"), ((c % 3) * 512, (c // 3) * 512))
        out.save(os.path.join(codex, "skinref_state%d.png" % st))


def write_skins(sheet, cfg, key, codex):
    os.makedirs(SKINS_DIR, exist_ok=True)
    base = np.array(sheet)
    for name, mode in cfg["skins"]:
        fb = recolour(sheet, mode)
        paths = [os.path.join(codex, f"skin_{name}_state{st}.png") for st in range(3)]
        if all(os.path.exists(q) for q in paths) and "--no-codex" not in sys.argv:
            out = Image.new("RGBA", sheet.size, (0, 0, 0, 0))
            holes = 0
            fba = np.array(fb)
            for st, q in enumerate(paths):
                cells, n = bp.codex_cells(q)
                for c, cell in enumerate(cells):
                    big, _ = bp.downscale_rgba(bp.key_green(cell), CELL / n)
                    tile = np.array(big)
                    if tile.shape[:2] != (CELL, CELL):
                        tile = np.array(big.resize((CELL, CELL), Image.LANCZOS))
                    reg = (slice(st * CELL, (st + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
                    a = base[reg][..., 3] > 0
                    hole = a & (tile[..., 3] < 128)
                    holes += int(hole.sum())
                    tile[hole] = fba[reg][hole]
                    tile[~a] = 0
                    tile[a, 3] = base[reg][..., 3][a]
                    out.alpha_composite(Image.fromarray(tile), (c * CELL, st * CELL))
            print(f"  skin {name}: Codex sheets, {holes} silhouette pixels from the fallback")
            fb = out
        else:
            print(f"  skin {name}: palette-shift fallback")
        assert (np.array(fb)[..., 3] == base[..., 3]).all()
        buf = io.BytesIO()
        fb.save(buf, "PNG", optimize=True)
        with open(os.path.join(SKINS_DIR, f"{key}_{name}.bytes"), "wb") as fh:
            fh.write(buf.getvalue())


def heat(idx):
    """Boost flames burn hotter than cruise (ExhaustStyleTest wants boost > 1.08x the cruise brightness):
    one ring of each hotter band grows outward (outer -> mid next to mid/core, mid -> core next to core).
    Band indices: 0 outer, 1 mid, 2 core, 3 dark, 4 accent."""
    def near(m):
        out = np.zeros_like(m)
        out[1:] |= m[:-1]
        out[:-1] |= m[1:]
        out[:, 1:] |= m[:, :-1]
        out[:, :-1] |= m[:, 1:]
        return out
    new = idx.copy()
    new[(idx == 1) & near(idx == 2)] = 2
    new[(idx == 0) & near((idx == 1) | (idx == 2))] = 1
    new[(idx == 3) & near(idx == 0)] = 0
    return new


def write_flames(cfg, codex):
    fl = cfg["flame"]
    if not all(os.path.exists(os.path.join(codex, f)) for f in ("flame_cruise.png", "flame_boost.png")):
        print("  no flame strips yet")
        return
    bp.BANDS.clear()
    bp.BANDS.update(fl["bands"])
    atlas = Image.open(bp.ATLAS).convert("RGBA")
    top = atlas.height - fl["y"] - bp.FRAME_H
    cols = np.array([bp.BANDS[k] for k in ("outer", "mid", "core", "dark", "accent")], float)
    for x0, f in zip(fl["x"], ("flame_cruise.png", "flame_boost.png")):
        im = Image.open(os.path.join(codex, f)).convert("RGB")
        n = fl["frames"]
        w = im.width // n
        for i in range(n):
            keyed = bp.key_green(im.crop((i * w, 0, (i + 1) * w, im.height)))
            a = np.array(keyed.resize((bp.FRAME_W, bp.FRAME_H), Image.BOX)).astype(float)
            idx = np.full((bp.FRAME_H, bp.FRAME_W), -1)
            solid = a[..., 3] > 110
            d = ((a[..., None, :3] - cols[None, None]) ** 2).sum(-1)
            idx[solid] = d.argmin(-1)[solid]
            if f == "flame_boost.png":
                idx = heat(idx)
            box = (x0 + i * bp.STRIDE, top, x0 + i * bp.STRIDE + bp.FRAME_W, top + bp.FRAME_H)
            atlas.paste(Image.new("RGBA", (bp.FRAME_W, bp.FRAME_H), (0, 0, 0, 0)), box)
            atlas.alpha_composite(bp.paint(idx), (box[0], box[1]))
    atlas.save(bp.ATLAS, optimize=True)
    print("  flames pasted")


def main():
    key = sys.argv[1]
    cfg = SHIPS[key]
    if cfg.get("magenta_key"):
        global MAGENTA_KEY, KEYRGB
        MAGENTA_KEY = True
        KEYRGB = np.array([255, 0, 255], dtype=np.uint8)
        bp.key_green = key_magenta
    codex =os.path.join(HERE, key.lower(), "codex")
    sheet = build_sheet(cfg, codex)
    sheet.save(os.path.join(HULLS, key + ".png"), optimize=True)
    data = measure(sheet, cfg)
    data["skins"] = [n for n, _ in cfg["skins"]]
    write_skin_refs(sheet, codex)
    if "--no-skins" not in sys.argv:
        write_skins(sheet, cfg, key, codex)
    if "--no-flame" not in sys.argv:
        write_flames(cfg, codex)
    with open(os.path.join(HERE, key.lower(), "data.json"), "w") as fh:
        json.dump(data, fh, indent=1)
    print(json.dumps(data))


if __name__ == "__main__":
    main()
