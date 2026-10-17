import numpy as np
from fxlib import *
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'

HALO = (40, 200, 90, 150)
MID = (120, 255, 150, 235)
CORE = (225, 255, 225, 255)

def line(p, q):
    (x0, y0), (x1, y1) = p, q
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    return [(int(round(x0 + (x1 - x0) * t / max(1, n - 1))), int(round(y0 + (y1 - y0) * t / max(1, n - 1)))) for t in range(n)]

def bolt_points(rs, a, b, rough, depth):
    pts = [a, b]
    for d in range(depth):
        new = [pts[0]]
        for i in range(len(pts) - 1):
            (x0, y0), (x1, y1) = pts[i], pts[i + 1]
            mx, my = (x0 + x1) / 2, (y0 + y1) / 2
            dx, dy = x1 - x0, y1 - y0
            L = max(1.0, (dx * dx + dy * dy) ** .5)
            off = (rs.rand() - .5) * rough * L
            new.append((mx - dy / L * off, my + dx / L * off))
            new.append(pts[i + 1])
        pts = new
        rough *= .85
    return pts

def stamp(img, x, y, r, col):
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r + 1:
                yy, xx = y + dy, x + dx
                if 0 <= yy < C and 0 <= xx < C:
                    cur = img[yy, xx]
                    if col[3] >= cur[3]:
                        img[yy, xx] = col

def draw_path(img, pts, widths):
    # widths: halo / mid / core radii
    path = []
    for i in range(len(pts) - 1):
        path += line(pts[i], pts[i + 1])
    for r, col in ((widths[0], HALO), (widths[1], MID), (widths[2], CORE)):
        if r < 0:
            continue
        for (x, y) in path:
            stamp(img, x, y, r, col)

def spark(img, x, y, s):
    for d in range(-s, s + 1):
        for (xx, yy) in ((x + d, y), (x, y + d)):
            if 0 <= yy < C and 0 <= xx < C:
                img[yy, xx] = CORE if abs(d) <= s // 2 else MID

def make_bolt(img, rs, a, b, thick=1):
    pts = bolt_points(rs, a, b, 1.05, 4)
    draw_path(img, pts, (thick + 1, thick, max(0, thick - 1)))
    # side branches
    for i in rs.choice(np.arange(4, len(pts) - 4), size=3, replace=False):
        x, y = pts[i]
        ang = rs.rand() * 2 * np.pi
        L = 10 + rs.rand() * 22
        e = (x + np.cos(ang) * L, y + np.sin(ang) * L)
        bp = bolt_points(rs, (x, y), e, 1.0, 3)
        draw_path(img, bp, (2, 1, 0))
    spark(img, int(a[0]), int(a[1]), 4)
    spark(img, int(b[0]), int(b[1]), 5)

def cell_arcs(k):
    rs = np.random.RandomState(100 + k)
    img = np.zeros((C, C, 4), np.uint8)
    j = lambda v, s: v + (rs.rand() - .5) * 2 * s
    make_bolt(img, rs, (j(172, 8), j(74, 6)), (j(96, 10), j(196, 8)), 2)
    make_bolt(img, rs, (j(282, 8), j(100, 6)), (j(316, 8), j(206, 8)), 2)
    if k % 2 == 0:
        make_bolt(img, rs, (j(205, 8), j(66, 4)), (j(240, 8), j(126, 8)), 2)
    return img

def build():
    fx = load(W + 'orig/Verdant_damage_fx.png')
    out = fx.copy()
    for c in range(6):
        out[C:2 * C, c * C:(c + 1) * C] = cell_arcs(c)
    return out

if __name__ == '__main__':
    o = build()
    save(o, W + 'out/Verdant_arcs.png')
    for c in range(6):
        print(c, longest_straight(cell_of(o, 1, c)[:, :, 3]))
