#!/usr/bin/env python3
"""Pre-flight for a world's themed ATTACK art (docs/world-attacks-art.md) before it is wired into
Pause/Assets/Art/Resources/Attacks/<World>/. Reproduces the numbers ShotSkinTest.AuditPixels checks in Unity.

  python3 verify_attack_art.py FILE.png [FILE.png ...] [--world frost|verdant|ember|tide] [--allow-hue 82 ...]
                               [--sheet OUT.png] [--backdrops DIR_OR_TILES...]

Files are named <world>_attack_<kind>.png (world from the name unless --world is given). Kinds and grids
(cell = 128 px unless stated; rows counted from the top; "used" cells drawn, the rest fully transparent):

  shots   1024x256  8x2   row 0 bolt a,b shard a,b shell a,b slag a,b | row 1 pool a,b sigA a,b sigB a,b sigC a,b
                          used cells: Frost 12 (0-11), Verdant 16, Ember 12 (0-11), Tide 14 (0-11 + sigC 14-15)
  fx      1024x256  8x2   all 16 used (impact x4, special x4 | trail x4, glyph a,b, flash a,b)
  strike  768x512         row 0 six 128x384 column bodies, row 1 six 128x128 (glyph a,b, burst x3; the 6th reserved)
  jet     Ember 768x384 / Tide 768x448   row 0 six frames (128x256 / 128x320), row 1 six 128x128 (nozzle x3, tip/splash x3)
  wave    512x480         rows 0-3 four 512x96 bodies (seamless left-right), row 4: capL capR gapMarker a,b (96x96 cells)
  ring    1024x128  8x1   all used (bar x4, gapMarker a,b, glyph a,b)
  beam    1024x128  8x1   all used (aim, beam a,b, flash a,b, spark a-c)
  lash    1024x128  8x1   all used (link a,b, tip a,b, root a,b, dash a,b)
  log     1024x256  4x2 of 256x128, all used (trunk roll frames)

Checks (FAIL exits 1):
  * exact size, RGBA; used cells non-empty; reserved cells exactly empty (alpha <= 24)
  * no pixel with alpha > 24 within 4 px of a cell border (a clipped sprite is a hard cut in game)
  * PINK CUE (PC2): >= 30% of the opaque pixels of every SHOT cell (shots file) are pink-family (hue 312-326 +-8, sat > .3)
    or near-white (sat < .2, value > .85); every other cell: >= 5% (a pink-white edge or core line)
  * HUE AUDIT (PC3): < 8% of the saturated pixels (sat > .35, value > .25) within 20 deg of a pickup hue
    178 (shield cyan) / 82 (repair green) / 259 (capacitor violet) / 37 (star-dust amber); --allow-hue H lifts one
    (e.g. a slot the art list allows); zero pixels in the player's red band (hue >= 345 or <= 15 at sat > .5)
  * frame pairs (a, b of shots/fx/glyphs/beam...) differ by >= 3% of the opaque pixels (flicker, drip, spin)
  * the art is real pixel art: downsample x2 (nearest) then upsample x2 is identical (>= 99.5% of pixels) - authored at 64 px
  * wave bodies tile: the first and last pixel column of each body frame match
  * --sheet writes a contact sheet: every file on #0b0b1a at 2x, then again over the first --backdrops tile (the world's mid layer:
    the readability the game will see), for the user's eyes
Needs numpy + Pillow.
"""
import sys, os, re, argparse
import numpy as np
from PIL import Image, ImageDraw
import colorsys

fails = 0
def check(ok, what):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok: fails += 1

def warn(ok, what):
    print(("PASS  " if ok else "WARN  ") + what)

PICKUPS = {178: "shield cyan", 82: "repair green", 259: "capacitor violet", 37: "star-dust amber"}
PINK_LO, PINK_HI = 312 - 8, 326 + 8
SHOT_PINK, STRIP_PINK, PICKUP_MAX = 0.30, 0.05, 0.08
MARGIN = 4

# kind -> (width, height) or per-world sizes; cells as (name, x, y, w, h, used?) built by layout()
SIZES = {"shots": (1024, 256), "fx": (1024, 256), "strike": (768, 512), "wave": (512, 480),
         "ring": (1024, 128), "beam": (1024, 128), "lash": (1024, 128), "log": (1024, 256)}
JET_SIZES = {"ember": (768, 384), "tide": (768, 448)}
SHOT_USED = {"frost": set(range(12)), "verdant": set(range(16)), "ember": set(range(12)), "tide": set(range(12)) | {14, 15}}
SHOT_NAMES = ["bolt a", "bolt b", "shard a", "shard b", "shell a", "shell b", "slag a", "slag b",
              "pool a", "pool b", "sigA a", "sigA b", "sigB a", "sigB b", "sigC a", "sigC b"]

def layout(kind, world, size):
    """-> list of (name, x, y, w, h, used, pair_with_index_or_None, shot?)"""
    cells = []
    def grid(prefix, cols, rows, cw, ch, y0=0, used=None, names=None, pairs=None, shot=False):
        n = 0
        for r in range(rows):
            for c in range(cols):
                i = n; n += 1
                nm = (names[i] if names and i < len(names) else f"{prefix}{i}")
                cells.append([nm, c * cw, y0 + r * ch, cw, ch, True if used is None else (i in used), None, shot])
    if kind == "shots":
        grid("shots", 8, 2, 128, 128, used=SHOT_USED.get(world, set(range(16))), names=SHOT_NAMES, shot=True)
        for i in range(0, 16, 2): cells[i][6] = i + 1
    elif kind == "fx":
        names = [f"impact {i}" for i in range(4)] + [f"special {i}" for i in range(4)] + [f"trail {i}" for i in range(4)] + ["glyph a", "glyph b", "flash a", "flash b"]
        grid("fx", 8, 2, 128, 128, names=names)
        cells[12][6] = 13; cells[14][6] = 15
    elif kind == "strike":
        grid("body", 6, 1, 128, 384, names=[f"column {i}" for i in range(6)])
        grid("low", 6, 1, 128, 128, y0=384, used={0, 1, 2, 3, 4}, names=["glyph a", "glyph b", "burst 0", "burst 1", "burst 2", "reserved"])
        cells[6][6] = 7
    elif kind == "jet":
        fh = 256 if world == "ember" else 320
        grid("flame" if world == "ember" else "jet", 6, 1, 128, fh, names=None)
        grid("low", 6, 1, 128, 128, y0=fh, names=["nozzle 0", "nozzle 1", "nozzle 2", "tip 0", "tip 1", "tip 2"])
    elif kind == "wave":
        for i in range(4): cells.append([f"body {i}", 0, i * 96, 512, 96, True, None, False])
        for i, nm in enumerate(["capL", "capR", "gapMarker a", "gapMarker b"]):
            cells.append([nm, i * 96, 384, 96, 96, True, None, False])
        cells[6][6] = 7
    elif kind == "ring":
        grid("ring", 8, 1, 128, 128, names=["bar 0", "bar 1", "bar 2", "bar 3", "gapMarker a", "gapMarker b", "glyph a", "glyph b"])
        cells[4][6] = 5; cells[6][6] = 7
    elif kind == "beam":
        grid("beam", 8, 1, 128, 128, names=["aim", "beam a", "beam b", "flash a", "flash b", "spark a", "spark b", "spark c"])
        cells[1][6] = 2; cells[3][6] = 4
    elif kind == "lash":
        grid("lash", 8, 1, 128, 128, names=["link a", "link b", "tip a", "tip b", "root a", "root b", "dash a", "dash b"])
        for i in (0, 2, 4, 6): cells[i][6] = i + 1
    elif kind == "log":
        grid("log", 4, 2, 256, 128, names=[f"roll {i}" for i in range(8)])
    return cells

def hsv(px):
    """px: (N,4) uint8 -> h(deg), s, v arrays of the opaque pixels"""
    rgb = px[:, :3].astype(np.float32) / 255.0
    mx, mn = rgb.max(1), rgb.min(1)
    d = mx - mn
    h = np.zeros(len(rgb), np.float32)
    r, g, b = rgb[:, 0], rgb[:, 1], rgb[:, 2]
    m = d > 1e-6
    idx = m & (mx == r); h[idx] = ((g - b)[idx] / d[idx]) % 6
    idx = m & (mx == g); h[idx] = (b - r)[idx] / d[idx] + 2
    idx = m & (mx == b); h[idx] = (r - g)[idx] / d[idx] + 4
    h = (h * 60) % 360
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx

def audit(cell, allow):
    px = cell.reshape(-1, 4)
    px = px[px[:, 3] > 24]
    n = len(px)
    if n == 0: return dict(n=0)
    h, s, v = hsv(px)
    white = (s < .2) & (v > .85)
    pink = (s > .3) & (h >= PINK_LO) & (h <= PINK_HI)
    red = (s > .5) & ((h >= 345) | (h <= 15))
    sat = (s > .35) & (v > .25)
    pick = np.zeros(len(h), bool)
    for hue in PICKUPS:
        if hue in allow: continue
        gap = np.abs(h - hue); gap = np.minimum(gap, 360 - gap)
        pick |= gap < 20
    return dict(n=n, pink=float((white | pink).sum()) / n, red=int(red.sum()),
                pick=float((sat & pick).sum()) / max(1, int(sat.sum())), sat=int(sat.sum()))

def border_ok(c):
    a = c[..., 3] > 24
    m = MARGIN
    return not (a[:m].any() or a[-m:].any() or a[:, :m].any() or a[:, -m:].any())

def differ(a, b):
    oa, ob = a[..., 3] > 24, b[..., 3] > 24
    op = oa | ob
    if op.sum() == 0: return 0.0
    diff = (np.abs(a.astype(int) - b.astype(int)).sum(2) > 30) | (oa != ob)
    return float((diff & op).sum()) / float(op.sum())

def check_file(path, world_arg, allow):
    name = os.path.basename(path)
    m = re.match(r"(?:(\w+?)_)?attack_(\w+)\.png$", name)
    if not m:
        check(False, f"{name}: named <world>_attack_<kind>.png"); return None
    world = (world_arg or m.group(1) or "").lower()
    kind = m.group(2)
    if kind not in SIZES:
        check(False, f"{name}: kind '{kind}' is one of {', '.join(sorted(SIZES))}"); return None
    img = Image.open(path)
    check(img.mode == "RGBA", f"{name}: RGBA ({img.mode})")
    a = np.asarray(img.convert("RGBA"))
    want = JET_SIZES.get(world, JET_SIZES["ember"]) if kind == "jet" else SIZES[kind]
    check((a.shape[1], a.shape[0]) == want, f"{name}: {want[0]}x{want[1]} ({a.shape[1]}x{a.shape[0]})")
    if (a.shape[1], a.shape[0]) != want: return None
    cells = layout(kind, world, want)
    empties, leaks, margins, pinks, picks, reds, flat = [], [], [], [], [], [], []
    blocks = []
    for ci, (nm, x, y, w, h, used, pair, shot) in enumerate(cells):
        c = a[y:y + h, x:x + w]
        blocks.append(c)
        solid = int((c[..., 3] > 24).sum())
        if not used:
            if solid: leaks.append(nm)
            continue
        if solid == 0: empties.append(nm); continue
        if not border_ok(c): margins.append(nm)
        au = audit(c, allow)
        need = SHOT_PINK if shot and (ci % 8) < 8 and kind == "shots" and not nm.startswith(("pool", "sig")) else STRIP_PINK
        if au["pink"] < need: pinks.append(f"{nm} {au['pink']:.0%}<{need:.0%}")
        if au["pick"] >= PICKUP_MAX: picks.append(f"{nm} {au['pick']:.0%}")
        if au["red"]: reds.append(f"{nm} {au['red']}px")
    check(not empties, f"{name}: every used cell is drawn {empties}")
    check(not leaks, f"{name}: reserved cells are empty {leaks}")
    check(not margins, f"{name}: nothing within {MARGIN} px of a cell border {margins}")
    check(not pinks, f"{name}: pink cue (>= {SHOT_PINK:.0%} of a shot cell, >= {STRIP_PINK:.0%} of a strip cell is pink-family/white) {pinks}")
    check(not picks, f"{name}: < {PICKUP_MAX:.0%} of the saturated area near a pickup hue 178/82/259/37 {picks}")
    check(not reds, f"{name}: no red-band pixels (hue 345-15, sat > .5) {reds}")
    # frame pairs
    still = []
    for ci, (nm, x, y, w, h, used, pair, shot) in enumerate(cells):
        if pair is None or not used: continue
        if differ(blocks[ci], blocks[pair]) < 0.03: still.append(nm)
    check(not still, f"{name}: frame b differs from frame a by >= 3% of the opaque pixels {still}")
    # 64 px authored, x2 nearest
    down = a[::2, ::2]
    up = np.repeat(np.repeat(down, 2, axis=0), 2, axis=1)
    same = float((up == a).all(2).mean())
    check(same >= .995, f"{name}: authored at 64 px and upscaled x2 with no smoothing ({same:.2%} identical after a x2 round trip)")
    if kind == "wave":
        seam = [i for i in range(4) if np.abs(blocks[i][:, 0].astype(int) - blocks[i][:, -1].astype(int)).mean() > 12]
        check(not seam, f"{name}: wave bodies tile left-right (first and last column match) {seam}")
    return a, cells

def sheet(out, files, tiles):
    bg = (11, 11, 26, 255)
    ims = [Image.open(p).convert("RGBA") for p in files]
    W = max(i.width for i in ims) * 2
    H = sum(i.height * 2 + 18 for i in ims)
    s = Image.new("RGBA", (W, H * (1 + (1 if tiles else 0))), bg)
    d = ImageDraw.Draw(s)
    y = 0
    for p, i in zip(files, ims):
        d.text((4, y + 3), os.path.basename(p), fill=(200, 200, 220, 255)); y += 18
        s.alpha_composite(i.resize((i.width * 2, i.height * 2), Image.NEAREST), (0, y))
        y += i.height * 2
    if tiles:
        base = Image.open(tiles[0]).convert("RGBA")
        for yy in range(y, s.height, base.height):
            for xx in range(0, W, base.width): s.alpha_composite(base, (xx, yy))
        for p, i in zip(files, ims):
            d.text((4, y + 3), os.path.basename(p) + " over the backdrop", fill=(255, 255, 255, 255)); y += 18
            s.alpha_composite(i.resize((i.width * 2, i.height * 2), Image.NEAREST), (0, y))
            y += i.height * 2
    s.convert("RGB").save(out)
    print("wrote", out, s.size)

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("files", nargs="+")
    ap.add_argument("--world")
    ap.add_argument("--allow-hue", type=int, action="append", default=[])
    ap.add_argument("--sheet")
    ap.add_argument("--backdrops", nargs="*", default=[])
    o = ap.parse_args()
    for p in o.files:
        check_file(p, o.world, set(o.allow_hue))
    if o.sheet: sheet(o.sheet, o.files, o.backdrops)
    print(("\nALL PASS" if not fails else f"\n{fails} FAILURES"))
    sys.exit(1 if fails else 0)

if __name__ == "__main__":
    main()
