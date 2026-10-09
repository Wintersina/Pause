#!/usr/bin/env python3
"""Pre-flight for an enemy idle strip (and an enemy's death strip) against the numbers the
Unity tests enforce, so Codex output can be judged BEFORE it is wired.

  python3 verify_enemy_strip.py STRIP.png [more.png ...] [--frames 7] [--death] [--idle-png IDLE.png] [--min-motion 3]

Idle strip (default): 7 square cells (192 px, big 256), checked like EnemyRosterTest:
  detail floor (cell 0 = the key pose): distinct tones >= 300, colour boundaries >= 3000 (big 6000)
  cell integrity: no opaque texel on a cell's outer columns; longest ruler-straight vertical cut < 0.15 x height (29 px at 192)
  player's reds: < 2% of opaque texels within 24 (L1) of #D8232C / #86121F / #FF5B45
  and the art-pass rules (audit_cells.py does the full job): idle cells 0-3 keep one anchor
  (centroid within 3 px of cell 0) and scale (area within +-4%); every cell keeps >= 6 px clear margin.
  VISIBLE IDLE MOTION (the Steel Hound loop was "too mute"): between consecutive idle cells (0>1, 1>2, 2>3,
  and the loop seam 3>0) >= 3% of the body's pixels must change (moving parts -- turbines, claws, core,
  visor -- not hull drift; anchor stays within 3 px). Tune with --min-motion.

--death: a death strip (Resources/Enemies/Death/<key>.png): width = 3 x height, cell 0 anchored on the
  enemy's idle cell 3 (pass --idle-png <the idle strip>; centroid within 3 px), bright parts >= 6 px from edges.

Exit status 1 on any FAIL. Needs numpy + Pillow.
"""
import sys, math, argparse
import numpy as np
from PIL import Image

REDS = [(0xD8, 0x23, 0x2C), (0x86, 0x12, 0x1F), (0xFF, 0x5B, 0x45)]
fails = 0

def check(ok, what):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok: fails += 1

def centroid_area(a):
    ys, xs = np.nonzero(a[..., 3] > 128)
    if len(xs) == 0: return None, 0
    return (xs.mean(), ys.mean()), len(xs)

def margins(a):
    ys, xs = np.nonzero(a[..., 3] > 16)
    if len(xs) == 0: return None
    h, w = a.shape[:2]
    return min(xs.min(), ys.min(), w - 1 - xs.max(), h - 1 - ys.max())

def straight_cut(cell):
    """Longest run of edge texels in one column (solid next to transparent or the cell side)."""
    h, w = cell.shape[:2]
    solid = cell[..., 3] > 128
    worst = 0
    for side in (-1, 1):
        for x in range(w):
            nx = x + side
            inside = 0 <= nx < w
            edge = solid[:, x] & (cell[:, nx, 3] == 0) if inside else solid[:, x]
            run = 0
            for v in edge:
                run = run + 1 if v else 0
                worst = max(worst, run)
    return worst

def motion_pct(a, b):
    """% of the body (union of opaque texels) whose colour or coverage differs between two cells."""
    oa, ob = a[..., 3] > 128, b[..., 3] > 128
    body = oa | ob
    if not body.any(): return 0.0
    d = np.abs(a[..., :3].astype(int) - b[..., :3].astype(int)).sum(axis=2) > 24
    changed = (oa ^ ob) | (oa & ob & d)
    return 100.0 * changed.sum() / body.sum()

def idle(path, frames, min_motion=3.0):
    im = np.asarray(Image.open(path).convert("RGBA"))
    h, w = im.shape[:2]
    check(w == h * frames, f"{path}: {frames} square cells ({w}x{h})")
    if w != h * frames: return
    big = h >= 256
    cells = [im[:, i * h:(i + 1) * h] for i in range(frames)]
    c0 = cells[0]
    op = c0[..., 3] > 128
    rgb = (c0[..., 0].astype(np.int32) << 16) | (c0[..., 1].astype(np.int32) << 8) | c0[..., 2]
    tones = len(np.unique(rgb[op]))
    diff_r = (rgb[:, :-1] != rgb[:, 1:]) & op[:, :-1] & op[:, 1:]
    diff_d = (rgb[:-1, :] != rgb[1:, :]) & op[:-1, :] & op[1:, :]
    edge = np.zeros_like(op)
    edge[:, :-1] |= diff_r; edge[:-1, :] |= diff_d
    boundaries = int(edge.sum())
    need = 6000 if big else 3000
    check(tones >= 300 and boundaries >= need, f"key pose detail: {tones} tones (>=300), {boundaries} colour boundaries (>={need})")
    outline = sum(int((c[:, 0, 3] > 128).sum() + (c[:, -1, 3] > 128).sum()) for c in cells)
    worst = max(straight_cut(c) for c in cells)
    limit = math.ceil(0.15 * h)
    check(outline == 0 and worst < limit, f"cells hold one pose each: {outline} texels on cell side columns, longest straight cut {worst} px (limit {limit})")
    flat = im.reshape(-1, 4)
    flat = flat[flat[:, 3] > 128]
    red = 0
    for r in REDS:
        red += int((np.abs(flat[:, :3].astype(int) - np.array(r)).sum(axis=1) <= 24).sum())
    check(len(flat) > 0 and red < len(flat) * .02, f"off the player's reds ({red} of {len(flat)} texels)")
    ref, area0 = centroid_area(c0)
    for i in range(1, 4):
        c, a = centroid_area(cells[i])
        d = math.hypot(c[0] - ref[0], c[1] - ref[1]) if c else 99
        check(d <= 3.0 and abs(a / area0 - 1) <= .04, f"idle cell {i}: centroid drift {d:.1f} px (<=3), area x{a/area0:.2f} (1+-.04)")
    for i in range(4):
        j = (i + 1) % 4
        mp = motion_pct(cells[i], cells[j])
        check(mp >= min_motion, f"idle motion cell {i}>{j}: {mp:.1f}% of body pixels change (>= {min_motion:g}%; a 2-3 px limb bob, not a still hull)")
    for i, c in enumerate(cells):
        m = margins(c)
        ok = m is not None and m >= 6
        label = "hit/tell cells may flare; still keep" if i >= 4 else "idle"
        check(ok, f"cell {i} ({label}) clear margin {m} px (>=6)")

def death(path, idle_png):
    im = np.asarray(Image.open(path).convert("RGBA"))
    h, w = im.shape[:2]
    check(w == h * 3, f"{path}: 3 square cells ({w}x{h})")
    if w != h * 3: return
    cells = [im[:, i * h:(i + 1) * h] for i in range(3)]
    for i, c in enumerate(cells):
        bright = (c[..., 3] > 128) & (c[..., :3].max(axis=2) > 200)
        ys, xs = np.nonzero(bright)
        m = min(xs.min(), ys.min(), h - 1 - xs.max(), h - 1 - ys.max()) if len(xs) else 99
        check(m >= 6, f"death cell {i}: bright parts {m} px from the edge (>=6)")
    if idle_png:
        s = np.asarray(Image.open(idle_png).convert("RGBA"))
        ih = s.shape[0]
        check(ih == h, f"death cell side {h} == idle cell side {ih}")
        c3 = s[:, 3 * ih:4 * ih]
        a, _ = centroid_area(c3); b, _ = centroid_area(cells[0])
        if a and b:
            d = math.hypot(a[0] - b[0], a[1] - b[1])
            check(d <= 3.0, f"death cell 0 centroid is {d:.1f} px from idle cell 3's (<=3)")

if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("strips", nargs="+")
    ap.add_argument("--frames", type=int, default=7)
    ap.add_argument("--death", action="store_true")
    ap.add_argument("--idle-png")
    ap.add_argument("--min-motion", type=float, default=3.0, help="min %% of body pixels changing between consecutive idle cells")
    a = ap.parse_args()
    for p in a.strips:
        death(p, a.idle_png) if a.death else idle(p, a.frames, a.min_motion)
    print("RESULT:", "FAIL" if fails else "PASS", f"({fails} failures)")
    sys.exit(1 if fails else 0)
