"""Replace the hostile pink of a rail-mine atlas with the world's own cue colours (user, 2026-10-11: no pink on any rail mine attack).

usage: python3 recolor_nopink.py <world> <atlas.png> [--out file]
Originals are saved once to <dir>/pink_original/<name>. Pointwise (so the x2 nearest round trip still holds).
A pixel is "pink family" when s > .15, v > .25 and hue 285..352 (the pink rim and pink-white specks; Ember's red-orange flame edge, hue 19, is untouched).
The same constants live in C# (MineCue, RailMineLaser.cs): keep them equal.
"""
import colorsys, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image

# world -> (hue deg, saturation cap, minimum value)
CUE = {
    "space":   (215, .45, .85),   # ion white-blue
    "frost":   (195, .40, .90),   # icy white-cyan
    "verdant": (135, .55, .88),   # bright green-white (jade, clear of repair-green 82)
    "ember":   (32,  .85, .92),   # amber / orange-white
    "tide":    (188, .38, .90),   # aqua-white
    "storm":   (258, .38, .92),   # electric white-violet (pale: clear of the capacitor violet)
    "desert":  (45,  .35, .92),   # sun-white
}

def remap(a, world):
    hue, cap, vmin = CUE[world]
    rgb = a[:, :, :3].astype(np.float32) / 255
    mx = rgb.max(2); mn = rgb.min(2); d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-4), 0)
    h = np.zeros(mx.shape, np.float32)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    ix = (mx == r) & (d > 0); h[ix] = ((g[ix] - b[ix]) / d[ix]) % 6
    ix = (mx == g) & (d > 0) & (mx != r); h[ix] = (b[ix] - r[ix]) / d[ix] + 2
    ix = (mx == b) & (d > 0) & (mx != r) & (mx != g); h[ix] = (r[ix] - g[ix]) / d[ix] + 4
    h = (h * 60) % 360
    pink = (a[:, :, 3] > 0) & (s > .15) & (mx > .25) & (h >= 285) & (h < 352)
    out = a.copy()
    ys, xs = np.where(pink)
    for y, x in zip(ys, xs):
        v = max(float(mx[y, x]), vmin)
        sat = min(float(s[y, x]) * .8, cap)
        # keep a little of the original lightness structure: paler pixels stay paler
        rr, gg, bb = colorsys.hsv_to_rgb(hue / 360, sat * (1.0 if mx[y, x] < .95 else .6), v)
        out[y, x, :3] = (round(rr * 255), round(gg * 255), round(bb * 255))
    return out, int(pink.sum())

if __name__ == "__main__":
    world, path = sys.argv[1], Path(sys.argv[2])
    keep = path.parent / "pink_original"
    keep.mkdir(exist_ok=True)
    bak = keep / path.name
    if not bak.exists(): shutil.copy(path, bak)
    a = np.array(Image.open(bak).convert("RGBA"))
    out, n = remap(a, world)
    dest = Path(sys.argv[4]) if len(sys.argv) > 4 and sys.argv[3] == "--out" else path
    Image.fromarray(out, "RGBA").save(dest)
    print(world, dest, "recoloured px:", n)
