"""Shellback = the special CHEVRON livery skin, composited explicitly (Codex painted dark patches instead of V shapes).
Run AFTER build_restyle.py Turtle: reads the final stock sheet Turtle.png, draws crisp slate "^" chevrons on the idle
cell, moves them with every cell (bob = shift, bank = the same affine map as the damage layer) and hides them where the
damage layer of that cell lies (so the chevrons look cracked / missing under damage). Writes Turtle_Shellback.bytes.
Chevron geometry (256 cell): three centre chevrons apex y 150 / 172 / 194, half width 46 / 38 / 30; flank chevrons at
x 84 and 172, apex y 96, half width 11. Slate #2C2D40, top edge #5A5C78, lower edge #1A1A28."""
import io
import os
import sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SHIP = os.path.join(HERE, "..", "..", "..")
CELL = 256
BODY, HI, LO = (0x2C, 0x2D, 0x40), (0x5A, 0x5C, 0x78), (0x1A, 0x1A, 0x28)


def chevron(dr, cx, y0, hw, d, t):
    pts = [(cx, y0), (cx + hw, y0 + d), (cx + hw, y0 + d + t), (cx, y0 + t), (cx - hw, y0 + d + t), (cx - hw, y0 + d)]
    dr.polygon(pts, fill=BODY + (255,))
    # crisp 1 px edges: lighter on the two top arms, darker along the two lower arms
    dr.line([(cx - hw, y0 + d), (cx, y0), (cx + hw, y0 + d)], fill=HI + (255,), width=1)
    dr.line([(cx - hw, y0 + d + t), (cx, y0 + t), (cx + hw, y0 + d + t)], fill=LO + (255,), width=1)


def layer():
    im = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    dr = ImageDraw.Draw(im)
    for y0, hw in ((150, 46), (172, 38), (194, 30)):
        chevron(dr, 128, y0, hw, int(hw * 0.6), 8)
    chevron(dr, 84, 96, 11, 7, 5)
    chevron(dr, 172, 96, 11, 7, 5)
    return np.array(im)


CREAM, CYAN = (0xF4, 0xEA, 0xD4), (0x6E, 0xF2, 0xEE)
RR = 12                                       # rotor radius (cell px)


def flipper_hubs(stock_alpha):
    """hub = the outer end of each of the four flippers (stock idle cell), pulled 9 px in along the flipper."""
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    out = []
    for m in ((xx < 65) & (yy < 128), (xx > 191) & (yy < 128), (xx < 65) & (yy >= 128), (xx > 191) & (yy >= 128)):
        ys, xs = np.nonzero(stock_alpha & m)
        d = (xs - 128) ** 2 + (ys - 128) ** 2
        i = d.argmax()
        px, py = xs[i], ys[i]
        near = (xs - px) ** 2 + (ys - py) ** 2 < 11 ** 2
        out.append((xs[near].mean(), ys[near].mean(), px, py))
    return out


def rotor_layer(stock_alpha):
    """a neat round metal HUB CAP (slate rim, steel face, bright cyan dot) at the outer end of each flipper, inside the
    flipper silhouette. The spinning blades are drawn by code, anchored at these hub centres (hubs.json)."""
    base = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    dr = ImageDraw.Draw(base)
    hubs = flipper_hubs(stock_alpha)
    out = {}
    for name, (hx, hy, px, py) in zip(("upper_left", "upper_right", "lower_left", "lower_right"), hubs):
        hx, hy = round(hx - .5) + .5, round(hy - .5) + .5          # pixel-centre aligned
        out[name] = [hx, hy]
        dr.ellipse([hx - 6, hy - 6, hx + 5, hy + 5], fill=LO + (255,))
        dr.ellipse([hx - 5, hy - 5, hx + 4, hy + 4], fill=HI + (255,))
        dr.ellipse([hx - 3, hy - 3, hx + 2, hy + 2], fill=(0x8A, 0x8D, 0xA8, 255))
        dr.rectangle([hx - 1, hy - 1, hx, hy], fill=CYAN + (255,))
    import json
    json.dump({"cell": CELL, "note": "hub centres in the 256 px idle cell (x right, y down); rotate blades here",
               "hubs": out}, open(os.path.join(HERE, "hubs.json"), "w"), indent=1)
    return np.array(base)


def extent(al, band=None):
    rows = al if band is None else al[band[0]:band[1]]
    xs = np.nonzero(rows.any(axis=0))[0]
    return xs.min(), xs.max() + 1


def vext(al):
    ys = np.nonzero(al.any(axis=1))[0]
    return ys.min(), ys.max() + 1


def main():
    stock = np.array(Image.open(os.path.join(SHIP, "Turtle.png")).convert("RGBA"))
    out = stock.copy()
    lay = layer()
    rlay = rotor_layer(stock[:CELL, :CELL, 3] > 0)
    _y, _x = np.mgrid[0:CELL, 0:CELL]
    flip_idle = (stock[:CELL, :CELL, 3] > 0) & ((_x < 66) | (_x > 190))
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
        RL = rlay[yi, xi]
        FM = flip_idle[yi, xi]
        for r in range(3):
            reg = (slice(r * CELL, (r + 1) * CELL), slice(c * CELL, (c + 1) * CELL))
            cell = stock[reg].copy()
            use = (L[..., 3] > 0) & ac
            # shell interior only: keep 4 px away from the silhouette edge
            inner = ac.copy()
            for _ in range(4):
                n = inner.copy()
                n[1:] &= inner[:-1]; n[:-1] &= inner[1:]; n[:, 1:] &= inner[:, :-1]; n[:, :-1] &= inner[:, 1:]
                inner = n
            use &= inner
            if r:
                dm = np.abs(cell[..., :3].astype(int) - ci[..., :3].astype(int)).sum(2) > 30
                n = dm.copy()
                n[1:] |= dm[:-1]; n[:-1] |= dm[1:]; n[:, 1:] |= dm[:, :-1]; n[:, :-1] |= dm[:, 1:]
                use &= ~n
                dmg = n
            cell[use, :3] = L[use][:, :3]
            # rotors: only on the flipper silhouette (1 px in from its edge), alpha-blended, hidden under damage
            edge = ac.copy()
            n = edge.copy()
            n[1:] &= edge[:-1]; n[:-1] &= edge[1:]; n[:, 1:] &= edge[:, :-1]; n[:, :-1] &= edge[:, 1:]
            ru = (RL[..., 3] > 0) & n & FM
            if r:
                ru &= ~dmg
            al_ = (RL[..., 3][ru].astype(float) / 255)[:, None]
            cell[ru, :3] = (cell[ru, :3] * (1 - al_) + RL[ru][:, :3] * al_ + .5).astype(np.uint8)
            out[reg] = cell
    buf = io.BytesIO()
    Image.fromarray(out).save(buf, "PNG", optimize=True)
    open(os.path.join(SHIP, "Turtle_Shellback.bytes"), "wb").write(buf.getvalue())
    assert (out[..., 3] == stock[..., 3]).all()
    print("Shellback written")


main()
