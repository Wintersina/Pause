#!/usr/bin/env python3
"""Rebuild the bank poses (columns 6 = BankLeft, 7 = BankRight) of one ship from its own idle cells.

    python3 ShipArt/src~/bank_pose.py <Key>            build stock + every skin, then verify
    python3 ShipArt/src~/bank_pose.py <Key> --verify   only verify
    python3 ShipArt/src~/bank_pose.py <Key> --sheet out.png   also write a before/after-free contact sheet

The pose is ShipDamageTable.Posed applied as an image warp (nearest neighbour): the near side (left for
BankLeft) is squeezed toward the body axis by k = .80, the far side by k = .95, the whole ship moves 1.5 units
toward the bank, and the vertical offset is +1.2 (near) / -.6 (far) * |d| / 60 units (up). 1 unit = Cell /
Canvas = 2 px. The near side is shaded ~12 % darker. Each row uses its own idle cell (column 0) of the same
sheet, so damage stays on the same panels. The stock sheet is built first; skins take the stock bank alpha.
Reads only columns 0 and the other columns it must keep; rewrites only columns 6 and 7.

Per-ship numbers live in <Key>/src~/bank_pose.json (optional): {"axis": 128.0, "shade": 0.12}.
"""
import glob, io, json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)                      # .../ShipArt
CELL, CANVAS = 256, 128.0
PPU = CELL / CANVAS                               # ShipDamageTable.PixelsPerUnit
BANK_L, BANK_R = 6, 7
NEAR_K, FAR_K, SHIFT_U = 0.80, 0.95, 1.5
NEAR_DV, FAR_DV = 1.2, -0.6
MAX_WIDTH_CHANGE = 0.22
MIN_SEPARATION = 0.05


def config(key):
    cfg = {"axis": CELL / 2.0, "shade": 0.12}
    p = os.path.join(ROOT, key, "src~", "bank_pose.json")
    if os.path.exists(p):
        with open(p) as f:
            cfg.update(json.load(f))
    return cfg


def sheets(key):
    d = os.path.join(ROOT, key)
    stock = os.path.join(d, key + ".png")
    skins = sorted(p for p in glob.glob(os.path.join(d, key + "_*.bytes"))
                   if not p.endswith(("_hitbox.bytes", "_shield.bytes")))
    return stock, skins


def load(p):
    return np.array(Image.open(p).convert("RGBA"))


def save(p, a):
    b = io.BytesIO()
    Image.fromarray(a, "RGBA").save(b, "PNG")
    with open(p, "wb") as f:
        f.write(b.getvalue())


def posed_cell(idle, bank, axis, shade, alpha=None):
    """Warp one idle cell into the bank pose (bank -1 = left, +1 = right)."""
    h, w = idle.shape[:2]
    ys, xs = np.mgrid[0:h, 0:w]
    dx = xs + 0.5 - (axis + SHIFT_U * PPU * bank)     # output offset from the shifted axis (pixels)
    near = (dx < 0) == (bank < 0)
    k = np.where(near, NEAR_K, FAR_K)
    src_d = dx / k                                    # source offset from the axis
    sx = np.floor(axis + src_d).astype(int)
    d_units = np.abs(src_d) / PPU
    dv = np.where(near, NEAR_DV, FAR_DV) * d_units / 60.0   # up, units
    sy = np.floor(ys + 0.5 + dv * PPU).astype(int)
    ok = (sx >= 0) & (sx < w) & (sy >= 0) & (sy < h)
    out = np.zeros_like(idle)
    out[ok] = idle[sy[ok], sx[ok]]
    out[..., 3] = np.where(out[..., 3] > 127, 255, 0)
    if alpha is not None:
        out[..., 3] = alpha
    rgb = out[..., :3].astype(np.float32)
    rgb = np.where(near[..., None] & (out[..., 3:4] > 0), rgb * (1.0 - shade), rgb)
    out[..., :3] = np.clip(rgb + 0.5, 0, 255).astype(np.uint8)
    out[out[..., 3] == 0] = 0
    return out


def build_sheet(a, cfg, stock_banks=None):
    """Return a copy of sheet `a` with columns 6 and 7 rebuilt. stock_banks: {(row,col): alpha}."""
    a = a.copy()
    rows = a.shape[0] // CELL
    banks = {}
    for r in range(rows):
        idle = a[r * CELL:(r + 1) * CELL, 0:CELL]
        for col, bank in ((BANK_L, -1), (BANK_R, 1)):
            al = None if stock_banks is None else stock_banks[(r, col)]
            cell = posed_cell(idle, bank, cfg["axis"], cfg["shade"], al)
            a[r * CELL:(r + 1) * CELL, col * CELL:(col + 1) * CELL] = cell
            banks[(r, col)] = cell[..., 3].copy()
    return a, banks


def build(key):
    cfg = config(key)
    stock, skins = sheets(key)
    a, banks = build_sheet(load(stock), cfg)
    save(stock, a)
    for p in skins:
        s, _ = build_sheet(load(p), cfg, banks)
        save(p, s)
    print("built", key, "stock +", len(skins), "skins")


def cell(a, r, c):
    return a[r * CELL:(r + 1) * CELL, c * CELL:(c + 1) * CELL]


def extents(c, axis):
    xs = np.where((c[..., 3] > 0).any(0))[0]
    return axis - xs.min(), xs.max() + 1 - axis


def verify(key):
    cfg = config(key)
    axis = cfg["axis"]
    stock, skins = sheets(key)
    st = load(stock)
    ok = True
    print("%s (axis %.1f)" % (key, axis))
    for r in range(st.shape[0] // CELL):
        il, ir = extents(cell(st, r, 0), axis)
        bl = extents(cell(st, r, BANK_L), axis)
        br = extents(cell(st, r, BANK_R), axis)
        rl = (bl[0] / il, bl[1] / ir)
        rr = (br[0] / il, br[1] / ir)
        wl = (bl[0] + bl[1]) / (il + ir) - 1
        wr = (br[0] + br[1]) / (il + ir) - 1
        good = (rl[1] - rl[0] >= MIN_SEPARATION and rr[0] - rr[1] >= MIN_SEPARATION
                and abs(wl) < MAX_WIDTH_CHANGE and abs(wr) < MAX_WIDTH_CHANGE)
        ok &= good
        print("  row %d bankL L/R %.3f/%.3f  bankR L/R %.3f/%.3f  width %+.1f%% %+.1f%%  %s" %
              (r, rl[0], rl[1], rr[0], rr[1], wl * 100, wr * 100, "ok" if good else "FAIL"))
    for p in [stock] + skins:
        a = load(p)
        if not np.isin(a[..., 3], (0, 255)).all():
            print("  non-binary alpha in", os.path.basename(p)); ok = False
        for r in range(a.shape[0] // CELL):
            for c in (BANK_L, BANK_R):
                if p != stock and not np.array_equal(cell(a, r, c)[..., 3], cell(st, r, c)[..., 3]):
                    print("  bank alpha differs from stock:", os.path.basename(p), r, c); ok = False
    print("  verify", "PASSED" if ok else "FAILED")
    return ok


def contact(key, out):
    stock, skins = sheets(key)
    names = [stock] + skins[:3]
    bg = np.array([0x0e, 0x14, 0x24, 255], np.uint8)
    tiles = []
    for p in names:
        a = load(p)
        row = np.concatenate([cell(a, 0, c) for c in (0, BANK_L, BANK_R)], 1)
        comp = np.where(row[..., 3:4] > 0, row, bg[None, None, :])
        tiles.append(comp)
    img = np.concatenate(tiles, 0).astype(np.uint8)
    im = Image.fromarray(img, "RGBA")
    im.save(out)


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__)
    key = args[0]
    if "--verify" not in args:
        build(key)
    good = verify(key)
    if "--sheet" in args:
        contact(key, args[args.index("--sheet") + 1])
    sys.exit(0 if good else 1)
