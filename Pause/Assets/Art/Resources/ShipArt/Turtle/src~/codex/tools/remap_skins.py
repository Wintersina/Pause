"""Turtle Night / Sodium / Bone from the FINAL stock sheet by a controlled palette remap (no Codex repaint).
Run after build_restyle.py Turtle (which writes the Codex versions), before the bake; make_shellback.py is independent.

Armour pixels (warm tan / gold hue, outside the lamp, nozzle and the burning damage effects) are mapped by their
luminance onto an explicit ramp per skin, so the tonal structure (dark seams, lit plates, light scutes) of the stock art
is kept exactly and the damage sits on the same pixels in every cell and every skin. Everything else (canopy gem, red
lights, amber lamp and nozzle, flippers, sparks / embers / tears) comes from the stock pixel; Night also darkens the
flippers. Night gets a cool cyan rim light on plate edges facing up-left. Alpha is the stock alpha exactly."""
import colorsys
import io
import os
import numpy as np
from PIL import Image

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
CELL = 256


def hx(s):
    return tuple(int(s[i:i + 2], 16) for i in (1, 3, 5))


# (luminance, colour): piecewise linear ramps; shell tan spans ~60-170, scutes ~170-215, glints above
RAMPS = {
    "Night": [(0, "#07091A"), (45, "#10143A"), (90, "#1C2250"), (130, "#2A2E6B"), (165, "#363D85"),
              (195, "#4C56A8"), (225, "#7C88D6"), (255, "#C4CEFF")],
    "Sodium": [(0, "#2A1006"), (45, "#5A2410"), (90, "#A9481A"), (130, "#D8631F"), (165, "#F2862B"),
               (195, "#FFB43C"), (225, "#FFD58A"), (255, "#FFF1CC")],
    "Bone": [(0, "#2A251C"), (45, "#6B6352"), (90, "#A89E88"), (130, "#D2C6AC"), (165, "#E9DEC5"),
             (195, "#F4EAD4"), (225, "#FFF8E8"), (255, "#FFFFFF")],
}


def ramp(name, lum):
    pts = RAMPS[name]
    xs = [p[0] for p in pts]
    cols = np.array([hx(p[1]) for p in pts], float)
    out = np.empty(lum.shape + (3,))
    for ch in range(3):
        out[..., ch] = np.interp(lum, xs, cols[:, ch])
    return out


def hsv(a):
    r, g, b = [a[..., i].astype(float) / 255 for i in range(3)]
    mx = np.maximum(np.maximum(r, g), b)
    mn = np.minimum(np.minimum(r, g), b)
    d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rr = m & (mx == r)
    gg = m & (mx == g) & ~rr
    bb = m & ~rr & ~gg
    h[rr] = (60 * ((g - b)[rr] / d[rr])) % 360
    h[gg] = 60 * ((b - r)[gg] / d[gg]) + 120
    h[bb] = 60 * ((r - g)[bb] / d[bb]) + 240
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h, s, mx


def grow(m, n=1):
    for _ in range(n):
        k = m.copy()
        k[1:] |= m[:-1]; k[:-1] |= m[1:]; k[:, 1:] |= m[:, :-1]; k[:, :-1] |= m[:, 1:]
        m = k
    return m


def lamp_centre(cell):
    w = cell[110:175, 96:160].astype(int)
    m = (w[..., 0] > 235) & (w[..., 1] > 150) & (w[..., 2] < 170) & (w[..., 3] > 0)
    if m.sum() < 3:
        return None
    ys, xs = np.nonzero(m)
    return xs.mean() + 96, ys.mean() + 110


def remap(stock, name):
    out = stock.copy()
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    for c in range(9):
        idle = stock[0:CELL, c * CELL:(c + 1) * CELL]
        lc = lamp_centre(idle)
        for r in range(3):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cell = stock[reg]
            al = cell[..., 3] > 0
            h, s, v = hsv(cell[..., :3])
            lum = 0.3 * cell[..., 0] + 0.59 * cell[..., 1] + 0.11 * cell[..., 2]
            arm = al & (h >= 18) & (h <= 58) & (s > 0.22) & (v > 0.12)
            if c == 8:
                continue                                   # the hit-flash column stays bone white
            arm &= ~((yy > 226) & (np.abs(xx - 128) < 24))                # nozzle lip
            if lc is not None:
                near = ((xx - lc[0]) ** 2 + (yy - lc[1]) ** 2) <= 7 ** 2
                arm &= ~(near & (s > 0.55) & (v > 0.8))                     # the amber lamp itself (bright, saturated)
            if r:                                                          # burning damage effects stay as they are
                dm = grow(np.abs(cell[..., :3].astype(int) - idle[..., :3].astype(int)).sum(2) > 30, 1) if False else None
                row0 = stock[0:CELL, c * CELL:(c + 1) * CELL]
                dm = np.abs(cell[..., :3].astype(int) - row0[..., :3].astype(int)).sum(2) > 30
                fire = dm & (s > 0.65) & (v > 0.7) & (h < 31)
                arm &= ~grow(fire, 1)
            new = ramp(name, lum)
            if name == "Night":
                dark = al & (lum < 55)
                dark_ul = np.zeros_like(dark)
                dark_ul[1:, 1:] = dark[:-1, :-1]                           # up-left neighbour is a seam: lit plate edge
                rim = arm & dark_ul & (lum > 95)
                new[rim] = new[rim] * .45 + np.array(hx("#6EF2EE")) * .55
            res = cell.copy()
            res[arm, :3] = np.clip(new[arm] + .5, 0, 255).astype(np.uint8)
            if name == "Night":
                # flippers (cool blue-grey, outside the shell) go darker
                fl = al & ~arm & (h > 190) & (h < 260) & ((xx < 66) | (xx > 190)) & (v < 0.75)
                res[fl, :3] = (res[fl, :3] * .72).astype(np.uint8)
            out[reg] = res
    return out


def main():
    stock = np.array(Image.open(os.path.join(S, "Turtle.png")).convert("RGBA"))
    for name in ("Night", "Sodium", "Bone"):
        out = remap(stock, name)
        assert (out[..., 3] == stock[..., 3]).all()
        buf = io.BytesIO()
        Image.fromarray(out).save(buf, "PNG", optimize=True)
        open(os.path.join(S, f"Turtle_{name}.bytes"), "wb").write(buf.getvalue())
        print(name, "written")


main()
