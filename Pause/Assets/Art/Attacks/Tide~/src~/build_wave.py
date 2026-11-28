#!/usr/bin/env python3
"""Tide "surf wave" (the Iron Kraken's Legacy wave special): Resources/Attacks/Tide/tide_attack_wave.png, built by script
from the two user-approved drawings (2026-11-27), no repaint and no generation:

    approved_wave_band.png   512 x 128, the surf band (meant to tile left-right; as drawn its ends do NOT meet)
    approved_wave_gap.png    96 x 96, the gap marker (a brass ring round two water arrows)

Layout the game reads (AttackArt.WaveBody / WaveCapL / WaveCapR / WaveGapMarker; AttackWave), 512 x 384 RGBA, 128 ppu:
    rows 0-2   three 512 x 96 band frames (y 0, 96, 192), seamless left-right, played 0, 1, 2, 1 as a 12 fps ping-pong loop
               (AttackArt.WaveBody maps the loop's fourth step back onto frame 1: the attack-art memory budget,
               MineFlameArtTest, has no room for a fourth band). AttackWave tiles them across each segment
               (SpriteDrawMode.Tiled) at .75 u thick on its .4 u hit (1.875 x, under the 2 x cap).
    row 3      (y 288) 96 x 96 cells: capL (x 0), capR (x 96), gap marker a (x 192), b (x 288), the rest empty.

Steps
  * seamless   the band and a copy rolled half its width are quilted (Efros-Freeman): in a strip near each end a
               minimum-difference vertical cut decides, row by row, which of the two drawings a pixel comes from, so the
               tile's ends are the copy's middle (columns 255 | 256, which already meet) and nothing is blended or
               dithered: every pixel is a drawn pixel.
  * height     128 -> 96 rows, NEAREST (every fourth row dropped), keeping the drawing's whole composition (dark wash on
               top, spray, crest lines, the white foam bulk on the lower, leading edge).
  * palette    no pink (none in the drawings); saturated pixels in the shield-cyan band (hue 172-186, s*v >= .1) are
               turned to hue 190 (Tide's cue 188 side), same s / v; pale sea-foam whites (hue ~168) stay.
  * loop       a 4-step ping-pong (0, 1, 2, 1) so the last step back to 0 is one step like every other: the foam bulk
               (lower rows) slides 0, 3, 6 px, the crest band 0, -2, -4 px, the top wash stays; whole rows are rolled, so the
               tile stays seamless; half the isolated spray specks twinkle out on frame 1, so all three drawings differ.
  * caps       a breaking-foam lip made from the band's own end: the band's last 48 columns with a foam wall drawn
               over their gap-side 10-14 px (rows where the band has paint), a ragged inner profile and a few drops
               thrown back over the band. AttackWave centres a cap ON the gap edge, so every cap pixel sits on the
               band's side of the cell's middle (capL: x < 48, capR: its mirror, x >= 48): nothing is drawn in the gap.
  * marker     a = the approved drawing (palette rule above); b = the same with its water lit (+ light) for the 8 fps
               blink. AttackWave turns it +-90 deg so the arrows point into the gap.

    python3 build_wave.py [OUT.png] [--preview PNG]
"""
import colorsys
import sys
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
OUT = ART / "Resources/Attacks/Tide/tide_attack_wave.png"
BAND_H, CELL = 96, 96
BANDS = 3                   # stored band frames; the loop plays 0, 1, 2, 1
AUX = BANDS * BAND_H        # the caps / markers row
FOAM_DX = (0, 3, 6, 3)
CREST_DX = (0, -2, -4, -2)


def load(name):
    a = np.array(Image.open(HERE / name).convert("RGBA"))
    a[..., 3] = np.where(a[..., 3] > 127, 255, 0)
    a[a[..., 3] == 0] = 0
    return a


def recolor(a):
    """Shield-cyan band (172..186) -> hue 190, keeping s and v; everything else untouched."""
    a = a.copy()
    H, W = a.shape[:2]
    for y in range(H):
        for x in range(W):
            if a[y, x, 3] == 0: continue
            r, g, b = (int(c) / 255 for c in a[y, x, :3])
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            if 172 <= h * 360 < 186 and s * v >= .1:
                r, g, b = colorsys.hsv_to_rgb(190 / 360, s, v)
                a[y, x, :3] = (round(r * 255), round(g * 255), round(b * 255))
    return a


def diff(a, b):
    """Per-pixel mismatch between two drawings (alpha mismatch costs most)."""
    d = np.abs(a[..., :3].astype(int) - b[..., :3].astype(int)).sum(-1)
    return d + 600 * ((a[..., 3] > 0) != (b[..., 3] > 0))


def cut(cost):
    """Minimum-cost top-to-bottom path through cost (H x W), 8-connected: the column index per row."""
    H, W = cost.shape
    acc = cost.astype(float).copy()
    back = np.zeros((H, W), int)
    for y in range(1, H):
        for x in range(W):
            lo, hi = max(0, x - 1), min(W, x + 2)
            k = int(np.argmin(acc[y - 1, lo:hi])) + lo
            back[y, x] = k
            acc[y, x] += acc[y - 1, k]
    path = np.zeros(H, int)
    path[-1] = int(np.argmin(acc[-1]))
    for y in range(H - 1, 0, -1):
        path[y - 1] = back[y, path[y]]
    return path


def seamless(a):
    W = a.shape[1]
    b = np.roll(a, W // 2, 1)                      # the copy: its ends are a's middle, which meet
    d = diff(a, b)
    out = a.copy()
    L0, L1 = 40, 168                               # left strip: b left of the cut, a right of it
    pl = cut(d[:, L0:L1]) + L0
    R0, R1 = W - 168, W - 40                       # right strip: a left of the cut, b right of it
    pr = cut(d[:, R0:R1]) + R0
    for y in range(a.shape[0]):
        out[y, :pl[y]] = b[y, :pl[y]]
        out[y, pr[y]:] = b[y, pr[y]:]
    return out


def squash(a):
    rows = (np.arange(BAND_H) * a.shape[0]) // BAND_H
    return a[rows]


def bands(a):
    """Row bands: top wash (still), crest (CREST_DX), foam bulk (FOAM_DX) -- by where the white foam takes over."""
    light = (a[..., 3] > 0) & (a[..., :3].min(-1) > 200)
    share = light.mean(1)
    foam0 = int(np.argmax(share > .45))            # first row that is mostly foam
    crest0 = max(8, foam0 - 26)
    return crest0, foam0


def frames(base):
    crest0, foam0 = bands(base)
    rng = np.random.default_rng(188)
    op = base[..., 3] > 0
    # isolated specks (no opaque 4-neighbour) in the top two thirds: the spray that twinkles
    nb = np.zeros_like(op)
    for dy, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        nb |= np.roll(np.roll(op, dy, 0), dx, 1)
    speck = op & ~nb
    speck[foam0:] = False
    ys, xs = np.nonzero(speck)
    pick = rng.random(len(ys)) < .5
    out = []
    for f in range(4):
        a = base.copy()
        a[crest0:foam0] = np.roll(base[crest0:foam0], CREST_DX[f], 1)
        a[foam0:] = np.roll(base[foam0:], FOAM_DX[f], 1)
        if f % 2:                                  # half the specks go out (the other half on frame 3), a lit pixel beside the rest
            for y, x, p in zip(ys, xs, pick):
                if p == (f == 1): a[y, x] = 0
                elif a[y, (x + 1) % a.shape[1], 3] == 0: a[y, (x + 1) % a.shape[1]] = (240, 255, 250, 255)
        out.append(a)
    return out, crest0, foam0


FOAM = [(246, 254, 253), (216, 252, 245), (193, 242, 233), (143, 208, 196)]
INK = (18, 36, 43)


def cap_left(band, crest0, foam0):
    """The band's last 48 columns with a breaking-foam lip on its gap side, nothing at x >= 48. The lip follows the band:
    sparse spray specks over the top wash (rows < crest0), a thin pale edge through the crest rows, and a full ragged
    foam curl over the white foam bulk (rows >= foam0), deepest at the bulk's middle."""
    c = np.zeros((CELL, CELL, 4), np.uint8)
    c[:, :48] = band[:, -48:]
    rng = np.random.default_rng(4)
    for y in range(CELL):
        if y < crest0:                                                       # spray: specks, denser toward the edge
            for x in range(40, 48):
                if rng.random() < (x - 39) / 14: c[y, x] = (*FOAM[0 if rng.random() < .6 else 1], 255)
            continue
        if y < foam0:                                                        # crest: a thin pale edge
            depth = int(3 + 3 * (y - crest0) / max(1, foam0 - crest0) + rng.integers(0, 2))
        else:                                                                # foam: the curl, deepest mid-bulk
            k = (y - foam0) / max(1, CELL - 1 - foam0)
            depth = int(round(8 + 6 * np.sin(k * np.pi) + 2 * np.sin(y * 1.7) + rng.integers(0, 2)))
        x0 = 47 - depth
        for x in range(x0, 48):
            t = (x - x0) / max(1, depth)
            col = FOAM[0] if t > .55 else FOAM[1] if t > .3 else FOAM[2]
            c[y, x] = (*col, 255)
        if rng.random() < .5: c[y, x0] = (*FOAM[3], 255)                    # a teal shade on the curl's inner face
        if y >= foam0 and rng.random() < .3: c[y, max(0, x0 - 1)] = (*INK, 255)
    for _ in range(22):                                                      # drops thrown back over the band
        y = int(rng.integers(crest0 - 8, CELL - 2)); x = int(rng.integers(18, 34))
        c[y, x] = (*FOAM[0], 255)
        if rng.random() < .5: c[y + 1, x] = (*FOAM[1], 255)
    c[:, 48:] = 0
    return c


def lit(a, k=.4):
    out = a.copy()
    rgb = out[..., :3].astype(float)
    water = (out[..., 3] > 0) & (rgb[..., 2] > rgb[..., 0] + 20)            # the water, not the brass
    rgb[water] = rgb[water] + (255 - rgb[water]) * k
    out[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return out


def build():
    band = recolor(squash(seamless(load("approved_wave_band.png"))))
    fr, crest0, foam0 = frames(band)
    gap = recolor(load("approved_wave_gap.png"))
    atlas = np.zeros((AUX + CELL, 512, 4), np.uint8)
    for i, f in enumerate(fr[:BANDS]):                 # (frame 3 equals frame 1 but for its specks: played as frame 1)
        atlas[i * BAND_H:(i + 1) * BAND_H] = f
    capL = cap_left(fr[0], crest0, foam0)
    atlas[AUX:, 0:96] = capL
    atlas[AUX:, 96:192] = capL[:, ::-1]
    atlas[AUX:, 192:288] = gap
    atlas[AUX:, 288:384] = lit(gap)
    return atlas, (crest0, foam0)


def checks(atlas):
    bad = []
    for i in range(BANDS):
        f = atlas[i * BAND_H:(i + 1) * BAND_H].astype(int)
        seam = np.abs(f[:, 0] - f[:, -1]).sum(-1)
        # the tile meets itself: its last column continues into its first like any two neighbouring columns do
        inner = np.mean([np.abs(f[:, x] - f[:, x + 1]).sum(-1).mean() for x in range(0, 511, 7)])
        if seam.mean() > inner * 1.6: bad.append(f"frame {i} seam {seam.mean():.0f} vs neighbours {inner:.0f}")
    caps = atlas[AUX:, :192]
    if (caps[:, 48:96, 3] > 0).any() or (caps[:, 96:144, 3] > 0).any(): bad.append("a cap draws into the gap half")
    for i in range(BANDS - 1):
        if (atlas[i * BAND_H:(i + 1) * BAND_H] == atlas[(i + 1) * BAND_H:(i + 2) * BAND_H]).all(): bad.append(f"frames {i}, {i+1} equal")
    return bad


def main():
    argv = sys.argv[1:]
    prev = Path(argv[argv.index("--preview") + 1]) if "--preview" in argv else None
    args = [a for i, a in enumerate(argv) if not a.startswith("--") and not (i > 0 and argv[i - 1] == "--preview")]
    out = Path(args[0]) if args else OUT
    atlas, (c0, f0) = build()
    Image.fromarray(atlas, "RGBA").save(out, optimize=True)
    print(f"crest rows {c0}..{f0 - 1}, foam rows {f0}..95; wrote {out}")
    for b in checks(atlas): print("CHECK FAILED:", b)
    if prev:
        p = prev
        H = atlas.shape[0]
        bg = Image.new("RGBA", (1024 + 40, H * 2 + 420), (24, 26, 40, 255))
        im = Image.fromarray(atlas, "RGBA").resize((1024, H * 2), Image.NEAREST)
        bg.alpha_composite(im, (20, 0))
        # the band tiled twice (the seam in the middle) and a gap with both caps, at 2x
        f0i = Image.fromarray(atlas[:96], "RGBA").resize((1024, 192), Image.NEAREST)
        strip = Image.new("RGBA", (1024, 192))
        strip.alpha_composite(f0i.crop((512, 0, 1024, 192)), (0, 0)); strip.alpha_composite(f0i.crop((0, 0, 512, 192)), (512, 0))
        bg.alpha_composite(strip, (20, H * 2 + 20))
        # a gap between two band ends with their caps, at 2x: [band end][capL] gap [capR][band start]
        g = Image.new("RGBA", (1024, 192))
        endL = Image.fromarray(atlas[:96, -144:], "RGBA").resize((288, 192), Image.NEAREST)
        startR = Image.fromarray(atlas[:96, :144], "RGBA").resize((288, 192), Image.NEAREST)
        g.alpha_composite(endL, (0, 0)); g.alpha_composite(startR, (736, 0))
        g.alpha_composite(Image.fromarray(atlas[AUX:, 0:96], "RGBA").resize((192, 192), Image.NEAREST), (288 - 96, 0))
        g.alpha_composite(Image.fromarray(atlas[AUX:, 96:192], "RGBA").resize((192, 192), Image.NEAREST), (736 - 96, 0))
        bg.alpha_composite(g, (20, H * 2 + 230))
        bg.convert("RGB").save(p)
        print("preview", p)


if __name__ == "__main__":
    main()
