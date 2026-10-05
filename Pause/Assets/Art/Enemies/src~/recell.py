#!/usr/bin/env python3
"""Rebuild a flipbook strip from a free-layout pose sheet, one pose per cell.

    python3 recell.py SHEET OUT.png [--frames 7] [--cell 192] [--margin 4]
                      [--filter hamming] [--match OLD.png | --frame0 PX | --scale S]
                      [--hit-scale S | --hit-floor 0.85 | --no-hit-scale] [--own-scale 5] [--assign X,Y:POSE]

The rugged concept sheets (Art/Enemies/Staging/*_concept.png) hold the seven
poses side by side but NOT on a grid: each pose is as wide as it needs to be.
Cutting such a sheet at fixed multiples puts slices of the neighbouring poses
inside a cell and clips the wide ones; fitting each cut to the cell on its own
makes the body change size from frame to frame. This tool cuts by the sprites
instead:

  1. the sheet is cut where nothing is drawn: runs of occupied columns are
     merged across their narrowest gaps until N poses remain, so every smoke
     puff, leaf, ember, slash arc and rock chip stays with its own pose;
  2. size is preserved: frames 0..N-2 share ONE scale (unless --own-scale
     releases a burst frame, see below), the one that draws
     frame 0 at --frame0 px (or as an existing strip draws it, --match), or
     the largest that fits when none is asked for or the asked one cannot fit
     (every pose inside the cell with `margin` px clear, the idle frames 0-3
     and any tell frame that holds the idle stance on the sheet's baseline).
     The hit frame (N-1) takes a smaller scale of its own when it alone is
     too big, or cannot keep the shared anchor without leaving the
     cell (down to --hit-floor of the shared scale; --no-hit-scale to
     forbid, --hit-scale to force). --own-scale 5 gives an action frame the
     same treatment: only for a burst so large that sharing one scale would
     push the enemy outside the roster's size tolerance;
  3. anchor: x on the body (each pose's body mask is registered against pose
     0, so the hull does not slide when the limbs or the debris change the
     bounding box), y on the sheet's own baseline (the artist's bob survives).
     A separately scaled hit frame is scaled about that same anchor.
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


def segment(rgba, frames, assign=()):
    """Split the sheet into `frames` poses, left to right.

    Poses on a sheet are separated by empty columns, so the sheet is cut where
    nothing is drawn: the runs of occupied columns are merged across their
    narrowest gaps until `frames` groups remain (a puff of debris a few px off
    its pose joins it; the wide gaps between poses survive). Every connected
    component then belongs to the group it stands in. Returns the poses (mask
    = everything the pose owns, body = its largest component, bbox, parts),
    a log of the larger detached parts, and the gap evidence."""
    al = rgba[..., 3]
    lab, comps = label(al > FAINT)
    cols = (al > FAINT).any(0)
    d = np.diff(np.concatenate(([0], cols.astype(np.int8), [0])))
    runs = [[int(a), int(b) - 1] for a, b in zip(np.where(d == 1)[0], np.where(d == -1)[0])]
    if len(runs) < frames:
        return segment_by_bodies(al, frames, assign)
    merged = []
    while len(runs) > frames:
        gaps = [runs[i + 1][0] - runs[i][1] - 1 for i in range(len(runs) - 1)]
        i = int(np.argmin(gaps))
        merged.append(gaps[i])
        runs[i][1] = runs[i + 1][1]
        del runs[i + 1]
    kept = [runs[i + 1][0] - runs[i][1] - 1 for i in range(len(runs) - 1)]
    info = {"pose_gaps": kept, "widest_merged_gap": max(merged) if merged else 0,
            "pose_columns": [tuple(r) for r in runs]}

    poses, log = [], []
    for i, (rx0, rx1) in enumerate(runs):
        mine = [c for c in comps if rx0 <= c["bbox"][0] and c["bbox"][2] <= rx1]
        body = max(mine, key=lambda c: c["area"])
        mask = np.zeros(al.shape, bool)
        mask[:, rx0:rx1 + 1] = al[:, rx0:rx1 + 1] > FAINT
        poses.append({"mask": mask, "body": lab == body["id"], "bbox": bbox_of(mask),
                      "body_bbox": tuple(body["bbox"]), "parts": len(mine)})
        for c in mine:
            if c is not body and c["area"] >= 30:
                log.append((c["area"], tuple(c["bbox"]), i))
    return poses, log, info


def segment_by_bodies(al, frames, assign=()):
    """The fallback when poses overlap in x (a diagonal wing over its
    neighbour's column) and no clean column cut exists: the `frames` largest
    connected components are the pose bodies, every other component goes to
    the body whose outline it is nearest to. Where a soft glow bridges two
    poses the bodies are found at a higher alpha threshold, and the glow
    between them is then shared out pixel by pixel to the pose it grows from.
    Refuses when the bodies cannot be told from the debris, or two "bodies"
    stand in the same place. `assign` = [(x, y, pose)] overrides the nearest-
    body rule for the component drawn at sheet pixel (x, y): for the odd piece
    of debris that lies nearer the neighbouring pose's beam than its own hull."""
    why = None
    for thr in (FAINT, 64, SOLID):
        lab, comps = label(al > thr)
        by_area = sorted(comps, key=lambda c: -c["area"])
        if len(by_area) < frames:
            why = "only %d components at alpha > %d" % (len(by_area), thr)
            continue
        bodies = sorted(by_area[:frames], key=lambda c: (c["bbox"][0] + c["bbox"][2]))
        rest = by_area[frames]["area"] if len(by_area) > frames else 0
        cx = [(b["bbox"][0] + b["bbox"][2]) / 2.0 for b in bodies]
        wid = float(np.median([b["bbox"][2] - b["bbox"][0] + 1 for b in bodies]))
        areas = [b["area"] for b in bodies]
        if by_area[frames - 1]["area"] < 3 * rest:
            why = "cannot tell %d bodies from the debris at alpha > %d: areas %s" % (
                frames, thr, [c["area"] for c in by_area[:frames + 2]])
        elif max(areas) > 2.2 * float(np.median(areas)):
            why = "one component is %d px against a median of %d at alpha > %d: two poses are fused" % (
                max(areas), int(np.median(areas)), thr)
        elif min(np.diff(cx)) < 0.5 * wid:
            why = "two of the largest components stand in the same pose (centres %s)" % cx
        else:
            break
    else:
        raise SystemExit("cannot separate the poses: " + why)

    outlines = []
    for b in bodies:
        x0, y0, x1, y1 = b["bbox"]
        ys, xs = np.where(lab[y0:y1 + 1, x0:x1 + 1] == b["id"])
        outlines.append(np.stack([xs[::7] + x0, ys[::7] + y0], 1).astype(np.float32))
    owner = {b["id"]: i for i, b in enumerate(bodies)}
    log, closest_call = [], None
    for c in comps:
        if c["id"] in owner:
            continue
        x0, y0, x1, y1 = c["bbox"]
        ys, xs = np.where(lab[y0:y1 + 1, x0:x1 + 1] == c["id"])
        pts = np.stack([xs + x0, ys + y0], 1).astype(np.float32)[:: max(1, xs.size // 200)]
        dist = [float(np.sqrt(((pts[:, None, :] - o[None, :, :]) ** 2).sum(2)).min()) for o in outlines]
        order = np.argsort(dist)
        owner[c["id"]] = int(order[0])
        if c["area"] >= 30:
            log.append((c["area"], tuple(c["bbox"]), int(order[0])))
            margin = dist[order[1]] - dist[order[0]]
            if closest_call is None or margin < closest_call[0]:
                closest_call = (round(margin, 1), c["area"], tuple(c["bbox"]), int(order[0]), int(order[1]))
    for x, y, pose in assign:
        cid = int(lab[int(y), int(x)])
        if cid == 0 or not 0 <= int(pose) < frames:
            raise SystemExit("--assign %s,%s:%s names no component / no such pose" % (x, y, pose))
        if cid in [b["id"] for b in bodies]:
            raise SystemExit("--assign %s,%s is on a pose body" % (x, y))
        owner[cid] = int(pose)
    lut = np.zeros(len(comps) + 1, np.int32) - 1
    for cid, i in owner.items():
        lut[cid] = i
    own = lut[lab]

    # the fainter art around the thresholded components (soft glow, mist):
    # grow each pose into it one pixel ring at a time, so a bridge between
    # two poses is split where the two fronts meet
    shared = 0
    if thr > FAINT:
        todo = (al > FAINT) & (own < 0)
        shared = int(todo.sum())
        for _ in range(400):
            if not todo.any():
                break
            best = own.copy()
            for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1)):
                sh = np.full_like(own, -1)
                ys0, ys1 = max(0, dy), own.shape[0] + min(0, dy)
                xs0, xs1 = max(0, dx), own.shape[1] + min(0, dx)
                sh[ys0:ys1, xs0:xs1] = own[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
                take = todo & (best < 0) & (sh >= 0)
                best[take] = sh[take]
            grew = todo & (best >= 0)
            if not grew.any():
                break
            own = best
            todo &= ~grew
        # faint specks that touch no pose: nearest body by column
        if todo.any():
            ys, xs = np.where(todo)
            centres = np.array([(b["bbox"][0] + b["bbox"][2]) / 2.0 for b in bodies])
            own[ys, xs] = np.abs(xs[:, None] - centres[None, :]).argmin(1)

    poses = []
    for i, b in enumerate(bodies):
        mask = own == i
        poses.append({"mask": mask, "body": lab == b["id"], "bbox": bbox_of(mask),
                      "body_bbox": tuple(b["bbox"]), "parts": sum(1 for v in owner.values() if v == i)})
    info = {"pose_gaps": "none: poses overlap in x, cut by nearest body at alpha > %d%s"
                         % (thr, ", %d px of soft glow shared out" % shared if shared else ""),
            "widest_merged_gap": 0, "closest_call": closest_call}
    return poses, log, info


def register_x(ref, m, span=40):
    """(iou, dx, dy): the shift that best lays mask m over mask ref (both
    centred on their own bounding boxes). Half resolution: plenty for an anchor."""
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


def build(sheet, frames=FRAMES, cell=192, margin=4, flt="hamming", scale=None, frame0=None,
          hit_own_scale=True, hit_scale=None, hit_floor=0.85, own_scale=(), assign=(), verbose=True):
    """Returns (strip image, {"scale": shared, "hit_scale": ..., "frames": [...], ...})."""
    rgba = load_rgba(sheet)
    poses, log, info = segment(rgba, frames, assign)
    al = rgba[..., 3]
    say = print if verbose else (lambda *a, **k: None)
    hit = frames - 1
    # frames allowed a smaller scale of their own: the hit frame, plus any
    # action frame (4, 5) the caller names. The idle loop never is.
    own = set(int(i) for i in own_scale if IDLE <= int(i) < frames)
    if hit_own_scale:
        own.add(hit)
    say("pose gaps on the sheet %s px; widest gap merged into a pose %d px%s"
        % (info["pose_gaps"], info["widest_merged_gap"],
           "; closest ownership call %s" % (info["closest_call"],) if info.get("closest_call") else ""))

    # ---- anchors (sheet px) ------------------------------------------------
    for i, p in enumerate(poses):
        x0, y0, x1, y1 = p["bbox"]
        iou, dx, dy = register_x(poses[0]["body"] & (al > SOLID), p["body"] & (al > SOLID)) if i else (1.0, 0, 0)
        bx0, by0, bx1, by1 = bbox_of(p["body"] & (al > SOLID))
        p["iou"] = iou
        # the body's centre: its own bbox centre, corrected by how far it had to
        # slide to sit on pose 0. Poses that no longer resemble pose 0 (a
        # lunge, a turn) centre on everything they own instead.
        p["ax"] = (bx0 + bx1) / 2.0 - dx if iou >= 0.5 else (x0 + x1) / 2.0
        p["body_cy"] = (by0 + by1) / 2.0 - dy
        p["anchored"] = "body" if iou >= 0.5 else "bbox"

    # "stance" frames keep the sheet's baseline untouched: the idle loop, plus
    # any tell frame whose body still sits where the idle body does (it only
    # flares). Lunges, turns and the hit frame may be nudged to fit instead.
    for i, p in enumerate(poses):
        p["stance"] = i < IDLE or (i < hit and i not in own and p["iou"] >= STANCE_IOU)
    idle = [p for p in poses if p["stance"]]
    iy0, iy1 = min(p["bbox"][1] for p in idle), max(p["bbox"][3] for p in idle)
    ay = (iy0 + iy1) / 2.0                           # the sheet row that lands on the cell's centre line

    # ---- scale ------------------------------------------------------------------
    # The idle loop sets the enemy's size, and every frame shares its scale
    # except the ones in `own`: the hit frame (a two-tick flash, often the
    # largest drawing because of its smoke and debris) and any burst frame
    # the caller names. Those take a smaller scale when they alone are too
    # big, so that one drawing does not shrink the whole enemy.
    room = cell - 2 * margin - 1                     # 1 px spare: resample rounding must not nudge an idle frame

    def fit_of(i):
        x0, y0, x1, y1 = poses[i]["bbox"]
        out = [(room / float(x1 - x0 + 1), "frame %d width" % i), (room / float(y1 - y0 + 1), "frame %d height" % i)]
        if poses[i]["stance"]:                        # must fit without being nudged
            half = max(poses[i]["ax"] - x0, x1 + 1 - poses[i]["ax"])
            out.append((room / (2.0 * half), "stance frame %d width about its anchor" % i))
        return out

    shared = [l for i in range(frames) if i not in own for l in fit_of(i)]
    shared.append((room / float(iy1 - iy0 + 1), "stance frames' shared baseline span"))
    fit, why = min(shared)
    want = scale
    if want is None and frame0 is not None:
        b = bbox_of(poses[0]["mask"] & (al > SOLID))
        want = frame0 / float(max(b[2] - b[0] + 1, b[3] - b[1] + 1))
    short = 0.0
    if want is None or want > fit + 1e-6:
        if want is not None:
            short = 1 - fit / want
            say("wanted scale %.4f does not fit: limited to %.4f by %s (%.1f%% smaller)" % (want, fit, why, 100 * short))
        scale = fit
    else:
        scale, why = want, "the requested size"

    scales, notes = [scale] * frames, {}
    for i in sorted(own):
        # it must not jump: it is scaled about the same anchor as every other
        # frame (body centre line, sheet baseline), so it has to fit ABOUT
        # that point. It shrinks for that down to hit_floor of the shared
        # scale; past the floor it is nudged the rest of the way (and it
        # always shrinks as far as its plain width / height demand).
        ofit, owhy = min(fit_of(i))
        x0, y0, x1, y1 = poses[i]["bbox"]
        half = cell / 2.0 - margin
        pinned = min(half / max(1.0, ay - y0), half / max(1.0, y1 + 1 - ay),
                     half / max(1.0, poses[i]["ax"] - x0), half / max(1.0, x1 + 1 - poses[i]["ax"]))
        if pinned < ofit:
            ofit, owhy = max(pinned, min(ofit, hit_floor * scale)), "frame %d about the shared anchor" % i
        if i == hit and hit_scale is not None:
            if hit_scale > min(fit_of(i))[0] + 1e-6:
                raise SystemExit("hit scale %.4f does not fit (max %.4f)" % (hit_scale, min(fit_of(i))[0]))
            ofit, owhy = hit_scale, "--hit-scale"
        if ofit < scale:
            scales[i], notes[i] = ofit, owhy
    hs = scales[hit]
    say("scale %.4f (set by %s); margin %d px" % (scale, why, margin))
    for i in sorted(notes):
        say("  frame %d takes its own scale %.4f, x%.3f of the others (set by %s)" % (i, scales[i], scales[i] / scale, notes[i]))

    strip = np.zeros((cell, cell * frames, 4), np.uint8)
    keep_halo = np.array(Image.fromarray(((al > FAINT) * 255).astype(np.uint8))
                         .filter(ImageFilter.MaxFilter(2 * HALO + 1))) > 0
    report = []
    for i, p in enumerate(poses):
        x0, y0, x1, y1 = p["bbox"]
        s = scales[i]
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

        tw, th = max(1, int(round((cx1 - cx0) * s))), max(1, int(round((cy1 - cy0) * s)))
        small = premult_resize(crop, (tw, th), FILTERS[flt])
        sb = bbox_of(small[..., 3] > FAINT)

        # where the crop's top-left lands in the cell
        px = int(round(cell / 2.0 - (p["ax"] - cx0) * s))
        py = int(round(cell / 2.0 - (ay - cy0) * s))
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
                       "iou": round(p["iou"], 3), "nudge": (nx, ny), "cell_bbox": fb, "scale": s,
                       "margins": (fb[0], fb[1], cell - 1 - fb[2], cell - 1 - fb[3])})
        say("frame %d: sheet %s  parts %d  anchor %s (iou %.2f)  nudge %s  cell bbox %s  margins L/T/R/B %s"
            % (i, p["bbox"], p["parts"], p["anchored"], p["iou"], (nx, ny), fb, report[-1]["margins"]))
    for area, bbox, o in sorted(log, key=lambda t: -t[0])[:8]:
        say("  detached part area %5d bbox %s -> pose %d" % (area, bbox, o))
    f0 = bbox_of(strip[:, :cell, 3] > SOLID)
    edge0 = max(f0[2] - f0[0] + 1, f0[3] - f0[1] + 1)
    say("frame 0 drawn %d px on its longest side (%.3f of the cell)" % (edge0, edge0 / float(cell)))
    return Image.fromarray(strip, "RGBA"), {"scale": scale, "hit_scale": hs, "scales": scales, "why": why, "short": short,
                                            "frames": report, "frame0_px": edge0, "info": info}


def frame0_edge(strip_path, frames=FRAMES):
    """Longest side of frame 0's solid art in an existing strip, px."""
    a = load_rgba(strip_path)
    cw = a.shape[1] // frames
    b = bbox_of(a[:, :cw, 3] > SOLID)
    return max(b[2] - b[0] + 1, b[3] - b[1] + 1)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("sheet")
    ap.add_argument("out")
    ap.add_argument("--frames", type=int, default=FRAMES)
    ap.add_argument("--cell", type=int, default=192)
    ap.add_argument("--margin", type=int, default=4)
    ap.add_argument("--filter", default="hamming", choices=sorted(FILTERS))
    ap.add_argument("--scale", type=float, default=None, help="scale for frames 0..N-2 (capped at what fits)")
    ap.add_argument("--frame0", type=float, default=None,
                    help="draw frame 0 this many px on its longest side (capped at what fits)")
    ap.add_argument("--match", default=None, help="an existing strip: keep its frame 0 size (same as --frame0)")
    ap.add_argument("--hit-scale", type=float, default=None, help="force the hit frame's own scale")
    ap.add_argument("--own-scale", type=int, action="append", default=[], metavar="FRAME",
                    help="also let this action frame (4 or 5) take a smaller scale of its own, like the "
                         "hit frame: for a burst so large that one shared scale would shrink the enemy")
    ap.add_argument("--assign", action="append", default=[], metavar="X,Y:POSE",
                    help="give the detached piece drawn at sheet pixel X,Y to pose POSE (overlapping sheets only)")
    ap.add_argument("--hit-floor", type=float, default=0.85,
                    help="the hit frame shrinks to keep the shared anchor down to this fraction "
                         "of the shared scale, then is nudged instead (default 0.85)")
    ap.add_argument("--no-hit-scale", action="store_true",
                    help="hold the hit frame to the shared scale (it then limits every frame)")
    a = ap.parse_args()
    f0 = a.frame0 if a.frame0 is not None else (frame0_edge(a.match, a.frames) if a.match else None)
    im, _ = build(a.sheet, a.frames, a.cell, a.margin, a.filter, a.scale, f0, not a.no_hit_scale, a.hit_scale, a.hit_floor, a.own_scale,
                  [(int(v.split(':')[0].split(',')[0]), int(v.split(':')[0].split(',')[1]), int(v.split(':')[1]))
                   for v in a.assign])
    im.save(a.out, optimize=True)
    print("wrote", a.out, im.size)


if __name__ == "__main__":
    main()
