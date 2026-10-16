#!/usr/bin/env python3
"""Assemble the vetted Codex art (src/<name>.png master, src/<name>_a.png / _b.png wing-pose variants) into seven-cell strips.

Cell contract (EnemyFlipbook / EnemyArt): 0-3 idle loop (held 6,3,2,3 ticks), 4 tell (wind-up), 5 action (release), 6 hit flash.
Craft idle loop = a REAL wing-beat drawn by Codex:  0 master (wings out)  1 mid-stroke (A)  2 top of stroke (B)  3 mid-stroke again (A,
returning), so the loop reads out -> up -> back.  Tell 4 = wings cocked at the top of the stroke (B) with the weapon charging; action 5 =
master pose with a muzzle burst; hit 6 = bone flash with cracks.  Every pose is registered on the master by its central body band so the
fuselage never jitters; only the wings move.  Rocks: one painting, glow pulse across the idle cells (they tumble in code).
Area-downsampling (premultiplied) then hard alpha; NO outline, NO palette clamp.

    python3 build_redesign.py [name ...]       -> strips/<name>.png
"""
import math, sys
from collections import deque
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import briefs

OUT = HERE / "strips"
MARGIN = 6

# per-craft weapon spot as (dx, dy) from the cell centre for the tell ring / muzzle burst; glow colour
SPOT = {
    "verdant_chaser": ((0, 62), (255, 214, 120)),
    "verdant_fighter_1": ((0, 48), (190, 245, 255)),
    "verdant_fighter_2": ((0, 70), (225, 215, 255)),
    "verdant_fighter_3": ((0, 54), (255, 214, 120)),
    "verdant_fighter_4": ((0, 62), (255, 220, 120)),
    "verdant_big": ((0, 24), (255, 226, 130)),
}
GLOW = {   # rock glow colour (used for the tell flare)
    "verdant_rock_pod": (255, 190, 90), "verdant_rock_spore": (255, 245, 190),
    "verdant_rock_knot": (255, 226, 120), "verdant_rock_vine": (200, 255, 215),
}


def largest(mask):
    h, w = mask.shape
    seen = np.zeros((h, w), bool); best = []
    for y, x in zip(*np.where(mask)):
        if seen[y, x]:
            continue
        q = deque([(int(y), int(x))]); seen[y, x] = 1; comp = []
        while q:
            cy, cx = q.popleft(); comp.append((cy, cx))
            for ny, nx in ((cy - 1, cx), (cy + 1, cx), (cy, cx - 1), (cy, cx + 1)):
                if 0 <= ny < h and 0 <= nx < w and mask[ny, nx] and not seen[ny, nx]:
                    seen[ny, nx] = 1; q.append((ny, nx))
        if len(comp) > len(best):
            best = comp
    out = np.zeros_like(mask)
    if best:
        yy, xx = np.array(best).T; out[yy, xx] = True
    return out


def load(path, canvas=None):
    im = Image.open(path).convert("RGBA")
    if canvas and im.size != canvas:
        im = im.resize(canvas, Image.Resampling.LANCZOS)
    a = np.array(im)
    a[:, :, 3] = np.where(a[:, :, 3] > 110, 255, 0)
    # drop stray specks: keep islands near the main body (dilated union)
    keep = largest(np.asarray(Image.fromarray(a[:, :, 3]).filter(ImageFilter.MaxFilter(15))) > 0)
    a[~keep, 3] = 0
    return Image.fromarray(a, "RGBA")


def bbox(im):
    ys, xs = np.nonzero(np.asarray(im)[:, :, 3] > 0)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def area_scale(im, f):
    size = (max(1, round(im.width * f)), max(1, round(im.height * f)))
    arr = np.asarray(im).astype(np.float32)
    arr[:, :, :3] *= arr[:, :, 3:4] / 255
    pm = Image.fromarray(arr.astype(np.uint8), "RGBA")
    ch = [np.asarray(pm.getchannel(i).resize(size, Image.Resampling.BOX)).astype(np.float32) for i in range(4)]
    al = ch[3]
    rgb = np.stack([np.where(al > 0, c / np.maximum(al, 1) * 255, 0) for c in ch[:3]], axis=2)
    alpha = np.where(al >= 104, 255, 0).astype(np.uint8)
    return Image.fromarray(np.dstack((np.clip(rgb, 0, 255).astype(np.uint8), alpha)), "RGBA")


def body_centroid(im):
    """Centroid of the opaque pixels inside the central 34% band of the bbox width (the fuselage, not the wings)."""
    a = np.asarray(im)[:, :, 3] > 0
    x0, y0, x1, y1 = bbox(im)
    cx = (x0 + x1) / 2; half = (x1 - x0) * .17
    band = a.copy(); band[:, :int(cx - half)] = False; band[:, int(cx + half):] = False
    ys, xs = np.nonzero(band)
    return xs.mean(), ys.mean()


def band_mask(im, ref=None):
    """Alpha mask restricted to the central fuselage band of the master (ref) or of im itself."""
    a = np.asarray(im)[:, :, 3] > 0
    x0, y0, x1, y1 = bbox(ref if ref is not None else im)
    cx = (x0 + x1) / 2; half = (x1 - x0) * .14
    m = np.zeros_like(a); m[:, int(cx - half):int(cx + half)] = a[:, int(cx - half):int(cx + half)]
    return m


def shift(im, dx, dy):
    out = Image.new("RGBA", im.size)
    out.alpha_composite(im, (int(round(dx)), int(round(dy))))
    return out


def clip_to_cell(im):
    return im


def to_cell(master, f, cell, side):
    """Scale a full source canvas by f and centre the MASTER's bbox in the cell; returns (scaled image placed in a cell-sized canvas)."""
    s = area_scale(master, f)
    x0, y0, x1, y1 = bbox(s)
    c = Image.new("RGBA", (side, side))
    c.alpha_composite(s.crop((x0, y0, x1, y1)), ((side - (x1 - x0)) // 2, (side - (y1 - y0)) // 2))
    return c, ((side - (x1 - x0)) // 2 - x0, (side - (y1 - y0)) // 2 - y0)


def place(src, f, off, side):
    """Another pose from the same source canvas, scaled by f and offset exactly like the master."""
    s = area_scale(src, f)
    c = Image.new("RGBA", (side, side))
    c.alpha_composite(s, off) if (off[0] >= 0 and off[1] >= 0) else c.alpha_composite(s, (0, 0), (max(0, -off[0]), max(0, -off[1])))
    return c


def lighten(im, amount, tint=(255, 250, 225)):
    a = np.asarray(im).astype(np.float32).copy()
    solid = a[:, :, 3] > 0
    a[solid, :3] = a[solid, :3] * (1 - amount) + np.array(tint, np.float32) * amount
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def pulse(im, amount):
    """Brighten only the glowing parts (bright + saturated) toward white; amount may be negative (dim)."""
    a = np.asarray(im).astype(np.float32).copy()
    rgb = a[:, :, :3] / 255
    mx = rgb.max(2); mn = rgb.min(2)
    sat = (mx - mn) / np.maximum(mx, 1e-3)
    glow = (a[:, :, 3] > 0) & (mx > .72) & (sat > .28)
    w = np.where(glow, 1.0, 0.0)[:, :, None]
    if amount >= 0:
        a[:, :, :3] += w * (255 - a[:, :, :3]) * amount
    else:
        a[:, :, :3] *= 1 + w * amount
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def ring(frame, spot, col, r, strong):
    """Glow ring + core, drawn soft on its own layer (no dark ink)."""
    side = frame.width
    g = Image.new("RGBA", (side, side))
    d = ImageDraw.Draw(g)
    x, y = spot
    for k, al in ((r + 6, 40), (r + 3, 80), (r, 140)):
        d.ellipse((x - k, y - k, x + k, y + k), fill=(*col, al))
    d.ellipse((x - r // 2, y - r // 2, x + r // 2, y + r // 2), fill=(255, 255, 245, 255 if strong else 210))
    out = frame.copy(); out.alpha_composite(g)
    return out


def burst(frame, spot, col):
    side = frame.width
    g = Image.new("RGBA", (side, side)); d = ImageDraw.Draw(g)
    x, y = spot
    d.line([(x, y), (x, min(side - MARGIN - 1, y + 20))], fill=(255, 255, 240, 255), width=3)
    for k in (-1, 1):
        d.line([(x, y + 2), (x + k * 9, min(side - MARGIN - 1, y + 18))], fill=(*col, 255), width=2)
        d.line([(x, y), (x + k * 14, y + 2)], fill=(*col, 220), width=2)
    out = frame.copy(); out.alpha_composite(g)
    return out


def hit_flash(key):
    f = lighten(key, .5, (250, 246, 214))
    a = np.array(f); side = f.width
    d = ImageDraw.Draw(f)
    cx = cy = side // 2
    pts = [(cx - 20, cy - 18), (cx - 8, cy - 6), (cx - 12, cy + 4), (cx + 3, cy + 12), (cx + 12, cy + 26)]
    # cracks only where the body is: draw, then re-mask to the body alpha
    layer = Image.new("RGBA", (side, side)); ld = ImageDraw.Draw(layer)
    ld.line(pts, fill=(96, 84, 52, 255), width=2, joint="curve")
    ld.line([(x + 1, y - 1) for x, y in pts], fill=(255, 255, 240, 255), width=1, joint="curve")
    la = np.array(layer); la[:, :, 3] = np.where(a[:, :, 3] > 0, la[:, :, 3], 0)
    f.alpha_composite(Image.fromarray(la, "RGBA"))
    return f


def sparks(frame, seed, col, n=5, rad=None):
    rng = np.random.default_rng(seed)
    side = frame.width
    x0, y0, x1, y1 = bbox(frame)
    d = ImageDraw.Draw(frame)
    for _ in range(n):
        ang = rng.uniform(0, math.tau)
        r = max(x1 - x0, y1 - y0) * .5 * rng.uniform(.55, .9)
        x = side / 2 + r * math.cos(ang) * (x1 - x0) / max(x1 - x0, y1 - y0)
        y = side / 2 + r * math.sin(ang) * (y1 - y0) / max(x1 - x0, y1 - y0)
        x = min(max(x, MARGIN + 3), side - MARGIN - 4); y = min(max(y, MARGIN + 3), side - MARGIN - 4)
        d.rectangle((x - 1, y - 1, x + 1, y + 1), fill=(*col, 255))
        d.point((x, y), fill=(255, 255, 245, 255))
    return frame


def build(name):
    spec = briefs.BRIEFS[name]
    side = spec["cell"]
    m = load(HERE / "src" / f"{name}.png")
    canvas = m.size
    f = spec["size"] / max(bbox(m)[2] - bbox(m)[0], bbox(m)[3] - bbox(m)[1])
    key, off = to_cell(m, f, spec["cell"], side)
    if spec["kind"] == "rock":
        gcol = GLOW[name]
        idle = [pulse(key, v) for v in (0.0, .26, .5, .16)]
        t4 = ring_glow = sparks(pulse(key, .45), 4, gcol, 6)
        t5 = sparks(pulse(key, .6), 5, gcol, 9)
        return assemble(name, idle + [t4, t5, hit_flash(key)], side)
    va = load(HERE / "src" / f"{name}_a.png", canvas)
    vb = load(HERE / "src" / f"{name}_b.png", canvas)
    mc = body_centroid(key)
    bm = band_mask(key)
    poses = []
    for v in (va, vb):
        best = None
        for sc in np.arange(.84, 1.17, .01):          # Codex redraws the craft a little larger / smaller: fit the fuselage band
            p = place(v, f * sc, (int(off[0] * sc), int(off[1] * sc)), side)
            c = body_centroid(p)
            p = shift(p, mc[0] - c[0], mc[1] - c[1])
            iou = (bm & band_mask(p, key)).sum() / max(1, (bm | band_mask(p, key)).sum())
            if best is None or iou > best[0]:
                best = (iou, sc, p)
        # a wing-beat moves the bbox centre (wing tips, flames) more than the body: meet it halfway so the ship bobs a little with the stroke
        kb = bbox(key); pb = bbox(best[2]); dyb = ((pb[1] + pb[3]) - (kb[1] + kb[3])) / 2
        if abs(dyb) > 3:
            best = (best[0], best[1], shift(best[2], 0, -.45 * dyb))
        print(f"   {name}: pose fit scale {best[1]:.2f} band IoU {best[0]:.2f}")
        poses.append(best[2])
    A, B = poses
    (dx, dy), col = SPOT[name]
    spot = (side // 2 + dx, side // 2 + dy)
    idle = [key, A, B, A]
    t4 = ring(lighten(B, .06), spot, col, 7, False)
    t5 = burst(ring(pulse(key, .25), spot, col, 11, True), spot, col)
    return assemble(name, idle + [t4, t5, hit_flash(key)], side)


def assemble(name, frames, side):
    strip = Image.new("RGBA", (side * 7, side))
    for i, fr in enumerate(frames):
        a = np.array(fr); a[:, :, 3] = np.where(a[:, :, 3] > 40, 255, 0); a[a[:, :, 3] == 0, :3] = 0
        strip.alpha_composite(Image.fromarray(a, "RGBA"), (i * side, 0))
    return strip


def check_framing(name, strip, tumbling):
    h = strip.height
    a = np.asarray(strip.getchannel("A")) > 8
    boxes = []
    for c in range(strip.width // h):
        ys, xs = np.where(a[:, c * h:(c + 1) * h])
        assert len(xs), f"{name}: cell {c} empty"
        box = (xs.min(), ys.min(), xs.max(), ys.max()); boxes.append(box)
        m = min(box[0], box[1], h - 1 - box[2], h - 1 - box[3])
        assert m >= MARGIN, f"{name}: cell {c} within {m} px of the cell edge (bbox {box})"
    if tumbling:
        return
    b = np.array(boxes)
    cx = (b[:, 0] + b[:, 2]) / 2; cy = (b[:, 1] + b[:, 3]) / 2
    print(f"   {name}: idle pivot dx {np.ptp(cx[:4]):.1f} dy {np.ptp(cy[:4]):.1f}  size {np.ptp(b[:4, 2] - b[:4, 0])}x{np.ptp(b[:4, 3] - b[:4, 1])}  all-cell pivot {np.ptp(cx):.1f}/{np.ptp(cy):.1f}")


def main():
    OUT.mkdir(exist_ok=True)
    names = sys.argv[1:] or [n for n in briefs.BRIEFS if (HERE / "src" / f"{n}.png").exists()]
    for n in names:
        s = build(n)
        check_framing(n, s, "_rock_" in n)
        s.save(OUT / f"{n}.png", optimize=True)
        print(n, s.size)


if __name__ == "__main__":
    main()
