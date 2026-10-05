#!/usr/bin/env python3
"""Measures an elite strip's muzzle and nozzle points (and suggests its body
numbers) for its EliteDef JSON.  Needs Pillow + numpy.

  python3 measure_elite_points.py STRIP.png \
      --muzzle lance:158,18 --muzzle left:28,95:180 \
      --nozzle l:96,150 --nozzle r:122,166 \
      [--write Pause/Assets/Art/Resources/Elites/Defs/<key>.json] \
      [--preview OUT.png]

STRIP is the final strip of square cells: by default the Ember layout
(idle0..3, tell, action, hit).  For a flight layout (landed, grounded
idle, lift-off, hover, bank left, bank right, damaged -- Frost's
Rimebreaker, Verdant's Resin Warden) pass --layout flight: muzzles,
nozzles and the body numbers are then all measured on the hover cell (3),
the frame the ship flies and fires on (pick the frames yourself with
--muzzle-frame / --nozzle-frame / --body-frame).  Each seed is NAME:X,Y[:DIR] in cell pixels (x right, y down) -- a
rough click is enough, look at the zoomed cells first.  DIR (degrees, 0
right, 90 up, art space) is the way a shot / plume leaves; leave it out for
"along the nose" (muzzles) / "away from the nose" (nozzles).

  muzzles are refined on the ACTION frame (cell 5): the centroid of the
          hottest pixels (the muzzle flash / glowing tip) within RADIUS of
          the seed, then snapped onto the nearest solid pixel
  nozzles are refined on IDLE 0 (cell 0) the same way (the engine glow)
          -- flames drawn into the art are bright: seed on the engine bell,
          keep RADIUS small, and check the preview

It also prints, for the def:
  cellPixels      the cell size
  hullRadiusPx    ~ the radius holding 80% of the idle silhouette
  noseDeg         the silhouette's long axis, pointing at its sharper end
  bbox per frame  (silhouettes must stay inset: no art touching a cell edge)

--write updates `muzzles`, `nozzles` and `cellPixels` in the JSON in place
(everything else untouched).  --preview writes every cell with the points
marked (muzzles magenta, nozzles cyan) at 3x for review.
"""
import argparse
import json
import math
import sys

import numpy as np
from PIL import Image, ImageDraw

ACTION, IDLE = 5, 0


def cells(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    n = max(1, w // h)
    return [im.crop((i * h, 0, i * h + h, h)) for i in range(n)], h


def parse(seed):
    parts = seed.split(":")
    if len(parts) < 2:
        raise SystemExit("seed must be NAME:X,Y[:DIR]: " + seed)
    x, y = (float(v) for v in parts[1].split(","))
    d = float(parts[2]) if len(parts) > 2 else -1.0
    return parts[0], x, y, d


def heat(cell):
    a = np.asarray(cell, dtype=np.float32) / 255.0
    rgb, alpha = a[:, :, :3], a[:, :, 3]
    lum = rgb.max(axis=2)
    sat = lum - rgb.min(axis=2)
    return (lum * 0.7 + sat * 0.3) * (alpha > 0.5), alpha


def refine(cell, x, y, radius):
    h, alpha = heat(cell)
    H, W = alpha.shape
    yy, xx = np.mgrid[0:H, 0:W]
    near = (xx - x) ** 2 + (yy - y) ** 2 <= radius * radius
    vals = h[near]
    if vals.size and vals.max() > 0.55:
        cut = max(0.55, np.percentile(vals[vals > 0], 90))
        m = near & (h >= cut)
        if m.any():
            x, y = float(xx[m].mean()), float(yy[m].mean())
    # snap onto a solid pixel
    xi, yi = int(round(x)), int(round(y))
    if 0 <= xi < W and 0 <= yi < H and alpha[yi, xi] >= 0.63:
        return xi, yi
    solid = alpha >= 0.63
    if not solid.any():
        return xi, yi
    d = (xx - x) ** 2 + (yy - y) ** 2
    d[~solid] = 1e9
    iy, ix = np.unravel_index(np.argmin(d), d.shape)
    return int(ix), int(iy)


def body(cell):
    a = np.asarray(cell)[:, :, 3] >= 160
    ys, xs = np.nonzero(a)
    cx, cy = xs.mean(), ys.mean()
    r = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    radius = float(np.percentile(r, 80))
    pts = np.stack([xs - cx, -(ys - cy)], axis=1)
    cov = pts.T @ pts / len(pts)
    w, v = np.linalg.eigh(cov)
    axis = v[:, 1]
    proj = pts @ axis
    # the sharper end: fewer pixels near its extreme
    hi = (proj > np.percentile(proj, 92)).sum()
    lo = (proj < np.percentile(proj, 8)).sum()
    if np.abs(proj.max()) * (1.0 / max(hi, 1)) < np.abs(proj.min()) * (1.0 / max(lo, 1)):
        axis = -axis
    nose = math.degrees(math.atan2(axis[1], axis[0])) % 360
    return radius, nose, (cx, cy)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("strip")
    ap.add_argument("--muzzle", action="append", default=[])
    ap.add_argument("--nozzle", action="append", default=[])
    ap.add_argument("--radius", type=float, default=14)
    ap.add_argument("--muzzle-frame", type=int, default=ACTION,
                    help="cell the muzzles are refined on (default 5, the action; some strips flash in the tell, 4)")
    ap.add_argument("--nozzle-frame", type=int, default=IDLE, help="cell the nozzles are refined on (default 0)")
    ap.add_argument("--body-frame", type=int, default=IDLE, help="cell hullRadius / noseDeg are measured on (default 0)")
    ap.add_argument("--layout", choices=["ember", "flight"], default="ember",
                    help="flight: muzzles, nozzles and body all on the hover cell 3")
    ap.add_argument("--write")
    ap.add_argument("--preview")
    args = ap.parse_args()

    if args.layout == "flight":
        args.muzzle_frame = args.nozzle_frame = args.body_frame = 3
    frames, cell = cells(args.strip)
    if len(frames) < 7:
        print("warning: %d cells (expected 7)" % len(frames))
    action = frames[min(args.muzzle_frame, len(frames) - 1)]
    idle = frames[min(args.nozzle_frame, len(frames) - 1)]
    bodyframe = frames[min(args.body_frame, len(frames) - 1)]

    muzzles, nozzles = [], []
    for s in args.muzzle:
        n, x, y, d = parse(s)
        px, py = refine(action, x, y, args.radius)
        muzzles.append({"name": n, "x": px, "y": py, "dir": d})
    for s in args.nozzle:
        n, x, y, d = parse(s)
        px, py = refine(idle, x, y, args.radius)
        nozzles.append({"name": n, "x": px, "y": py, "dir": d})

    radius, nose, centre = body(bodyframe)
    print("cellPixels   %d" % cell)
    print("hullRadiusPx %.1f  (x cellWorldSize / cellPixels for hullRadius; ~0.85x for a fair hitbox)" % radius)
    print("noseDeg      %.0f  (check it: art-space degrees, 0 right, 90 up)" % nose)
    print("centroid     %.1f, %.1f" % centre)
    for i, f in enumerate(frames):
        bb = f.getbbox()
        inset = bb and bb[0] > 0 and bb[1] > 0 and bb[2] < cell and bb[3] < cell
        print("frame %d bbox %s%s" % (i, bb, "" if inset else "  <-- touches the cell edge"))
    print("muzzles " + json.dumps(muzzles))
    print("nozzles " + json.dumps(nozzles))

    if args.write:
        with open(args.write) as fh:
            data = json.load(fh)
        data["muzzles"] = muzzles
        data["nozzles"] = nozzles
        data["cellPixels"] = cell
        with open(args.write, "w") as fh:
            json.dump(data, fh, indent=2)
            fh.write("\n")
        print("wrote " + args.write)

    if args.preview:
        k = 3
        out = Image.new("RGBA", (cell * len(frames) * k, cell * k), (24, 18, 40, 255))
        for i, f in enumerate(frames):
            out.alpha_composite(f.resize((cell * k, cell * k), Image.NEAREST), (i * cell * k, 0))
        d = ImageDraw.Draw(out)
        for i in range(len(frames)):
            ox = i * cell * k
            for p, col in [(m, (255, 60, 220, 255)) for m in muzzles] + [(n, (90, 240, 255, 255)) for n in nozzles]:
                x, y = ox + p["x"] * k, p["y"] * k
                d.ellipse((x - 6, y - 6, x + 6, y + 6), outline=col, width=2)
            cx, cy = ox + centre[0] * k, centre[1] * k
            r = math.radians(nose)
            d.line((cx, cy, cx + math.cos(r) * 60, cy - math.sin(r) * 60), fill=(255, 200, 80, 255), width=2)
        out.save(args.preview)
        print("preview " + args.preview)


if __name__ == "__main__":
    main()
