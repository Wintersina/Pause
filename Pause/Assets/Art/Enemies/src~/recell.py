#!/usr/bin/env python3
"""Rebuild a flipbook strip from a free-layout pose sheet, one pose per cell.

    python3 recell.py SHEET OUT.png [--frames 7] [--cell 192] [--margin 4]
                      [--filter hamming] [--scale S]

The rugged v2 concept sheets (Art/Enemies/Staging/*_rugged_v2_concept.png)
hold the seven poses side by side but NOT on a grid: each pose is as wide as
it needs to be. Cutting such a sheet at fixed multiples puts slices of the
neighbouring poses inside a cell and clips the wide ones. This tool cuts by
the sprites instead:

  1. connected components (alpha > FAINT) over the whole sheet; the N largest
     are the pose bodies, every other component (smoke puff, leaf, ember,
     slash arc, rock chip) goes to the body it is nearest to;
  2. ONE scale for all frames: the largest that lets every pose fit a cell
     with `margin` px clear on each side, and lets the idle frames (0-3, plus
     a tell frame that holds the idle stance) keep the sheet's shared baseline;
  3. anchor: x on the body (each pose's body mask is registered against pose
     0, so the hull does not slide when the limbs or the debris change the
     bounding box), y on the sheet's own baseline (the artist's bob survives).
     Action poses that would cross the margin are nudged back inside, idle
     poses never are;
  4. premultiplied resample, paste, and a hard transparent ring on every
     cell's outline.

Nothing is redrawn; pixels only move and scale.
"""
import argparse
import sys
import os

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cells import FAINT, SOLID, FRAMES, label, bbox_of, load_rgba  # noqa: E402

FILTERS = {"nearest": Image.NEAREST, "box": Image.BOX, "bilinear": Image.BILINEAR,
           "hamming": Image.HAMMING, "bicubic": Image.BICUBIC, "lanczos": Image.LANCZOS}
IDLE = 4            # frames 0..3 loop; they share one mapping, never clamped
STANCE_IOU = 0.7    # a tell frame this close to pose 0's body shares it too
HALO = 6            # matte residue (alpha <= FAINT) further than this from art is dropped


def segment(rgba, frames):
    """Split the sheet into `frames` poses. Returns a list of dicts, left to
    right: mask (everything the pose owns, alpha > FAINT), body (its largest
    component), bbox, parts (count of components)."""
    al = rgba[..., 3]
    lab, comps = label(al > FAINT)
    if len(comps) < frames:
        raise SystemExit("only %d components, expected %d poses" % (len(comps), frames))
    by_area = sorted(comps, key=lambda c: -c["area"])
    bodies = sorted(by_area[:frames], key=lambda c: c["bbox"][0])
    if by_area[frames - 1]["area"] < 4 * (by_area[frames]["area"] if len(by_area) > frames else 0):
        raise SystemExit("cannot tell %d pose bodies apart from the debris: areas %s"
                         % (frames, [c["area"] for c in by_area[:frames + 2]]))

    # outline points of each body, for nearest-body ownership of the debris
    outlines = []
    for b in bodies:
        x0, y0, x1, y1 = b["bbox"]
        ys, xs = np.where(lab[y0:y1 + 1, x0:x1 + 1] == b["id"])
        outlines.append(np.stack([xs[::7] + x0, ys[::7] + y0], 1).astype(np.float32))

    owner = {b["id"]: i for i, b in enumerate(bodies)}
    log = []
    for c in comps:
        if c["id"] in owner:
            continue
        x0, y0, x1, y1 = c["bbox"]
        ys, xs = np.where(lab[y0:y1 + 1, x0:x1 + 1] == c["id"])
        pts = np.stack([xs + x0, ys + y0], 1).astype(np.float32)[:: max(1, xs.size // 200)]
        dist = [float(np.sqrt(((pts[:, None, :] - o[None, :, :]) ** 2).sum(2)).min()) for o in outlines]
        order = np.argsort(dist)
        owner[c["id"]] = int(order[0])
        log.append((c["area"], c["bbox"], int(order[0]), dist[order[0]], dist[order[1]]))

    lut = np.zeros(len(comps) + 1, np.int32) - 1
    for cid, i in owner.items():
        lut[cid] = i
    own = lut[lab]                                    # -1 background, else pose index
    poses = []
    for i, b in enumerate(bodies):
        mask = own == i
        poses.append({"mask": mask, "body": lab == b["id"], "bbox": bbox_of(mask),
                      "body_bbox": tuple(b["bbox"]), "parts": sum(1 for v in owner.values() if v == i)})
    return poses, log


def register_x(ref, m, span=40):
    """dx that best lays mask m over mask ref (both centred on their own
    bounding boxes), and the IoU there. Half resolution: plenty for an anchor."""
    def centred(mask):
        x0, y0, x1, y1 = bbox_of(mask)
        h = mask.shape[0]
        out = np.zeros((h, 640), np.float32)
        sub = mask[:, x0:x1 + 1]
        ox = 320 - (x1 - x0 + 1) // 2
        out[:, ox:ox + sub.shape[1]] = sub
        return out[::2, ::2]
    R, M = centred(ref), centred(m)
    best = (-1.0, 0, 0)
    for dy in range(-span // 2, span // 2 + 1):
        sy = np.roll(M, dy, 0)
        for dx in range(-span // 2, span // 2 + 1):
            s = np.roll(sy, dx, 1)
            inter = float((R * s).sum())
            iou = inter / (float(R.sum()) + float(M.sum()) - inter)
            if iou > best[0]:
                best = (iou, dx * 2, dy * 2)
    return best


def premult_resize(rgba, size, flt):
    a = rgba.astype(np.float32)
    a[..., :3] *= a[..., 3:4] / 255.0
    chans = [np.array(Image.fromarray(a[..., c], "F").resize(size, flt)) for c in range(4)]
    out = np.stack(chans, -1)
    al = np.clip(out[..., 3], 0, 255)
    rgb = np.where(al[..., None] > 0.5, out[..., :3] * 255.0 / np.maximum(al[..., None], 1e-3), 0)
    return np.dstack([np.clip(np.rint(rgb), 0, 255), np.rint(al)]).astype(np.uint8)


def build(sheet, frames=FRAMES, cell=192, margin=4, flt="hamming", scale=None, verbose=True):
    rgba = load_rgba(sheet)
    poses, log = segment(rgba, frames)
    al = rgba[..., 3]
    say = print if verbose else (lambda *a, **k: None)

    # ---- anchors (sheet px) ------------------------------------------------
    for i, p in enumerate(poses):
        x0, y0, x1, y1 = p["bbox"]
        iou, dx, dy = register_x(poses[0]["body"] & (al > SOLID), p["body"] & (al > SOLID)) if i else (1.0, 0, 0)
        bx0, _, bx1, _ = bbox_of(p["body"] & (al > SOLID))
        p["iou"] = iou
        # the body's centre line: its own bbox centre, corrected by how far it
        # had to slide to sit on pose 0. Poses that no longer resemble pose 0
        # (a lunge, a turn) centre on everything they own instead.
        p["ax"] = (bx0 + bx1) / 2.0 - dx if iou >= 0.5 else (x0 + x1) / 2.0
        p["anchored"] = "body" if iou >= 0.5 else "bbox"

    # "stance" frames keep the sheet's baseline untouched: the idle loop, plus
    # any tell frame whose body still sits where the idle body does (it only
    # flares). Lunges, turns and the hit frame may be nudged to fit instead.
    for i, p in enumerate(poses):
        p["stance"] = i < IDLE or (i < frames - 1 and p["iou"] >= STANCE_IOU)
    idle = [p for p in poses if p["stance"]]
    iy0, iy1 = min(p["bbox"][1] for p in idle), max(p["bbox"][3] for p in idle)
    ay = (iy0 + iy1) / 2.0                           # the sheet row that lands on the cell's centre line

    # ---- one scale for every frame ----------------------------------------------
    room = cell - 2 * margin
    limits = []
    for i, p in enumerate(poses):
        x0, y0, x1, y1 = p["bbox"]
        limits.append((room / float(x1 - x0 + 1), "frame %d width" % i))
        limits.append((room / float(y1 - y0 + 1), "frame %d height" % i))
        if p["stance"]:                               # must fit without being nudged
            half = max(p["ax"] - x0, x1 + 1 - p["ax"])
            limits.append((room / (2.0 * half), "stance frame %d width about its anchor" % i))
    limits.append((room / float(iy1 - iy0 + 1), "stance frames' shared baseline span"))
    fit, why = min(limits)
    if scale is None:
        scale = fit
    elif scale > fit + 1e-6:
        raise SystemExit("scale %.4f does not fit (max %.4f, limited by %s)" % (scale, fit, why))
    say("scale %.4f (limited by %s); margin %d px" % (scale, why, margin))

    strip = np.zeros((cell, cell * frames, 4), np.uint8)
    keep_halo = np.array(Image.fromarray(((al > FAINT) * 255).astype(np.uint8))
                         .filter(ImageFilter.MaxFilter(2 * HALO + 1))) > 0
    report = []
    for i, p in enumerate(poses):
        x0, y0, x1, y1 = p["bbox"]
        # the pose's art, plus the soft matte right around it, nothing of its neighbours
        near = np.array(Image.fromarray((p["mask"] * 255).astype(np.uint8))
                        .filter(ImageFilter.MaxFilter(2 * HALO + 1))) > 0
        others = np.zeros_like(near)
        for j, q in enumerate(poses):
            if j != i:
                others |= q["mask"]
        take = (p["mask"] | (near & keep_halo & (al <= FAINT))) & ~others
        pad = HALO + 2
        cx0, cy0 = max(0, x0 - pad), max(0, y0 - pad)
        cx1, cy1 = min(rgba.shape[1], x1 + 1 + pad), min(rgba.shape[0], y1 + 1 + pad)
        crop = rgba[cy0:cy1, cx0:cx1].copy()
        crop[~take[cy0:cy1, cx0:cx1]] = 0

        tw, th = max(1, int(round((cx1 - cx0) * scale))), max(1, int(round((cy1 - cy0) * scale)))
        small = premult_resize(crop, (tw, th), FILTERS[flt])
        sb = bbox_of(small[..., 3] > FAINT)

        # where the crop's top-left lands in the cell
        px = int(round(cell / 2.0 - (p["ax"] - cx0) * scale))
        py = int(round(cell / 2.0 - (ay - cy0) * scale))
        nx = ny = 0
        if px + sb[0] < margin: nx = margin - (px + sb[0])
        if px + sb[2] > cell - 1 - margin: nx = (cell - 1 - margin) - (px + sb[2])
        if py + sb[1] < margin: ny = margin - (py + sb[1])
        if py + sb[3] > cell - 1 - margin: ny = (cell - 1 - margin) - (py + sb[3])
        if (nx or ny) and p["stance"]:
            if max(abs(nx), abs(ny)) > 1:             # 1 px is resample rounding
                raise SystemExit("stance frame %d would need a nudge of (%d, %d); lower the scale" % (i, nx, ny))
        px, py = px + nx, py + ny

        cellimg = np.zeros((cell, cell, 4), np.uint8)
        sx0, sy0 = max(0, -px), max(0, -py)
        ex, ey = min(tw, cell - px), min(th, cell - py)
        cellimg[py + sy0:py + ey, px + sx0:px + ex] = small[sy0:ey, sx0:ex]
        cellimg[0, :], cellimg[-1, :], cellimg[:, 0], cellimg[:, -1] = 0, 0, 0, 0
        strip[:, i * cell:(i + 1) * cell] = cellimg

        fb = bbox_of(cellimg[..., 3] > FAINT)
        report.append({"frame": i, "sheet_bbox": p["bbox"], "parts": p["parts"], "anchor": p["anchored"],
                       "iou": round(p["iou"], 3), "nudge": (nx, ny), "cell_bbox": fb,
                       "margins": (fb[0], fb[1], cell - 1 - fb[2], cell - 1 - fb[3])})
        say("frame %d: sheet %s  parts %d  anchor %s (iou %.2f)  nudge %s  cell bbox %s  margins L/T/R/B %s"
            % (i, p["bbox"], p["parts"], p["anchored"], p["iou"], (nx, ny), fb, report[-1]["margins"]))
    for area, bbox, o, d0, d1 in sorted(log, key=lambda t: -t[0]):
        if area >= 30:
            say("  detached part area %5d bbox %s -> pose %d (%.0f px away; next pose %.0f px)" % (area, bbox, o, d0, d1))
    return Image.fromarray(strip, "RGBA"), scale, report


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet")
    ap.add_argument("out")
    ap.add_argument("--frames", type=int, default=FRAMES)
    ap.add_argument("--cell", type=int, default=192)
    ap.add_argument("--margin", type=int, default=4)
    ap.add_argument("--filter", default="hamming", choices=sorted(FILTERS))
    ap.add_argument("--scale", type=float, default=None, help="force a scale (must fit)")
    a = ap.parse_args()
    im, _, _ = build(a.sheet, a.frames, a.cell, a.margin, a.filter, a.scale)
    im.save(a.out, optimize=True)
    print("wrote", a.out, im.size)


if __name__ == "__main__":
    main()
