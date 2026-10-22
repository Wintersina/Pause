"""Storm = the CHEVRON livery skin, rebuilt as an explicit composite (the Codex painting read as flat orange stripes).
Reads the committed Storm sheet as the base (colours + damage), removes its old orange livery by refilling those
pixels from the stock hull through a stock->Storm colour table, then draws crisp amber "^" chevrons (3 stacked on the
rear fuselage, one small on each wing; cream top line, darker lower edge) on the idle cell and moves them with every
cell (bob = shift, bank = the same affine map as the other skins) and hides them where damage lies.
Idempotent: pass --base <old storm sheet> once; by default reads git-tracked VoltViper_Storm.bytes (original saved to
src~/storm_base.png on first run and reused afterwards). Writes VoltViper_Storm.bytes. Alpha stays equal to stock."""
import io
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.join(HERE, "..", "..", "..")
CELL = 256
AMBER, CREAM, SHADE = (0xFF, 0xB0, 0x1C), (0xFF, 0xF0, 0xC4), (0xC8, 0x6A, 0x0C)


def hsv(a):
    return np.array(Image.fromarray(np.ascontiguousarray(a[..., :3])).convert("HSV")) / 255.


def orange(h, a):
    return (a[..., 3] > 0) & (h[..., 0] > 0.05) & (h[..., 0] < 0.14) & (h[..., 1] > 0.35) & (h[..., 2] > 0.35)


def dilate(m, n=1):
    for _ in range(n):
        o = m.copy()
        o[1:] |= m[:-1]; o[:-1] |= m[1:]; o[:, 1:] |= m[:, :-1]; o[:, :-1] |= m[:, 1:]
        o[1:, 1:] |= m[:-1, :-1]; o[:-1, :-1] |= m[1:, 1:]; o[1:, :-1] |= m[:-1, 1:]; o[:-1, 1:] |= m[1:, :-1]
        m = o
    return m


def erode(m, n=1):
    return ~dilate(~m, n)


def chevron(dr, cx, y0, hw, d, t):
    pts = [(cx, y0), (cx + hw, y0 + d), (cx + hw, y0 + d + t), (cx, y0 + t), (cx - hw, y0 + d + t), (cx - hw, y0 + d)]
    dr.polygon(pts, fill=AMBER + (255,))
    dr.line([(cx - hw, y0 + d + t), (cx, y0 + t), (cx + hw, y0 + d + t)], fill=SHADE + (255,), width=1)
    dr.line([(cx - hw, y0 + d), (cx, y0), (cx + hw, y0 + d)], fill=CREAM + (255,), width=1)


def layer():
    im = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    for y0 in (116, 136, 156):
        chevron(dr, 128, y0, 32, 16, 7)
    chevron(dr, 62, 118, 11, 7, 4)
    chevron(dr, 194, 118, 11, 7, 4)
    return np.array(im)


def extent(al, band):
    xs = np.nonzero(al[band[0]:band[1]].any(axis=0))[0]
    return xs.min(), xs.max() + 1


def vext(al):
    ys = np.nonzero(al.any(axis=1))[0]
    return ys.min(), ys.max() + 1


def clean_base(stock, base):
    """remove the old orange livery from the Storm base: refill from the stock pixel via a stock->Storm colour table."""
    hb, hs = hsv(base), hsv(stock)
    om = orange(hb, base) & ~orange(hs, stock)
    yy, xx = np.mgrid[0:stock.shape[0], 0:stock.shape[1]]
    box = ((yy % CELL) > 95) & ((yy % CELL) < 200) & ((xx % CELL) > 36) & ((xx % CELL) < 220)
    om &= box
    fill = dilate(om, 2) & box & (base[..., 3] > 0)
    ok = (base[..., 3] > 0) & ~dilate(om, 3)
    key = (stock[..., 0].astype(int) >> 2) << 16 | (stock[..., 1].astype(int) >> 2) << 8 | (stock[..., 2].astype(int) >> 2)
    table = {}
    for k, c in zip(key[ok], base[ok][:, :3]):
        table.setdefault(int(k), []).append(c)
    lut = {k: np.median(np.array(v), axis=0) for k, v in table.items()}
    ks = np.array(list(lut.keys()))
    kc = np.array([[k >> 16, (k >> 8) & 255, k & 255] for k in ks], float)
    out = base.copy()
    for y, x in zip(*np.nonzero(fill)):
        k = int(key[y, x])
        if k in lut:
            v = lut[k]
        else:
            q = np.array([k >> 16, (k >> 8) & 255, k & 255], float)
            v = lut[int(ks[((kc - q) ** 2).sum(1).argmin()])]
        out[y, x, :3] = np.round(v).astype(np.uint8)
    return out


def main():
    stock = np.array(Image.open(os.path.join(SHIP, "VoltViper.png")).convert("RGBA"))
    basep = os.path.join(HERE, "..", "..", "storm_base.png")
    if not os.path.exists(basep):
        Image.open(os.path.join(SHIP, "VoltViper_Storm.bytes")).convert("RGBA").save(basep)
    base = np.array(Image.open(basep).convert("RGBA"))
    base = clean_base(stock, base)
    out = base.copy()
    lay = layer()
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(float)
    idle_al = stock[0:CELL, 0:CELL, 3] > 0
    for c in range(8):
        ci = stock[0:CELL, c * CELL:(c + 1) * CELL]
        ac = ci[..., 3] > 0
        if c in (6, 7):
            l0, r0 = extent(idle_al, (100, 150))
            lc, rc = extent(ac, (100, 150))
            t0, b0 = vext(idle_al)
            tc, bc = vext(ac)
            xk = l0 + (xx - lc) * ((r0 - l0) / (rc - lc))
            yk = t0 + (yy - tc) * ((b0 - t0) / (bc - tc))
        else:
            dy = {1: -4, 2: -4, 5: 4}.get(c, 0)
            xk, yk = xx, yy - dy
        xi = np.clip(np.round(xk).astype(int), 0, CELL - 1)
        yi = np.clip(np.round(yk).astype(int), 0, CELL - 1)
        L = lay[yi, xi]
        inner = erode(ac, 3)
        for r in range(3):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cell = out[reg].copy()
            use = (L[..., 3] > 0) & inner
            if r:
                sc = stock[reg]
                dm = dilate(np.abs(sc[..., :3].astype(int) - ci[..., :3].astype(int)).sum(2) > 90, 0)
                use &= ~dm
            cell[use, :3] = L[use][:, :3]
            out[reg] = cell
    # hit-flash column unchanged
    out[:, 8 * CELL:] = base[:, 8 * CELL:]
    assert (out[..., 3] == stock[..., 3]).all()
    buf = io.BytesIO()
    Image.fromarray(out).save(buf, "PNG", optimize=True)
    open(os.path.join(SHIP, "VoltViper_Storm.bytes"), "wb").write(buf.getvalue())
    print("Storm written")


main()
