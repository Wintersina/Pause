#!/usr/bin/env python3
"""Audit flipbook strips for art that is not laid out on the cell grid.

    python3 audit_cells.py STRIP.png [more.png ...] [--frames 7] [--near 2]
                           [--crops DIR] [--sheets DIR] [--brief]

EnemyArt cuts a strip at width / FrameCount. If the poses were not composed on
that grid, a cell shows slices of its neighbours and wide poses lose their
tips. Per cell this reports:

  bbox / margins   bounds of the visible art (alpha > 16) and the clear space
                   to each cell side
  edge px          visible pixels within --near px of the L / T / R / B side
  cut px           solid pixels (alpha > 128) ON the outermost column / row:
                   art that runs off the cell or into the next one
  straddle         connected components of the whole strip that span a cell
                   boundary (one drawing sliced across two frames)
  cuts             detached components pressed against a side (a slice of the
                   neighbouring pose), and any component, the body included,
                   whose outermost column is one long unbroken vertical run:
                   the ruler-straight scar a grid cut leaves even after the
                   cell was re-centred. Heuristic: confirm by eye.
  anchor           how far the body sits from where frame 0's body sits
                   (mask registration, px), so the hull does not jump
  scale            the body's size relative to frame 0 (registration over a
                   scale sweep): per-frame "fit to cell" resizing shows up here

Verdict: FAIL on any cut px / straddle / cut scar, WARN on a margin under 2 px,
an idle anchor drift over 4 px or an idle scale drift over 5%, else ok.
Exit status is 1 if any strip FAILs.
"""
import argparse
import glob
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cells import FAINT, SOLID, FRAMES, label, bbox_of, load_rgba, checker, contact_sheet  # noqa: E402

IDLE = 4
CUT_PART = 0.12     # a detached piece whose outermost column is one unbroken run this tall (x cell height)
CUT_BODY = 0.17     # ... and the same for the body itself
SCALES = [round(0.86 + 0.02 * i, 2) for i in range(15)]      # 0.86 .. 1.14


def _run(col):
    """Longest unbroken run of True in a 1-D bool array."""
    d = np.diff(np.concatenate(([0], col.astype(np.int8), [0])))
    a, b = np.where(d == 1)[0], np.where(d == -1)[0]
    return int((b - a).max()) if a.size else 0


def _best_shift(ref, m):
    """Best IoU of m laid over ref for any shift (FFT correlation), with the shift."""
    h, w = ref.shape
    F = np.fft.rfft2(ref, (2 * h, 2 * w))
    G = np.fft.rfft2(m, (2 * h, 2 * w))
    corr = np.fft.irfft2(F * np.conj(G), (2 * h, 2 * w))
    i = int(np.argmax(corr))
    dy, dx = divmod(i, 2 * w)
    if dy >= h: dy -= 2 * h
    if dx >= w: dx -= 2 * w
    inter = float(corr.flat[i])
    return inter / max(1.0, float(ref.sum()) + float(m.sum()) - inter), dx, dy


def register(ref, m):
    """(iou, dx, dy, scale): how mask m must be resized about its centre and
    shifted to sit on ref. dx/dy are where m's body is relative to ref's."""
    best = (-1.0, 0, 0, 1.0)
    h, w = m.shape
    src = Image.fromarray((m * 255).astype(np.uint8))
    for s in SCALES:
        # resizing m by 1/s undoes a body drawn s times too large
        tw, th = max(1, int(round(w / s))), max(1, int(round(h / s)))
        r = np.array(src.resize((tw, th), Image.BILINEAR)) > 127
        canvas = np.zeros((h, w), np.float32)
        ox, oy = (w - tw) // 2, (h - th) // 2
        sx0, sy0 = max(0, -ox), max(0, -oy)
        ex, ey = min(tw, w - ox), min(th, h - oy)
        canvas[oy + sy0:oy + ey, ox + sx0:ox + ex] = r[sy0:ey, sx0:ex]
        iou, dx, dy = _best_shift(ref, canvas)
        if iou > best[0] + 1e-4:
            best = (iou, -dx, -dy, s)
    return best


def audit(path, frames=FRAMES, near=2):
    rgba = load_rgba(path)
    h, w = rgba.shape[:2]
    cw = w // frames
    al = rgba[..., 3]
    out = {"path": path, "size": (w, h), "cell": cw, "cells": [], "straddle": [], "problems": [], "warnings": []}
    if w % frames:
        out["problems"].append("width %d is not a multiple of %d frames" % (w, frames))

    lab, comps = label(al > FAINT)
    for c in comps:
        x0, _, x1, _ = c["bbox"]
        if x0 // cw != x1 // cw:
            out["straddle"].append({"area": c["area"], "bbox": tuple(c["bbox"]), "cells": (x0 // cw, x1 // cw)})
            out["problems"].append("component of %d px spans cells %d-%d (x %d..%d)"
                                   % (c["area"], x0 // cw, x1 // cw, x0, x1))

    body0 = None
    for i in range(frames):
        ca = al[:, i * cw:(i + 1) * cw]
        vis, solid = ca > FAINT, ca > SOLID
        cell = {"frame": i}
        bb = bbox_of(vis)
        if bb is None:
            cell["empty"] = True
            out["cells"].append(cell)
            out["problems"].append("cell %d is empty" % i)
            continue
        cell["bbox"] = bb
        cell["margins"] = (bb[0], bb[1], cw - 1 - bb[2], h - 1 - bb[3])
        cell["edge"] = (int(vis[:, :near].sum()), int(vis[:near, :].sum()),
                        int(vis[:, cw - near:].sum()), int(vis[h - near:, :].sum()))
        cell["cut"] = (int(solid[:, 0].sum()), int(solid[0, :].sum()),
                       int(solid[:, cw - 1].sum()), int(solid[h - 1, :].sum()))
        if sum(cell["cut"]):
            out["problems"].append("cell %d: solid art on the cell outline L/T/R/B %s" % (i, cell["cut"]))
        elif min(cell["margins"]) < 2:
            out["warnings"].append("cell %d: margin under 2 px %s" % (i, cell["margins"]))

        # components inside the cell on its own
        clab, ccomps = label(vis)
        ccomps.sort(key=lambda c: -c["area"])
        main = ccomps[0]
        mx0, my0, mx1, my1 = main["bbox"]
        cell["parts"] = len(ccomps)
        cell["orphans"] = []
        cell["flat"] = 0
        for c in ccomps:
            if c["area"] < 6:
                continue
            x0, y0, x1, y1 = c["bbox"]
            m = clab[y0:y1 + 1, x0:x1 + 1] == c["id"]
            left, right = _run(m[:, 0]), _run(m[:, -1])
            flat, side = max(left, right), "left" if left >= right else "right"
            cell["flat"] = max(cell["flat"], flat)
            is_main = c is main
            why = None
            if not is_main and (x0 == 0 or x1 == cw - 1):
                why = "pressed against the %s side" % ("left" if x0 == 0 else "right")
            elif flat >= (CUT_BODY if is_main else CUT_PART) * h:
                # a hull that is flat on BOTH sides is a design, not a cut
                if is_main and min(left, right) >= 0.75 * flat:
                    out["warnings"].append("cell %d: body is flat on both sides (%d / %d px), check by eye" % (i, left, right))
                    continue
                why = "ruler-straight %s side %d px tall (sliced by a grid cut?)" % (side, flat)
            if why:
                cell["orphans"].append({"area": c["area"], "bbox": (x0, y0, x1, y1), "why": why, "main": is_main,
                                        "weight": flat * 10 if is_main else c["area"]})
                out["problems"].append("cell %d: %s %d px at %s, %s"
                                       % (i, "body" if is_main else "detached fragment", c["area"], (x0, y0, x1, y1), why))
        if main["bbox"][0] == 0 or main["bbox"][2] == cw - 1:
            cell["clipped"] = True

        # anchor / scale against frame 0's body (half resolution)
        body = (clab == main["id"]) & solid
        half = np.zeros((h, cw), np.float32)
        half[body] = 1
        half = half[::2, ::2]
        if body0 is None:
            body0 = half
            cell["reg"] = (1.0, 0, 0, 1.0)
        else:
            iou, dx, dy, s = register(body0, half)
            cell["reg"] = (round(iou, 3), dx * 2, dy * 2, s)
            if i < IDLE:
                if max(abs(dx * 2), abs(dy * 2)) > 4:
                    out["warnings"].append("idle frame %d body sits (%d, %d) px off frame 0" % (i, dx * 2, dy * 2))
                if abs(s - 1) > 0.05:
                    out["warnings"].append("idle frame %d body is drawn at x%.2f of frame 0" % (i, s))
        cell["centre"] = (round((bb[0] + bb[2]) / 2.0 - (cw - 1) / 2.0, 1), round((bb[1] + bb[3]) / 2.0 - (h - 1) / 2.0, 1))
        out["cells"].append(cell)

    cut = sum(sum(c.get("cut", (0,))) for c in out["cells"])
    orphan = sum(o["weight"] for c in out["cells"] for o in c.get("orphans", []))
    strad = sum(s["area"] for s in out["straddle"])
    out["severity"] = cut * 10 + orphan + strad
    out["verdict"] = "FAIL" if out["problems"] else ("WARN" if out["warnings"] else "ok")
    return out


def print_report(r, brief=False):
    print("%s  %dx%d  cell %d  -> %s (severity %d)" % (os.path.basename(r["path"]), r["size"][0], r["size"][1],
                                                         r["cell"], r["verdict"], r["severity"]))
    if not brief:
        print("  cell  bbox x0,y0-x1,y1      margins L/T/R/B   edge L/T/R/B     cut L/T/R/B   parts  flat  anchor dx,dy  scale  iou")
        for c in r["cells"]:
            if c.get("empty"):
                print("  %d     (empty)" % c["frame"])
                continue
            print("  %d     %-20s  %-16s  %-15s  %-12s  %-5d  %-4d  %-12s  %.2f   %.2f"
                  % (c["frame"], "%d,%d-%d,%d" % c["bbox"], "%d/%d/%d/%d" % c["margins"],
                     "%d/%d/%d/%d" % c["edge"], "%d/%d/%d/%d" % c["cut"], c["parts"], c["flat"],
                     "%+d,%+d" % (c["reg"][1], c["reg"][2]), c["reg"][3], c["reg"][0]))
    for p in r["problems"]:
        print("  FAIL " + p)
    for p in r["warnings"]:
        print("  warn " + p)


def save_crops(path, dst, frames=FRAMES, scale=3):
    im = Image.open(path).convert("RGBA")
    cw = im.width // frames
    name = os.path.splitext(os.path.basename(path))[0]
    os.makedirs(dst, exist_ok=True)
    for i in range(frames):
        bg = checker(cw, im.height, (60, 90, 140), (75, 110, 165))
        bg.alpha_composite(im.crop((i * cw, 0, (i + 1) * cw, im.height)))
        bg.resize((cw * scale, im.height * scale), Image.NEAREST).convert("RGB").save(
            os.path.join(dst, "%s_cell%d.png" % (name, i)))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("strips", nargs="+", help="strip PNGs (globs are expanded)")
    ap.add_argument("--frames", type=int, default=FRAMES)
    ap.add_argument("--near", type=int, default=2)
    ap.add_argument("--crops", help="write every cell as its own PNG here")
    ap.add_argument("--sheets", help="write an outlined contact sheet per strip here")
    ap.add_argument("--brief", action="store_true", help="verdict and findings only")
    a = ap.parse_args()
    paths = []
    for s in a.strips:
        paths += sorted(glob.glob(s)) or [s]
    results = []
    for p in paths:
        r = audit(p, a.frames, a.near)
        results.append(r)
        print_report(r, a.brief)
        if a.crops: save_crops(p, a.crops, a.frames)
        if a.sheets:
            os.makedirs(a.sheets, exist_ok=True)
            contact_sheet(p, os.path.join(a.sheets, os.path.splitext(os.path.basename(p))[0] + "_sheet.png"), a.frames)
    if len(results) > 1:
        print("\nranked by severity:")
        for r in sorted(results, key=lambda r: (-r["severity"], -len(r["warnings"]))):
            if r["verdict"] != "ok":
                print("  %-5s %6d  %s  (%d fail, %d warn)" % (r["verdict"], r["severity"], os.path.basename(r["path"]),
                                                           len(r["problems"]), len(r["warnings"])))
        print("  %d of %d strips clean" % (sum(r["verdict"] == "ok" for r in results), len(results)))
    sys.exit(1 if any(r["verdict"] == "FAIL" for r in results) else 0)


if __name__ == "__main__":
    main()
