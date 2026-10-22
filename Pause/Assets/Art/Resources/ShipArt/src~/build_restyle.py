#!/usr/bin/env python3
"""Rebuilds a player hull in the Paragon style from Codex's paintings.

    python3 build_restyle.py NeonComet [--no-skins] [--no-flame] [--no-codex]

The ship is addressed by NAME only; everything it reads and writes is in its own folder
ShipArt/<Key>/ (see shiplib.py). Generic version of Paragon/src~/build_paragon.py for the old
flat-cartoon hulls (restyle pass, Oct 2026). Per ship it reads

    <Key>/src~/restyle.json        the ship's build config (world size, nozzle lips, emitters, skin modes,
                                   flame bands, damage zones)
    <Key>/src~/codex/
      key_state{0,1,2}.png         1024 px key poses on #00FF00 (intact / damaged / wreck)
      hull_redo_state{0,1,2}.png   3x2 cells: lights off, glint 1, glint 2, bank L, bank R
      skin_<Name>_state{0,1,2}.png 3x3 repainted flipbooks (optional; palette fallback)
      flame_cruise.png / flame_boost.png  N cells 1:4 (optional)

and writes the 2304 x 768 sheet <Key>/<Key>.png (9 columns x 3 damage rows, 256 px cells, same
layout as every hull), the skin sheets <Key>/<Key>_<Skin>.bytes, the flame sheet
<Key>/<Key>_flame.png and <Key>/src~/data.json (rect, ppu, nozzles, emitters, strips) which
patch_restyle.py merges into <Key>/<Key>_data.json. Only keying / fitting / assembling
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
sys.path.insert(0, HERE)
import shiplib  # noqa: E402

spec = importlib.util.spec_from_file_location("bp", os.path.join(shiplib.src_dir("Paragon"), "build_paragon.py"))
bp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bp)

CELL, COLS, STATES = bp.CELL, bp.COLS, bp.STATES
GREEN = np.array([0, 255, 0], dtype=np.uint8)

KINDS = {"Sparks": "sparks", "Arc": "arc", "Smoke": "smoke", "Flame": "flame", "Leak": "leak", "Smolder": "smolder"}


def load_cfg(key):
    """<Key>/src~/restyle.json -> the build config (lists back to the tuples the code uses)."""
    cfg = json.load(open(os.path.join(shiplib.src_dir(key), "restyle.json")))
    cfg["emitters"] = [(int(st), kind, float(u), float(v)) for st, kind, u, v in cfg["emitters"]]
    cfg["skins"] = [tuple(x) for x in cfg["skins"]]
    if "noz_scale" in cfg:
        cfg["noz_scale"] = tuple(cfg["noz_scale"])
    if "noz_cells" in cfg:
        cfg["noz_cells"] = [tuple(c) for c in cfg["noz_cells"]]
    if "damage_zones" in cfg:
        cfg["damage_zones"] = {k: [tuple(z) for z in v] for k, v in cfg["damage_zones"].items()}
    fl = cfg["flame"]
    fl["bands"] = {k: tuple(int(v[i:i + 2], 16) for i in (1, 3, 5)) for k, v in fl["bands"].items()}
    return cfg


def is_green(rgb):
    v = rgb.astype(np.int16)
    r, g, b = v[..., 0], v[..., 1], v[..., 2]
    return (g > 100) & (g > r * 1.55) & (g > b * 1.4) & (g - r > 65) & (g - b > 65)


def hit_flash(rgb, hull=None):
    """Bone-white hull washed from the key pose (keeps ink + a hint of panel lines).
    `hull`: optional explicit mask (green ships: is_green mistakes deep jade armour for background)."""
    if hull is None:
        hull = ~is_green(rgb)
    peak = rgb.astype(np.int16).max(axis=2)
    out = np.empty_like(rgb)
    out[:] = GREEN
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


def fill_enclosed(img):
    """Close transparent pixels fully enclosed by the hull (bright highlights the green key took out):
    they shred the baked hitbox / shield contour. Filled from the neighbouring paint, alpha 255."""
    arr = np.array(img)
    a = arr[..., 3] > 0
    h, w = a.shape
    out = np.zeros((h, w), bool)
    stack = [(y, x) for y in range(h) for x in range(w) if (y in (0, h - 1) or x in (0, w - 1)) and not a[y, x]]
    for y, x in stack:
        out[y, x] = True
    while stack:
        y, x = stack.pop()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < h and 0 <= xx < w and not a[yy, xx] and not out[yy, xx]:
                out[yy, xx] = True
                stack.append((yy, xx))
    hole = ~a & ~out
    n = int(hole.sum())
    while hole.any():
        todo = np.argwhere(hole)
        paint = []
        for y, x in todo:
            nb = [arr[yy, xx, :3] for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1))
                  if 0 <= yy < h and 0 <= xx < w and arr[yy, xx, 3] > 0]
            if nb:
                paint.append((y, x, np.mean(nb, axis=0)))
        if not paint:
            break
        for y, x, c in paint:
            arr[y, x, :3] = c.astype(np.uint8)
            arr[y, x, 3] = 255
            hole[y, x] = False
    return Image.fromarray(arr), n


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
        hull_mask = (np.array(bp.key_green(keys[st]))[..., 3] > 0) if cfg.get("fill_holes") else None
        flash_src = Image.fromarray(hit_flash(np.array(keys[st]), hull_mask))
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
        if cfg.get("fill_holes"):
            filled = [fill_enclosed(p) for p in pieces]
            print(f"  state {st}: closed {sum(n for _, n in filled)} enclosed transparent px")
            pieces = [p for p, _ in filled]
        per_state.append(pieces)
    sheet =Image.new("RGBA", (CELL * COLS, CELL * STATES), (0, 0, 0, 0))
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


def zone_mask(zones, col, bbox0, bbox_c):
    """bool 256x256 mask of the union of ellipses, moved with column `col` (bob = y shift, bank = x stretch)."""
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(np.float64)
    dy = {1: -4, 2: -4, 5: 4}.get(col, 0)
    yk = yy - dy
    if col in (6, 7):
        kw = bbox0[1] - bbox0[0] + 1
        bw = bbox_c[1] - bbox_c[0] + 1
        xk = bbox0[0] + (xx - bbox_c[0]) * (kw / bw)
    else:
        xk = xx
    m = np.zeros((CELL, CELL), bool)
    for cx, cy, rx, ry in zones:
        m |= ((xk - cx) / rx) ** 2 + ((yk - cy) / ry) ** 2 <= 1.0
    return m


def compose_states(img, cfg):
    """Rows 1-2 = row 0 outside the damage zones, the painted damage state inside (cell for cell, col 8 left alone)."""
    z = cfg.get("damage_zones")
    if not z:
        return img
    a = np.array(img)
    out = a.copy()
    alpha0 = a[:CELL, :, 3]
    def bbx(c):
        xs = np.nonzero(alpha0[:, c * CELL:(c + 1) * CELL].any(axis=0))[0]
        return xs.min(), xs.max()
    b0 = bbx(0)
    for c in range(COLS - 1):
        bc = bbx(c)
        m1 = zone_mask(z["s1"], c, b0, bc)
        w1 = zone_mask(z.get("s1w", []), c, b0, bc)    # optional: spots where row 1 shows the wreck painting
        m1 = m1 | w1
        m2 = m1 | zone_mask(z["s2"], c, b0, bc)
        c0 = a[0:CELL, c * CELL:(c + 1) * CELL]
        for st, m in ((1, m1), (2, m2)):
            reg = (slice(st * CELL, (st + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cur = a[reg]
            if st == 1 and w1.any():
                cur = np.where(w1[..., None], a[2 * CELL:3 * CELL, c * CELL:(c + 1) * CELL], cur)
            out[reg] = np.where(m[..., None], cur, c0)
            out[reg][..., 3] = c0[..., 3]
    return Image.fromarray(out)


def write_skin_refs(sheet, codex):
    """3 x 3 reference sheets of the FINAL flipbook (cells 512 px, green bg) for the skin briefs."""
    for st in range(3):
        out = Image.new("RGB", (1536, 1536), (0, 255, 0))
        for c in range(COLS):
            cell = sheet.crop((c * CELL, st * CELL, (c + 1) * CELL, (st + 1) * CELL)).resize((512, 512), Image.NEAREST)
            bg = Image.new("RGBA", (512, 512), (0, 255, 0, 255))
            bg.alpha_composite(cell)
            out.paste(bg.convert("RGB"), ((c % 3) * 512, (c // 3) * 512))
        out.save(os.path.join(codex, "skinref_state%d.png" % st))


def write_skins(sheet, cfg, key, codex):
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
                    if cfg.get("fill_holes") and c != 8:
                        # the skin's own neighbouring paint (the red-armour recolour would leave green flecks here)
                        pending = hole.copy()
                        for _ in range(60):
                            if not pending.any():
                                break
                            have = (tile[..., 3] >= 128) & ~pending
                            fills = []
                            for y, x in np.argwhere(pending):
                                nb = [tile[yy, xx, :3] for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1))
                                      if 0 <= yy < CELL and 0 <= xx < CELL and have[yy, xx]]
                                if nb:
                                    fills.append((y, x, np.mean(nb, axis=0)))
                            if not fills:
                                break
                            for y, x, col in fills:
                                tile[y, x, :3] = col.astype(np.uint8)
                                tile[y, x, 3] = 255
                                pending[y, x] = False
                        hole = pending
                    tile[hole] = fba[reg][hole]
                    tile[~a] = 0
                    tile[a, 3] = base[reg][..., 3][a]
                    out.alpha_composite(Image.fromarray(tile), (c * CELL, st * CELL))
            print(f"  skin {name}: Codex sheets, {holes} silhouette pixels from the fallback")
            fb = compose_states(out, cfg)
        else:
            print(f"  skin {name}: palette-shift fallback")
        assert (np.array(fb)[..., 3] == base[..., 3]).all()
        buf = io.BytesIO()
        fb.save(buf, "PNG", optimize=True)
        with open(shiplib.skin_path(key, name), "wb") as fh:
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


def write_spin_flames(key, cfg, codex):
    """A spinner (Ninja, UFO) has no plume: Ring / RingBoost / Wake / WakeBoost strips, 8 square cells each
    (ring_cruise.png, ring_boost.png, wake_cruise.png, wake_boost.png), snapped to the five exhaust bands."""
    fl = cfg["flame"]
    files = [("Ring", "ring_cruise.png"), ("RingBoost", "ring_boost.png"),
             ("Wake", "wake_cruise.png"), ("WakeBoost", "wake_boost.png")]
    if not all(os.path.exists(os.path.join(codex, f)) for _, f in files):
        print("  no spin flame strips yet")
        return
    sz, n = fl.get("size", 120), fl["frames"]
    cols = np.array([fl["bands"][k] for k in ("outer", "mid", "core", "dark", "accent")], float)
    strips = {}
    for layer, f in files:
        im = Image.open(os.path.join(codex, f)).convert("RGB")
        w = im.width // n
        frames = []
        for i in range(n):
            keyed = bp.key_green(im.crop((i * w, 0, (i + 1) * w, im.height)))
            a = np.array(keyed.resize((sz, sz), Image.BOX)).astype(float)
            idx = np.full((sz, sz), -1)
            solid = a[..., 3] > 110
            d = ((a[..., None, :3] - cols[None, None]) ** 2).sum(-1)
            idx[solid] = d.argmin(-1)[solid]
            if layer.endswith("Boost"):
                idx = heat(idx)
            img = np.zeros((sz, sz, 4), np.uint8)
            for k, c in enumerate(cols):
                img[idx == k] = (*[int(v) for v in c], 255)
            frames.append(Image.fromarray(img))
        strips[layer] = frames
    layout = {l: (sz, sz, fl.get("stride", 122)) for l, _ in files}
    rows = shiplib.write_flame_sheet(key, strips, layout)
    print("  spin flames written:", [(r["layer"], r["frames"]) for r in rows])


def write_flames(key, cfg, codex):
    fl = cfg["flame"]
    if fl.get("kind") == "spin":
        return write_spin_flames(key, cfg, codex)
    if not all(os.path.exists(os.path.join(codex, f)) for f in ("flame_cruise.png", "flame_boost.png")):
        print("  no flame strips yet")
        return
    bp.BANDS.clear()
    bp.BANDS.update(fl["bands"])
    cols = np.array([bp.BANDS[k] for k in ("outer", "mid", "core", "dark", "accent")], float)
    try:
        strips = shiplib.read_strips(key)        # keeps the ship's other layers (a spinner's ring / wake)
    except (OSError, KeyError):
        strips = {}
    for layer, f in (("Plume", "flame_cruise.png"), ("PlumeBoost", "flame_boost.png")):
        im = Image.open(os.path.join(codex, f)).convert("RGB")
        n = fl["frames"]
        w = im.width // n
        frames = []
        for i in range(n):
            keyed = bp.key_green(im.crop((i * w, 0, (i + 1) * w, im.height)))
            a = np.array(keyed.resize((bp.FRAME_W, bp.FRAME_H), Image.BOX)).astype(float)
            idx = np.full((bp.FRAME_H, bp.FRAME_W), -1)
            solid = a[..., 3] > 110
            d = ((a[..., None, :3] - cols[None, None]) ** 2).sum(-1)
            idx[solid] = d.argmin(-1)[solid]
            if layer == "PlumeBoost":
                idx = heat(idx)
            frames.append(bp.paint(idx))
        strips[layer] = frames
    box = (bp.FRAME_W, bp.FRAME_H, bp.STRIDE)
    rows = shiplib.write_flame_sheet(key, strips, {"Plume": box, "PlumeBoost": box})
    print("  flames written:", [(r["layer"], r["frames"]) for r in rows])


def main():
    key = shiplib.resolve(sys.argv[1])
    cfg = load_cfg(key)
    codex = os.path.join(shiplib.src_dir(key), "codex")
    raw = build_sheet(cfg, codex)
    sheet = compose_states(raw, cfg)
    sheet.save(shiplib.hull_path(key), optimize=True)
    data = measure(sheet, cfg)
    data["skins"] = [n for n, _ in cfg["skins"]]
    write_skin_refs(raw, codex)
    if "--no-skins" not in sys.argv:
        write_skins(sheet, cfg, key, codex)
    if "--no-flame" not in sys.argv:
        write_flames(key, cfg, codex)
    data["strips"] = shiplib.load_data(key).get("exhaust", {}).get("strips", [])
    with open(os.path.join(shiplib.src_dir(key), "data.json"), "w") as fh:
        json.dump(data, fh, indent=1)
    print(json.dumps(data))
    print("next: python3 patch_restyle.py %s   (merges src~/data.json into %s)" % (key, os.path.basename(shiplib.data_path(key))))


if __name__ == "__main__":
    main()
