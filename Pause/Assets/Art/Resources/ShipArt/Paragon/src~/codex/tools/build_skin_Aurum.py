"""Register image-generated Aurum paint to the approved Paragon flipbook.

The three painted inputs are imagegen redraws.  Their 1254 px canvases are
sampled with nearest neighbour.  The approved sheet supplies the exact key,
contour, blue machinery, ink, glints, and the white hit pose.
"""

from pathlib import Path

import numpy as np
from PIL import Image


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
GREEN = np.array((0, 255, 0), dtype=np.uint8)


def hsv(rgb):
    a = rgb.astype(np.float32) / 255.0
    hi = a.max(axis=2)
    lo = a.min(axis=2)
    span = hi - lo
    safe = np.maximum(span, 1e-8)
    r, g, b = a.transpose(2, 0, 1)
    h = np.zeros_like(hi)
    use = (span > 0) & (hi == r)
    h[use] = ((g[use] - b[use]) / safe[use]) % 6
    use = (span > 0) & (hi == g)
    h[use] = (b[use] - r[use]) / safe[use] + 2
    use = (span > 0) & (hi == b)
    h[use] = (r[use] - g[use]) / safe[use] + 4
    return h * 60, span / np.maximum(hi, 1e-8), hi


def build(state):
    ref = np.asarray(Image.open(HERE / f"skinref_state{state}.png").convert("RGB"))
    painted = Image.open(HERE / f"skin_Aurum_painted_state{state}.png").convert("RGB")
    painted = np.asarray(painted.resize((1536, 1536), Image.Resampling.NEAREST))
    rh, rs, rv = hsv(ref)
    ph, ps, pv = hsv(painted)
    ship = np.any(ref != GREEN, axis=2)
    candidate_gold = (ph >= 29) & (ph <= 59) & (ps > .33) & (pv > .18)
    old_red = ((rh <= 34) | (rh >= 350)) & (rs > .40) & (rv > .19)
    ref_blue = (ref[:, :, 2].astype(float) > ref[:, :, 0] * 1.18) & (
        ref[:, :, 2].astype(float) > ref[:, :, 1] * .78) & (ref[:, :, 2] > 38)
    white_glint = (ref.min(axis=2) > 215) & (rs < .27)
    ink = ref.max(axis=2) < 42
    protected = ~ship | ref_blue | white_glint | ink
    # Preserve the damage's hot orange cracks and sparks.  On the intact sheet
    # these same hues are red armour and therefore eligible for gold.
    if state:
        yy, xx = np.indices((1536, 1536))
        lx, ly = xx % 512, yy % 512
        damage = ((state == 1) & (lx < 112) & (ly > 180)) | (
            (state == 2) & (lx > 310) & (ly > 175))
        hot_crack = (rh < 26) & (rs > .72) & (rv > .48)
        protected |= damage & hot_crack & ~candidate_gold
    # The generated paint supplies the warm-gold material and texture.  Only
    # pixels associated with red armour in the approved art are repainted.
    repaint = ship & ~protected & (candidate_gold | old_red)
    out = ref.copy()
    out[repaint & candidate_gold] = painted[repaint & candidate_gold]
    missed = repaint & old_red & ~candidate_gold
    # Rare reference red pixels that the model missed receive a representative
    # gold swatch from its own painting, matched by the old pixel's brightness.
    tones = painted[candidate_gold]
    levels = np.percentile(tones.mean(axis=1), [8, 30, 53, 76, 94])
    swatches = np.array([tones[np.argmin(np.abs(tones.mean(axis=1) - v))]
                         for v in levels], dtype=np.uint8)
    idx = np.clip((ref[missed].mean(axis=1) / 256 * 5).astype(int), 0, 4)
    out[missed] = swatches[idx]
    # The approved red armour contains a few purple-red shadow pixels.  Move
    # those into the gold shadow ramp, and move any blue-side purple to navy.
    oh, os, ov = hsv(out)
    purple = ship & (oh > 280) & (oh < 345) & (os > .4) & (ov > .25)
    purple_blue = purple & ref_blue
    purple_armour = purple & ~ref_blue
    out[purple_armour] = swatches[0]
    out[:, :, 0][purple_blue] = np.minimum(
        out[:, :, 0][purple_blue], out[:, :, 2][purple_blue] * .52).astype(np.uint8)
    # Keep the hit flash exactly bone-white, as supplied in the approved sheet.
    out[1024:1536, 1024:1536] = ref[1024:1536, 1024:1536]
    out[~ship] = GREEN
    image = Image.fromarray(out, "RGB").resize((3072, 3072), Image.Resampling.NEAREST)
    path = OUT / f"skin_Aurum_state{state}.png"
    image.save(path, optimize=True)
    print(path, "repainted", round(repaint.mean() * 100, 2), "% at reference size")


if __name__ == "__main__":
    for number in range(3):
        build(number)
