#!/usr/bin/env python3
"""Measures where each boss attack leaves the boss's body, frame by frame.

  python3 measure_emitters.py [--preview DIR]      (needs Pillow + numpy)

Reads the body atlases Codex renders into Art/Resources/Bosses/<World>.png
(5 x 8 cells of 384 px; see BossArt.cs) and writes

  Scripts/Bosses/BossEmitterTable.cs   (generated -- do not edit by hand)

Every emitter is a named body part (the Archon's chin cannon, the
Leviathan's eyes, the Bloom Queen's petal tips, the Drake's jaw ...). It is
seeded once, by hand, as a small patch of the drawing in one reference
frame plus an offset from that patch to the exact muzzle pixel. For every
drawing the boss can fire or tell from (idle 0..3, hit, tell0..2 a/b, fire)
the patch is found again by template matching -- a small search over
position and uniform scale, because the tell poses squash, lean and grow
the whole boss -- and the muzzle is carried along. The result is snapped
onto the nearest opaque pixel, so a shot can never start in empty air.

So when the art is redrawn, re-running this script moves every muzzle with
it; BossAttackTest then checks that each one still lands on the boss.

--preview DIR also writes one overlay sheet per boss (every frame, every
emitter marked) for review.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
ATLAS = os.path.join(ASSETS, "Art", "Resources", "Bosses")
OUT = os.path.join(ASSETS, "Scripts", "Bosses", "BossEmitterTable.cs")

CELL = 384
COLS = 5
# The drawings an attack can be told or fired from: idle 0..3, hit,
# tell0 a/b, tell1 a/b, fire, tell2 a/b (BossArt flat indices 0..11).
# The Archon's later combat poses map back to these measured anchors in
# BossEmitters.TableFrame.
FRAMES = list(range(12))
FRAME_NAMES = ["idle0", "idle1", "idle2", "idle3", "hit", "tell0a", "tell0b",
               "tell1a", "tell1b", "fire", "tell2a", "tell2b"]

# name, reference frame, patch centre (cell px, y down), patch half size,
# muzzle offset from the patch centre (cell px at the reference frame).
PARTS = {
    "Space": [
        ("Chin", 0, (192, 284), 22, (0, 40)),      # chin cannon barrel -> its muzzle
        ("Core", 0, (192, 226), 30, (0, 6)),       # the reactor core behind the chest hatch
        ("PodL", 0, (52, 250), 22, (-2, 56)),      # left engine pod -> its nozzle
        ("PodR", 0, (334, 250), 22, (2, 56)),      # right engine pod -> its nozzle
    ],
    "Frost": [
        ("Jaw", 0, (218, 300), 34, (0, 14)),       # the icicle jaw
        ("EyeL", 0, (155, 236), 20, (0, 0)),
        ("EyeR", 0, (281, 236), 20, (0, 0)),
        ("Crown", 0, (218, 116), 22, (0, -26)),    # the blowhole crown -> its vent
    ],
    "Verdant": [
        ("Stinger", 0, (190, 262), 20, (0, 40)),   # brass stinger -> its tip
        ("PetalUL", 8, (120, 70), 18, (0, 0)),     # the six petal tips that spark (tell1)
        ("PetalUR", 8, (274, 70), 18, (0, 0)),
        ("PetalL", 8, (70, 145), 18, (0, 0)),
        ("PetalR", 8, (326, 145), 18, (0, 0)),
        ("PetalLL", 0, (80, 296), 16, (0, 0)),     # lower petal claws
        ("PetalLR", 0, (304, 296), 16, (0, 0)),
        ("CannonL", 0, (106, 262), 22, (-14, 10)), # acid cannons -> their mouths
        ("CannonR", 0, (276, 262), 22, (14, 10)),
        ("Bulb", 0, (190, 180), 30, (0, 0)),       # the seed bulb (radial centre for spores)
    ],
    "Ember": [
        ("Jaw", 0, (192, 196), 26, (0, 8)),        # the burning maw
        ("Furnace", 0, (192, 250), 22, (0, 0)),    # the chest furnace
        ("Brow", 0, (192, 140), 22, (0, 0)),       # the brow gem between the eyes
    ],
}

# Rows (cell px) the whole-body registration compares: the body, clear of
# the smoke stacks, the crown flame and the muzzle bursts below the chin.
# (y0, y1, x0, x1): the Drake's wings flap between poses, so only its head
# and neck are compared.
BAND = {"Space": (96, 282, 0, 384), "Frost": (112, 292, 0, 384),
        "Verdant": (40, 262, 0, 384), "Ember": (40, 236, 112, 272)}

SCALES = [0.86 + 0.02 * i for i in range(18)]   # 0.86 .. 1.20


def cells(world):
    im = Image.open(os.path.join(ATLAS, world + ".png")).convert("RGBA")
    out = []
    for f in range(12):
        r, c = divmod(f, COLS)
        out.append(im.crop((c * CELL, r * CELL, c * CELL + CELL, r * CELL + CELL)))
    return out


def premul(img):
    a = np.asarray(img, dtype=np.float32) / 255.0
    rgb = a[:, :, :3] * a[:, :, 3:4]
    return np.concatenate([rgb, a[:, :, 3:4] * 1.5], axis=2)   # alpha (silhouette) weighs more


def silhouette(img, k=4):
    """Alpha mask, downsampled k times (registration works on the outline)."""
    small = img.resize((CELL // k, CELL // k), Image.BILINEAR)
    return np.asarray(small, dtype=np.float32)[:, :, 3] / 255.0


def register(ref, target, band):
    """Global (scale, dx, dy) taking `ref` cell px to `target` cell px:
    p' = (p - c) * s + c + d, c the cell centre. Coarse, on the silhouette;
    the tell poses mostly grow / shift the whole boss."""
    k = 4
    tgt = silhouette(target, k)
    n = CELL // k
    b0, b1, c0, c1 = band[0] // k, band[1] // k, band[2] // k, band[3] // k
    best = None
    for s in SCALES:
        size = int(round(n * s))
        r = np.asarray(ref.resize((size * k, size * k), Image.BILINEAR).resize((size, size), Image.BILINEAR),
                       dtype=np.float32)[:, :, 3] / 255.0
        off = (n - size) / 2.0
        for dy in range(-10, 11):
            for dx in range(-10, 11):
                ox, oy = int(round(off + dx)), int(round(off + dy))
                canvas = np.zeros((n, n), np.float32)
                x0, y0 = max(0, ox), max(0, oy)
                x1, y1 = min(n, ox + size), min(n, oy + size)
                if x0 >= x1 or y0 >= y1:
                    continue
                canvas[y0:y1, x0:x1] = r[y0 - oy:y1 - oy, x0 - ox:x1 - ox]
                # Only a band of rows clear of the tells' bursts and flames
                # (BAND, per boss) is compared, so a muzzle flash never drags
                # the registration.
                err = float(np.abs(canvas[b0:b1, c0:c1] - tgt[b0:b1, c0:c1]).sum())
                if best is None or err < best[0]:
                    best = (err, s, (ox - off) * k, (oy - off) * k)
    return best[1], best[2], best[3]


def match(ref, target, seed, half, reg):
    """Where the patch at `seed` in `ref` sits in `target`: the global
    registration's guess, refined locally on colour + alpha (+/- 8 px)."""
    s, gdx, gdy = reg
    px = (seed[0] - CELL / 2) * s + CELL / 2 + gdx
    py = (seed[1] - CELL / 2) * s + CELL / 2 + gdy
    size = int(round(CELL * s))
    scaled = ref.resize((size, size), Image.BILINEAR)
    sx = (seed[0] - CELL / 2) * s + size / 2
    sy = (seed[1] - CELL / 2) * s + size / 2
    x0, y0 = int(round(sx)) - half, int(round(sy)) - half
    if x0 < 0 or y0 < 0 or x0 + 2 * half + 1 > size or y0 + 2 * half + 1 > size:
        return px, py, s
    tpl = premul(scaled)[y0:y0 + 2 * half + 1, x0:x0 + 2 * half + 1]
    tgt = premul(target)
    H, W = tgt.shape[:2]
    cx0, cy0 = int(round(px)), int(round(py))
    R = 8
    lo_x, hi_x = max(half, cx0 - R), min(W - half - 1, cx0 + R)
    lo_y, hi_y = max(half, cy0 - R), min(H - half - 1, cy0 + R)
    if lo_x > hi_x or lo_y > hi_y:
        return px, py, s
    region = tgt[lo_y - half:hi_y + half + 1, lo_x - half:hi_x + half + 1]
    win = np.lib.stride_tricks.sliding_window_view(region, (2 * half + 1, 2 * half + 1), axis=(0, 1))
    diff = win - np.transpose(tpl, (2, 0, 1))[None, None]
    ssd = np.einsum("yxchw,yxchw->yx", diff, diff)
    yy, xx = np.mgrid[lo_y:hi_y + 1, lo_x:hi_x + 1]
    ssd = ssd + 0.5 * ((xx - px) ** 2 + (yy - py) ** 2)
    iy, ix = np.unravel_index(np.argmin(ssd), ssd.shape)
    return lo_x + ix, lo_y + iy, s


def snap_opaque(img, x, y, radius=96):
    a = np.asarray(img)[:, :, 3]
    xi, yi = int(round(x)), int(round(y))
    if 0 <= xi < CELL and 0 <= yi < CELL and a[yi, xi] >= 160:
        return xi, yi
    best = None
    for dy in range(-radius, radius + 1):
        for dx in range(-radius, radius + 1):
            X, Y = xi + dx, yi + dy
            if 0 <= X < CELL and 0 <= Y < CELL and a[Y, X] >= 160:
                d = dx * dx + dy * dy
                if best is None or d < best[0]:
                    best = (d, X, Y)
    if best is None:
        raise SystemExit("no opaque pixel near (%d, %d)" % (xi, yi))
    return best[1], best[2]


def measure(world):
    frames = cells(world)
    table = []
    regs = {}
    for name, ref_f, seed, half, off in PARTS[world]:
        ref = frames[ref_f]
        pts = []
        for f in FRAMES:
            if f == ref_f:
                x, y, s = seed[0], seed[1], 1.0
            else:
                if (ref_f, f) not in regs:
                    regs[(ref_f, f)] = register(ref, frames[f], BAND[world])
                x, y, s = match(ref, frames[f], seed, half, regs[(ref_f, f)])
            mx, my = x + off[0] * s, y + off[1] * s
            pts.append(snap_opaque(frames[f], mx, my))
        table.append((name, pts))
    return frames, table


def preview(world, frames, table, out_dir):
    S = 1
    sheet = Image.new("RGBA", (CELL * 6, (CELL + 18) * 2), (16, 16, 26, 255))
    colors = [(0, 255, 120), (255, 230, 0), (0, 200, 255), (255, 120, 0), (255, 255, 255),
              (180, 120, 255), (120, 255, 255), (255, 160, 200), (160, 255, 0), (255, 80, 255)]
    for i, f in enumerate(FRAMES):
        bg = Image.new("RGBA", (CELL, CELL), (16, 16, 26, 255))
        bg.alpha_composite(frames[f])
        d = ImageDraw.Draw(bg)
        for j, (name, pts) in enumerate(table):
            x, y = pts[i]
            c = colors[j % len(colors)]
            d.ellipse([(x - 6, y - 6), (x + 6, y + 6)], outline=(0, 0, 0), width=3)
            d.line([(x - 7, y), (x + 7, y)], fill=c, width=2)
            d.line([(x, y - 7), (x, y + 7)], fill=c, width=2)
            if i == 0:
                d.text((x + 6, y - 12), name, fill=c)
        X, Y = (i % 6) * CELL, (i // 6) * (CELL + 18)
        sheet.paste(bg, (X, Y + 18))
        ImageDraw.Draw(sheet).text((X + 4, Y + 3), FRAME_NAMES[i], fill=(255, 255, 255, 255))
    sheet.save(os.path.join(out_dir, "bossatk-emitters-%s.png" % world.lower()))


def write_cs(results):
    lines = []
    w = lines.append
    w("// GENERATED by Art/BossAttacks/src~/measure_emitters.py -- do not edit by hand.")
    w("// Re-run the script after the boss art changes; BossAttackTest checks that")
    w("// every point still lands on an opaque pixel of its drawing.")
    w("//")
    w("// Muzzle points per boss, per named body part, per body drawing (BossArt")
    w("// flat frames 0..11: idle 0..3, hit, tell0 a/b, tell1 a/b, fire, tell2")
    w("// a/b), in cell pixels (384 per cell, x right, y down from the top-left).")
    w("public static class BossEmitterTable")
    w("{")
    w("    public const int CellPixels = %d;" % CELL)
    w("    public const int Frames = %d;" % len(FRAMES))
    w("")
    w("    public static readonly string[] Worlds = { %s };" % ", ".join('"%s"' % k for k in results))
    w("")
    w("    // Parts[world][part] = name; Points[world][part] = { x0, y0, x1, y1, ... } per frame.")
    w("    public static readonly string[][] Parts =")
    w("    {")
    for world, table in results.items():
        w("        new[] { %s }," % ", ".join('"%s"' % n for n, _ in table))
    w("    };")
    w("")
    w("    public static readonly short[][][] Points =")
    w("    {")
    for world, table in results.items():
        w("        new[]   // " + world)
        w("        {")
        for name, pts in table:
            flat = ", ".join("%d, %d" % p for p in pts)
            w("            new short[] { %s },   // %s" % (flat, name))
        w("        },")
    w("    };")
    w("}")
    with open(OUT, "w") as fh:
        fh.write("\n".join(lines) + "\n")


def main():
    out_dir = None
    if "--preview" in sys.argv:
        out_dir = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(out_dir, exist_ok=True)
    results = {}
    for world in ["Space", "Frost", "Verdant", "Ember"]:
        frames, table = measure(world)
        results[world] = table
        if out_dir:
            preview(world, frames, table, out_dir)
        print(world, ", ".join("%s %s" % (n, pts[9]) for n, pts in table))
    write_cs(results)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
