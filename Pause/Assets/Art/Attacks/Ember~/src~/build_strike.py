#!/usr/bin/env python3
"""Ember "eruption columns" (the Cinder Drake's Legacy strike special): ember_attack_strike.png, built by
script from Ember's already-approved, installed attack art (no repaint, no generation):

    Resources/Attacks/Ember/ember_attack_laserbody.png   four 128 x 256 flame-beam frames, seamless top to bottom
    Resources/Attacks/Ember/ember_attack_mineflame.png   row 1 pilot flames a-d, row 2 muzzle bursts / impact a,b, row 3 impact c

Layout the game reads (AttackArt.StrikeBody / StrikeGlyph / StrikeBurst; docs world-attacks-art.md 3.3), 768 x 512 RGBA:
    row 0      six 128 x 384 column-body frames, played ONCE at 24 fps over the .25 s live window (AttackStrike.DrawBody),
               tiled foot to top, so every frame is seamless top to bottom:
               0 ignite (the beam pinched to ~55 % width, white-hot)  1-3 full burn (laserbody frames 1, 2, 3)
               4-5 dying (laserbody 0 / 1 at 62 % / 30 % alpha, cooled toward red: the source art is soft-edged)
    row 1      (y 384) 128 x 128: lane glyph a, b (pilot flame c, d), ground burst x3 (impact a, b, c), one reserved (empty)

The body frames are the 256 px beam tiles stretched NEAREST to 384 (every other row doubled): the stretch keeps the tile
seamless (row 383 meets row 0 exactly as row 255 met row 0). The hit is the middle 46 px; the beam's flame is ~88 px wide.

INSTALLED (2026-11-27) as Resources/Attacks/Ember/ember_attack_strike.png (.meta copied from ember_attack_laserbody.png.meta,
new GUID: Point filter, no mips, not readable, ASTC on Android / iOS). The column tiles at 3 u (384 px at 128 ppu); AttackStrike.DrawBody
cuts the last tile at the column's top (AttackStrike.Cropped), so the 4.2 u geyser never draws past its hitbox
(AttackFairnessTest.ArtSlots checks it). Rebuild + reinstall:

    python3 build_strike.py ../../../Resources/Attacks/Ember/ember_attack_strike.png

    python3 build_strike.py [OUT.png]
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]
SRC = ART / "Resources/Attacks/Ember"
OUT = Path(sys.argv[1]) if len(sys.argv) > 1 else HERE / "candidate_strike.png"

body = np.asarray(Image.open(SRC / "ember_attack_laserbody.png").convert("RGBA"))
mine = np.asarray(Image.open(SRC / "ember_attack_mineflame.png").convert("RGBA"))


def beam(i):
    return body[:, i * 128:(i + 1) * 128]


def stretch(a):                                   # 256 -> 384 rows, NEAREST: rows 0,0,1,2,2,3,...
    rows = (np.arange(384) * 256 // 384)
    return a[rows]


def pinch(a, k):                                  # squeeze the flame toward the centre column, white-hot
    out = np.zeros_like(a)
    for x in range(128):
        sx = int(round(64 + (x + .5 - 64) / k - .5))
        if 0 <= sx < 128: out[:, x] = a[:, sx]
    rgb = out[..., :3].astype(int)
    out[..., :3] = np.clip(rgb + (255 - rgb) * .35, 0, 255).astype(np.uint8)
    return out


def dying(a, k):                                  # the beam art is soft-edged (154 alpha levels): fade it by alpha, cooled
    out = a.copy()
    out[..., 3] = (out[..., 3].astype(float) * k).astype(np.uint8)
    rgb = out[..., :3].astype(float)
    out[..., :3] = np.clip(rgb * np.array([1.0, .8, .7]) * (.55 + .45 * k), 0, 255).astype(np.uint8)
    return out


def cell(r, c):
    return mine[r * 128:(r + 1) * 128, c * 128:(c + 1) * 128] if r else None


frames = [pinch(stretch(beam(0)), .55), stretch(beam(1)), stretch(beam(2)), stretch(beam(3)),
          dying(stretch(beam(0)), .62), dying(stretch(beam(1)), .3)]
atlas = np.zeros((512, 768, 4), np.uint8)
for i, f in enumerate(frames):
    atlas[:384, i * 128:(i + 1) * 128] = f
# row 1: glyph a,b = pilot flame c,d (mineflame row 1 at y 384, cols 2,3); burst = impact a,b (row 2 y 512, cols 4,5), impact c (row 3 y 640, col 0)
row1 = [mine[384:512, 256:384], mine[384:512, 384:512], mine[512:640, 512:640], mine[512:640, 640:768], mine[640:768, 0:128]]
for i, c in enumerate(row1):
    atlas[384:512, i * 128:(i + 1) * 128] = c
Image.fromarray(atlas, "RGBA").save(OUT, optimize=True)
# seam check: each body frame's last row against its first (the tile meets itself)
for i, f in enumerate(frames[:4]):
    print(f"frame {i}: seam |row383-row0| = {np.abs(f[-1].astype(int) - f[0].astype(int)).mean():.1f}")
print("wrote", OUT)
