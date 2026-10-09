#!/usr/bin/env python3
"""Pre-flight + measurement for a world's planetfall set (Backgrounds/Resources/Worlds/<World>/Planetfall/<w>_*.png),
the same seven files Frost/Verdant/Ember have (PlanetfallDef, PlanetfallArt, PlanetfallArtImporter).

  python3 verify_planetfall_art.py DIR PREFIX          e.g.  .../Ember/Planetfall ember

Checks (sizes are contracts - PlanetfallArt cuts cells from them):
  <p>_planet.png            1024x1024 RGBA, disc fully inside with >= 18 px margin
  <p>_planet_limb.png       2048x1024, transparent above the horizon, solid below it
  <p>_cloud_deck.png        2048x1024 tileable on BOTH axes (wrap edges equal)
  <p>_cloud_deck_dark.png   2048x1024 tileable on both axes
  <p>_entry_fx.png          3072x1024 = 6 cells of 512x1024; each cell has the clear OPENING round (256, ~338)
                            (the ship sits in it: ~146 px wide, ~165 tall) and the plasma is not clipped at the cell sides
  <p>_breakthrough.png      5120x1024 = 5 cells of 1024
  <p>_entry_streaks.png     1024x2048 tileable vertically
Then PRINTS the numbers PlanetfallDef needs, measured on the pixels (verify them by eye on the preview and
tune with the Ember comments in PlanetfallDef.cs as the model - the radius is the middle of the atmosphere rim
band, not the outer halo):
  planetCentrePx / planetDiscPx   rough: centroid + equivalent radius of the opaque area (rings/plumes inflate it -
                                  fit a circle to the globe's edge by hand/rays, as the Ember comment says)
  limbApexPx / limbEdgePx         first row with alpha >= 128 at the centre column / at the side columns
  entryShipPx / entryHolePx / entryHoleX[6]   the opening: centre y, width and per-cell centre x of the clear run at y=338
Exit status 1 on any FAIL.
"""
import sys, os
import numpy as np
from PIL import Image

fails = 0
def check(ok, what):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok: fails += 1

def load(p):
    im = Image.open(p)
    return im, np.asarray(im.convert("RGBA")).astype(np.int16)

def main():
    d, p = sys.argv[1], sys.argv[2]
    F = lambda s: os.path.join(d, f"{p}_{s}.png")

    im, a = load(F("planet")); check(im.size == (1024, 1024) and im.mode == "RGBA", f"planet 1024x1024 RGBA ({im.size})")
    ys, xs = np.nonzero(a[..., 3] > 0)
    m = min(xs.min(), ys.min(), 1023 - xs.max(), 1023 - ys.max())
    check(m >= 18, f"planet margin to the canvas edge {m} px (>=18)")
    area = int((a[..., 3] > 128).sum()); cy, cx = ys.mean(), xs.mean()
    print(f"      rough planetCentrePx ({cx:.0f}, {cy:.0f}), equivalent radius {np.sqrt(area/np.pi):.0f} (rings/plumes inflate it)")

    im, a = load(F("planet_limb")); check(im.size == (2048, 1024), f"limb 2048x1024 ({im.size})")
    col = lambda x: np.nonzero(a[:, x, 3] >= 128)[0]
    apex = col(1024)[0] if len(col(1024)) else -1
    edge = [col(x)[0] for x in (0, 2047) if len(col(x))]
    print(f"      limbApexPx ~{apex}  limbEdgePx ~{int(np.mean(edge)) if edge else -1} (first opaque rows; the glow band's middle is a little above)")
    check(apex > 0 and a[:max(apex - 40, 1), 1024, 3].max() == 0, "limb is clear above its horizon")
    check(a[min(apex + 45, 1023):, 1024, 3].min() == 255 if apex > 0 else False, "limb is solid below its horizon")

    for name in ("cloud_deck", "cloud_deck_dark"):
        im, a = load(F(name)); check(im.size == (2048, 1024), f"{name} 2048x1024 ({im.size})")
        check(np.array_equal(a[:, 0], a[:, -1]) and np.array_equal(a[0], a[-1]), f"{name} tiles on both axes (wrap edges equal)")
        check(np.abs(a[:, 0] - a[:, -2]).mean() < 10 and np.abs(a[0] - a[-2]).mean() < 10, f"{name} has no seam jump")

    im, a = load(F("entry_fx")); check(im.size == (3072, 1024), f"entry_fx 3072x1024 ({im.size})")
    holes = []
    for i in range(6):
        c = a[:, i * 512:(i + 1) * 512]
        row = c[338, :, 3] < 16
        # the contiguous clear run containing x=256
        if not row[256]: check(False, f"entry cell {i}: opening not clear at (256, 338)"); holes.append(None); continue
        l = 256
        while l > 0 and row[l - 1]: l -= 1
        r = 256
        while r < 511 and row[r + 1]: r += 1
        holes.append((l, r))
        check(r - l + 1 >= 120, f"entry cell {i}: opening {r-l+1} px wide at y=338 (>=120)")
        check(c[:, 0, 3].max() < 16 and c[:, -1, 3].max() < 16, f"entry cell {i}: plasma not clipped at the cell sides")
    ok = [h for h in holes if h]
    if ok: print("      entryHolePx ~%d  entryHoleX = {%s}  (entryShipPx.y ~338)" % (int(np.mean([r - l + 1 for l, r in ok])), ", ".join("%.1ff" % ((l + r) / 2) for l, r in ok)))

    im, a = load(F("breakthrough")); check(im.size == (5120, 1024), f"breakthrough 5120x1024 ({im.size})")
    check(all(a[:, i * 1024:(i + 1) * 1024, 3].max() > 0 for i in range(5)), "all 5 breakthrough cells are non-empty")
    im, a = load(F("entry_streaks")); check(im.size == (1024, 2048), f"entry_streaks 1024x2048 ({im.size})")
    check(np.array_equal(a[0], a[-1]), "entry_streaks tiles vertically")
    check(int((a[..., 3] > 0).sum()) > 70000, "entry_streaks is dense enough (>70000 opaque texels)")
    print("RESULT:", "FAIL" if fails else "PASS", f"({fails} failures)")
    sys.exit(1 if fails else 0)
main()
