#!/usr/bin/env python3
"""Find blinking-light anchors on Space's station cells (fx.png) and emit the
C# table SpaceStationLights.Anchors.

Usage: station_lights.py <fx.png> <fx.json> <out.cs> <debug_dir>
       station_lights.py --standalone <sprite.png> <name> <out.cs> <debug_dir>

--standalone: a station drawn as its own high-resolution sprite (Codex's
station_ring_v2.png, 1254 px). It is analysed at atlas-cell scale (about
240 px wide, the same thresholds as the fx cells) and the anchors are mapped
back to the sprite's own pixels, each snapped to the nearest solid pixel
there. Both are 100 px per unit, so the table needs no other scale.

Anchors are atlas-pixel offsets from the cell's centre (x right, y up), each
on an opaque pixel of the cell:
  * tower tips   - local peaks of the top silhouette (spires), beacons
  * keel tips    - local troughs of the bottom silhouette (hanging antennas)
  * girder ends  - the leftmost / rightmost solid points
  * windows      - spread-out clusters of the art's bright magenta window grid
  * cyan rim     - a couple of the brightest blue/cyan rim pixels
"""
import json, sys, random
from PIL import Image

CELLS = ["station_00", "station_01", "station_02", "station_03",
         "ringstation_00", "ringstation_01", "ringstation_02", "ringstation_03", "moon"]

# Patterns (BackdropStationLights.shader): 0 pulse 1.2 s, 1 double blink,
# 2 strobe (long on, short off), 3 window flicker, 4 beacon flash.
PULSE, DOUBLE, STROBE, FLICKER, BEACON = range(5)
# Colours (SpaceStationLights.Colors): 0 magenta, 1 pink, 2 cyan, 3 white, 4 amber.
MAGENTA, PINK, CYAN, WHITE, AMBER = range(5)


def load(fx_png, fx_json):
    im = Image.open(fx_png).convert("RGBA")
    rects = {r["n"]: r for r in json.load(open(fx_json))["sprites"]}
    return im, rects


def cell_pixels(im, r):
    H = im.height
    x0, y0 = r["x"], H - r["y"] - r["h"]          # json y is bottom-up
    return im.crop((x0, y0, x0 + r["w"], y0 + r["h"]))


def analyse(cell, name, rng):
    w, h = cell.size
    px = cell.load()
    opaque = [[px[x, y][3] >= 200 for x in range(w)] for y in range(h)]

    def solid(x, y, rad=1):
        # opaque with opaque neighbours: the light sits on the body, not a fringe
        for dy in range(-rad, rad + 1):
            for dx in range(-rad, rad + 1):
                xx, yy = x + dx, y + dy
                if not (0 <= xx < w and 0 <= yy < h) or not opaque[yy][xx]:
                    return False
        return True

    def mag(x, y):
        r, g, b, a = px[x, y]
        return a >= 200 and r > 120 and b > 120 and g < 0.55 * min(r, b)

    def cyan(x, y):
        r, g, b, a = px[x, y]
        return a >= 200 and b > 170 and g > 110 and r < 0.55 * b

    anchors = []      # (x, y, kind)

    def far_enough(x, y, d=9):
        return all((x - ax) ** 2 + (y - ay) ** 2 >= d * d for ax, ay, _ in anchors)

    # --- tower tips: peaks of the top silhouette
    top = [next((y for y in range(h) if opaque[y][x]), None) for x in range(w)]
    peaks = []
    for x in range(w):
        t = top[x]
        if t is None:
            continue
        win = [top[i] for i in range(max(0, x - 7), min(w, x + 8)) if top[i] is not None]
        if t > min(win):
            continue
        around = [top[i] for i in range(max(0, x - 22), min(w, x + 23)) if top[i] is not None]
        around.sort()
        prom = around[len(around) // 2] - t
        if prom < 10:
            continue
        # walk down the spire to the first solid pixel
        for y in range(t, min(h, t + 12)):
            if solid(x, y, 0) and opaque[min(h - 1, y + 1)][x]:
                peaks.append((t, prom, x, y + 1))
                break
    peaks.sort()
    for t, prom, x, y in peaks:
        if len([a for a in anchors if a[2] == "tip"]) >= 3:
            break
        if far_enough(x, y, 16):
            anchors.append((x, y, "tip"))

    # --- keel tips: troughs of the bottom silhouette (antennas hanging down)
    bot = [next((y for y in range(h - 1, -1, -1) if opaque[y][x]), None) for x in range(w)]
    troughs = []
    for x in range(w):
        b = bot[x]
        if b is None:
            continue
        win = [bot[i] for i in range(max(0, x - 7), min(w, x + 8)) if bot[i] is not None]
        if b < max(win):
            continue
        around = sorted(bot[i] for i in range(max(0, x - 22), min(w, x + 23)) if bot[i] is not None)
        prom = b - around[len(around) // 2]
        if prom < 12:
            continue
        for y in range(b, max(-1, b - 12), -1):
            if solid(x, y, 0) and opaque[max(0, y - 1)][x]:
                troughs.append((-b, prom, x, y - 1))
                break
    troughs.sort()
    for _, prom, x, y in troughs:
        if len([a for a in anchors if a[2] == "keel"]) >= 2:
            break
        if far_enough(x, y, 16):
            anchors.append((x, y, "keel"))

    # --- girder ends: leftmost / rightmost solid points
    cols = [(x, y) for y in range(h) for x in range(w) if solid(x, y, 1)]
    if cols:
        lx, ly = min(cols, key=lambda p: (p[0], abs(p[1] - h / 2)))
        rx, ry = max(cols, key=lambda p: (p[0], -abs(p[1] - h / 2)))
        for x, y in ((lx + 2, ly), (rx - 2, ry)):
            if opaque[y][x] and far_enough(x, y, 12):
                anchors.append((x, y, "end"))

    # --- windows: magenta grid, in 14 px blocks, spread out
    B = 14
    blocks = []
    for by in range(0, h, B):
        for bx in range(0, w, B):
            pts = [(x, y) for y in range(by, min(h, by + B)) for x in range(bx, min(w, bx + B)) if mag(x, y)]
            if len(pts) >= 6:
                cx = sum(p[0] for p in pts) / len(pts)
                cy = sum(p[1] for p in pts) / len(pts)
                best = min(pts, key=lambda p: (p[0] - cx) ** 2 + (p[1] - cy) ** 2)
                blocks.append((len(pts), best))
    want_windows = 7 if not name.startswith("moon") else 6
    # farthest-point sampling, seeded by the densest block
    blocks.sort(reverse=True)
    picked = 0
    cand = [b[1] for b in blocks]
    while cand and picked < want_windows:
        if picked == 0:
            p = cand[0]
        else:
            p = max(cand, key=lambda c: min((c[0] - a[0]) ** 2 + (c[1] - a[1]) ** 2 for a in anchors))
        cand.remove(p)
        if far_enough(p[0], p[1], 10):
            anchors.append((p[0], p[1], "window"))
            picked += 1

    # --- cyan rim: two bright cyan pixels, far from everything
    cy_pts = [(x, y) for y in range(h) for x in range(w) if cyan(x, y) and solid(x, y, 0)]
    for _ in range(2):
        if not cy_pts:
            break
        p = max(cy_pts, key=lambda c: min([(c[0] - a[0]) ** 2 + (c[1] - a[1]) ** 2 for a in anchors] or [1e9]))
        cy_pts.remove(p)
        if far_enough(p[0], p[1], 12):
            anchors.append((p[0], p[1], "cyan"))

    # pattern / colour per kind
    out = []
    tips = 0
    for x, y, kind in anchors:
        seed = round(rng.random(), 3)
        if kind == "tip":
            col = WHITE if tips == 0 else AMBER
            pat = BEACON
            tips += 1
        elif kind == "keel":
            col, pat = AMBER, PULSE
        elif kind == "end":
            col, pat = (WHITE, DOUBLE) if x < w / 2 else (AMBER, DOUBLE)
        elif kind == "window":
            col = rng.choice([MAGENTA, MAGENTA, PINK])
            pat = rng.choice([FLICKER, FLICKER, PULSE])
        else:
            col, pat = CYAN, STROBE
        dx = x + 0.5 - w / 2.0
        dy = h / 2.0 - (y + 0.5)
        out.append((dx, dy, pat, col, seed, kind, x, y))
    return out


STANDALONE_CELL_PX = 240


def standalone(png, name, out_cs, dbg):
    full = Image.open(png).convert("RGBA")
    k = full.width / float(STANDALONE_CELL_PX)
    small = full.resize((STANDALONE_CELL_PX, round(full.height / k)), Image.LANCZOS)
    lights = analyse(small, name, random.Random(2001))
    fpx = full.load()
    W, H = full.size

    def solid(x, y):
        return all(0 <= x + dx < W and 0 <= y + dy < H and fpx[x + dx, y + dy][3] >= 200
                   for dy in (-1, 0, 1) for dx in (-1, 0, 1))

    out = []
    for dx, dy, pat, col, seed, kind, x, y in lights:
        fx_, fy_ = int((x + 0.5) * k), int((y + 0.5) * k)
        best = None
        for r in range(0, int(k * 2) + 2):
            for yy in range(fy_ - r, fy_ + r + 1):
                for xx in range(fx_ - r, fx_ + r + 1):
                    if max(abs(xx - fx_), abs(yy - fy_)) == r and solid(xx, yy):
                        best = (xx, yy)
                        break
                if best:
                    break
            if best:
                break
        if best is None:
            continue
        bx, by = best
        out.append((bx + 0.5 - W / 2.0, H / 2.0 - (by + 0.5), pat, col, seed, kind, bx, by))
    body = ", ".join("L({:.1f}f, {:.1f}f, {}, {}, {:.3f}f)".format(dx, dy, pat, col, seed)
                     for dx, dy, pat, col, seed, *_ in out)
    with open(out_cs, "w") as f:
        f.write('        S("%s", new[] { %s }),\n' % (name, body))
    from PIL import ImageDraw
    bg = Image.new("RGBA", full.size, (12, 14, 30, 255))
    bg.alpha_composite(full)
    d = ImageDraw.Draw(bg)
    colors = {"tip": (255, 255, 255), "keel": (255, 180, 0), "end": (255, 120, 0),
              "window": (0, 255, 0), "cyan": (0, 255, 255)}
    for *_, kind, x, y in out:
        d.rectangle((x - 10, y - 10, x + 10, y + 10), outline=colors[kind], width=4)
    bg.save("%s/anchors_%s.png" % (dbg, name))
    print(name, len(out), {kd: sum(1 for l in out if l[5] == kd) for kd in
                           ("tip", "keel", "end", "window", "cyan")})


def main():
    if sys.argv[1] == "--standalone":
        standalone(*sys.argv[2:6])
        return
    fx_png, fx_json, out_cs, dbg = sys.argv[1:5]
    im, rects = load(fx_png, fx_json)
    rng = random.Random(1988)
    lines = []
    summary = []
    for name in CELLS:
        r = rects[name]
        cell = cell_pixels(im, r)
        lights = analyse(cell, name, rng)
        summary.append((name, len(lights), {k: sum(1 for l in lights if l[5] == k) for k in
                                           ("tip", "keel", "end", "window", "cyan")}))
        body = ", ".join("L({:.1f}f, {:.1f}f, {}, {}, {:.3f}f)".format(dx, dy, pat, col, seed)
                         for dx, dy, pat, col, seed, *_ in lights)
        lines.append('        { "%s", new[] { %s } },' % (name, body))
        # debug overlay, 4x
        bg = Image.new("RGBA", cell.size, (12, 14, 30, 255))
        bg.alpha_composite(cell)
        dbgim = bg.resize((cell.width * 4, cell.height * 4), Image.NEAREST)
        from PIL import ImageDraw
        d = ImageDraw.Draw(dbgim)
        colors = {"tip": (255, 255, 255), "keel": (255, 180, 0), "end": (255, 120, 0),
                  "window": (0, 255, 0), "cyan": (0, 255, 255)}
        for *_, kind, x, y in lights:
            d.rectangle((x * 4 - 3, y * 4 - 3, x * 4 + 6, y * 4 + 6), outline=colors[kind], width=2)
        dbgim.save("%s/anchors_%s.png" % (dbg, name))
    with open(out_cs, "w") as f:
        f.write("\n".join(lines) + "\n")
    for s in summary:
        print(s)


if __name__ == "__main__":
    main()
