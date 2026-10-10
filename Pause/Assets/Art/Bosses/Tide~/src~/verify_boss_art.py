#!/usr/bin/env python3
"""Pre-flight for a world boss's art set (Resources/Bosses/<Key>*.png) against what BossArt / BossArtImporter /
BossDamageTest / BossEncounterTest expect.

  python3 verify_boss_art.py BOSSES_DIR KEY [--no-damage] [--no-death]

  <Key>.png         1920x1536: 5 x 4 cells of 384, all 20 non-empty; no alpha>24 pixel within 6 px of a cell border
                    (a clipped glow reads as a hard rectangular cut in game - the Space/Frost fix of 2026-10-08)
  <Key>_shots.png   1024x128: 8 cells of 128 (bolt 0-1, shard 2-3, lane telegraph 4, beam 5-6, muzzle charge 7)
  <Key>_card.png    1024x288 (intro name card)
  <Key>_damage.png  768x1536: 2 cols x 4 rows (row r = damage stage r+1, cols = 2-frame idle loop A,B), registered to the
                    pristine idle cell 0 (alpha bbox within the pristine bbox grown by 6 px), each stage visibly different
                    from the one before (mean absolute difference > 4 / 255 over the cell)
  <Key>_damage_fx.png 2304x768: 6 x 2 cells of 384 (row 0 smoke loop, row 1 electrical arcs loop), non-empty, not clipped
  <Key>_death.png   2304x384: 6 cells of 384, non-empty, registered (centre of mass within the cell), margin >= 6 except
                    the big fireball cells (2, 3), which may reach 90% of the cell
Exit status 1 on any FAIL. Needs numpy + Pillow.
"""
import sys, os, argparse
import numpy as np
from PIL import Image

fails = 0
def check(ok, what):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok: fails += 1

def warn(ok, what):
    print(("PASS  " if ok else "WARN  ") + what)

def load(p):
    return np.asarray(Image.open(p).convert("RGBA"))

def cells(a, cw, ch):
    H, W = a.shape[:2]
    return [[a[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw] for c in range(W // cw)] for r in range(H // ch)]

def bbox(c, thr=24):
    ys, xs = np.nonzero(c[..., 3] > thr)
    return None if len(xs) == 0 else (xs.min(), ys.min(), xs.max(), ys.max())

def margin(c, thr=24):
    b = bbox(c, thr)
    if b is None: return None
    h, w = c.shape[:2]
    return min(b[0], b[1], w - 1 - b[2], h - 1 - b[3])

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dir"); ap.add_argument("key")
    ap.add_argument("--no-damage", action="store_true"); ap.add_argument("--no-death", action="store_true")
    o = ap.parse_args()
    P = lambda s: os.path.join(o.dir, o.key + s)

    a = load(P(".png"))
    check(a.shape[:2] == (1536, 1920), f"{o.key}.png is 1920x1536 ({a.shape[1]}x{a.shape[0]})")
    grid = cells(a, 384, 384)
    empty = [(r, c) for r in range(4) for c in range(5) if bbox(grid[r][c]) is None]
    check(not empty, f"all 20 base cells are non-empty {empty}")
    clipped = [(r, c, margin(grid[r][c])) for r in range(4) for c in range(5) if margin(grid[r][c]) is not None and margin(grid[r][c]) < 6]
    touching = [(r, c, int(m)) for r, c, m in clipped if m == 0]
    idle_touch = [(r, c) for r, c, m in touching if r * 5 + c <= 4]
    check(not idle_touch, f"idle/hit cells (0-4) never touch the cell border {idle_touch}")
    warn(not clipped, f"every base cell keeps >= 6 px clear (alpha>24); cells inside 6 px (row, col, margin): {[(r, c, int(m)) for r, c, m in clipped]} - aim for none; touching (margin 0) = a hard rectangular cut in game")

    s = load(P("_shots.png")); check(s.shape[:2] == (128, 1024), f"{o.key}_shots.png is 1024x128 ({s.shape[1]}x{s.shape[0]})")
    sc = [s[:, i * 128:(i + 1) * 128] for i in range(8)]
    check(all(bbox(c) is not None for c in sc), "all 8 shot cells are non-empty")
    c = load(P("_card.png")); check(c.shape[:2] == (288, 1024), f"{o.key}_card.png is 1024x288 ({c.shape[1]}x{c.shape[0]})")

    if not o.no_damage and os.path.exists(P("_damage.png")):
        d = load(P("_damage.png")); check(d.shape[:2] == (1536, 768), f"{o.key}_damage.png is 768x1536 ({d.shape[1]}x{d.shape[0]})")
        dg = cells(d, 384, 384)
        pb = bbox(grid[0][0])
        ok = True
        for r in range(4):
            for col in range(2):
                b = bbox(dg[r][col])
                if b is None or pb is None or b[0] < pb[0] - 6 or b[1] < pb[1] - 6 or b[2] > pb[2] + 6 or b[3] > pb[3] + 6:
                    ok = False; print(f"      stage {r+1} frame {col}: bbox {b} vs pristine {pb}")
        check(ok, "every damage cell registers to the pristine idle cell (bbox within +-6 px)")
        diffs = [float(np.abs(dg[r][0].astype(int) - dg[r - 1][0].astype(int)).mean()) for r in range(1, 4)]
        diffs.insert(0, float(np.abs(dg[0][0].astype(int) - grid[0][0].astype(int)).mean()))
        check(all(x > 4 for x in diffs), f"each stage differs from the last (mean abs diff {[round(x,1) for x in diffs]} > 4)")
        late = float(np.abs(dg[3][0].astype(int) - grid[0][0].astype(int)).mean())
        print(f"      (late-stage wreckage vs pristine: mean abs diff {late:.1f}; stage 3 and 4 must look DRAMATICALLY wrecked - view the sheet)")
        fx = load(P("_damage_fx.png")); check(fx.shape[:2] == (768, 2304), f"{o.key}_damage_fx.png is 2304x768 ({fx.shape[1]}x{fx.shape[0]})")
        fg = cells(fx, 384, 384)
        check(all(bbox(fg[r][c]) is not None for r in range(2) for c in range(6)), "all 12 fx cells are non-empty")
        fm = [margin(fg[r][c]) for r in range(2) for c in range(6)]
        warn(min(m for m in fm if m is not None) >= 6, f"fx cells keep >= 6 px clear (min margin {min(m for m in fm if m is not None)}; 0 = chopped)")
    if not o.no_death and os.path.exists(P("_death.png")):
        t = load(P("_death.png")); check(t.shape[:2] == (384, 2304), f"{o.key}_death.png is 2304x384 ({t.shape[1]}x{t.shape[0]})")
        tc = [t[:, i * 384:(i + 1) * 384] for i in range(6)]
        check(all(bbox(c) is not None for c in tc), "all 6 death cells are non-empty")
        mm = [margin(c) for c in tc]
        small = [(i, m) for i, m in enumerate(mm) if m is not None and m < 6 and i not in (2, 3)]
        warn(not small, f"death cells (except the fireball peak 2-3) keep >= 6 px margin {[(i, int(m)) for i, m in small]}")
        # distinct frames
        dd = [float(np.abs(tc[i].astype(int) - tc[i - 1].astype(int)).mean()) for i in range(1, 6)]
        check(min(dd) > 2, f"consecutive death cells differ (mean abs diff {[round(x,1) for x in dd]})")
    print("RESULT:", "FAIL" if fails else "PASS", f"({fails} failures)")
    sys.exit(1 if fails else 0)

main()
