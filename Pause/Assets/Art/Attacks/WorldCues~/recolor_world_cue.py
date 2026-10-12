"""NO PINK OUTSIDE SPACE (user, 2026-10-11): replace the hostile pink of every painted attack asset of a non-Space world
with that world's own cue colour (same table as WorldCue.cs / MineCue / MineBeams~/recolor_nopink.py).

usage: python3 recolor_world_cue.py            (re-runs every job below from the saved originals; idempotent)
       python3 recolor_world_cue.py --check    (prints the pink share of each target now)

Originals (the pink art) are saved once to pink_original/<Resources path>. Two modes:
  attack  shots, beams, lasers, fx: pink pixels take the cue hue, saturation capped, value lifted to the cue's floor (a bright cue core)
  hull    boss bodies, damage and death strips: pink pixels take the cue hue at their OWN value (shading is kept, so the body does not glow)
Pointwise (no neighbourhood), so cell grids, anchors, outlines and the x2 nearest round trip are untouched.
"""
import colorsys, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
RES = HERE.parents[1] / "Resources"          # Assets/Art/Resources
KEEP = HERE / "pink_original"

# world -> (hue deg, saturation cap, minimum value)   keep equal to MineCue / recolor_nopink.py / WorldCue.cs
CUE = {
    "frost":   (195, .40, .90),
    "verdant": (135, .55, .88),
    "ember":   (32,  .85, .92),
    "tide":    (188, .38, .90),
    "storm":   (258, .38, .92),
    "desert":  (45,  .35, .92),
}

# also the five rail-mine beam atlases (Attacks/<World>/<w>_attack_minebeam.png, Ember's mineflame) -- redone from MineBeams~/*/pink_original
# by MineBeams~/recolor_nopink.py with the same mask (run it after this one if both change).
# (world, file under Resources, mode, optional pixel rows (y0, y1) from the top)
JOBS = []
for n in ("laser", "laserbody", "lasertell"):
    for w in ("Ember", "Frost", "Verdant"):
        JOBS.append((w.lower(), f"Attacks/{w}/{w.lower()}_attack_{n}.png", "attack", None))
for n in ("shots", "lash", "log"):
    JOBS.append(("verdant", f"Attacks/Verdant/verdant_attack_{n}.png", "attack", None))
for w in ("Ember", "Frost", "Verdant"):
    JOBS.append((w.lower(), f"Bosses/{w}_shots.png", "attack", None))
for w in ("Ember", "Frost"):
    JOBS.append((w.lower(), f"Bosses/{w}_card.png", "hull", None))
    JOBS.append((w.lower(), f"Bosses/{w}.png", "hull", None))
    JOBS.append((w.lower(), f"Bosses/{w}_damage.png", "hull", None))
    JOBS.append((w.lower(), f"Bosses/{w}_damage_fx.png", "hull", None))
JOBS.append(("ember", "Bosses/Ember_death.png", "hull", None))
JOBS.append(("frost", "Bosses/Frost_death.png", "hull", None))
JOBS.append(("verdant", "Bosses/Verdant_damage.png", "hull", None))
JOBS.append(("ember", "BossAttackFx/boss_attack_fx.png", "attack", (144, 192)))   # row 3 = Ember's attack fx (row 0 = Space keeps its pink)

def hsv(a):
    rgb = a[..., :3].astype(np.float32) / 255
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-4), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dd = np.maximum(d, 1e-6)
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60 % 360
    return h, s, mx

def pink_mask(a):
    h, s, v = hsv(a)
    d = v * s   # chroma
    return (a[..., 3] > 0) & (d >= .035) & (v > .25) & (h >= 280) & (h < 352)   # saturated pink AND pale pinkish-white / lavender-white

def remap(a, world, mode):
    hue, cap, vmin = CUE[world]
    h, s, v = hsv(a)
    m = pink_mask(a)
    out = a.copy()
    ys, xs = np.where(m)
    for y, x in zip(ys, xs):
        vv = float(v[y, x]) if mode == "hull" else max(float(v[y, x]), vmin)
        sat = min(max(float(s[y, x]) * (.9 if mode == "hull" else .8), .10 if mode == "attack" else 0.0), cap if mode == "attack" else max(cap, .8))
        if mode == "attack" and v[y, x] >= .95: sat *= .75
        rr, gg, bb = colorsys.hsv_to_rgb(hue / 360, sat, vv)
        out[y, x, :3] = (round(rr * 255), round(gg * 255), round(bb * 255))
    return out, int(m.sum())

def share(a):
    op = (a[..., 3] > 76).sum()
    return 100 * (pink_mask(a) & (a[..., 3] > 76)).sum() / max(op, 1)

if __name__ == "__main__":
    for world, rel, mode, rows in JOBS:
        path = RES / rel
        bak = KEEP / rel
        if "--check" in sys.argv:
            print(f"{share(np.array(Image.open(path).convert('RGBA'))):5.1f}%  {rel}")
            continue
        if not bak.exists():
            bak.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(path, bak)
        a = np.array(Image.open(bak).convert("RGBA"))
        if rows:
            part, n = remap(a[rows[0]:rows[1]], world, mode)
            out = a.copy(); out[rows[0]:rows[1]] = part
        else:
            out, n = remap(a, world, mode)
        Image.fromarray(out, "RGBA").save(path)
        print(f"{world:8s} {mode:6s} {n:7d} px  {rel}")
