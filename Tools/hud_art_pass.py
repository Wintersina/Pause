#!/usr/bin/env python3
"""HUD art pass (run from the repo root):
  1. hud_board.png (every world): the dark backing fill (the interior flood-filled from the
     centre) gets FILL_ALPHA of its alpha; the frame art is untouched.
  (the shared home / replay glyphs are drawn by Tools/hud_icons.py)
Everything is deterministic; rerun after changing the constants."""
import os, sys
import numpy as np
from PIL import Image
from collections import deque

ROOT = "Pause/Assets/Art/Resources/"
WORLDS = ["Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert"]
FILL_ALPHA = 0.70      # backing opacity (was 1.0)

def fill_pass(world):
    p = f"{ROOT}HudSkins/{world}/hud_board.png"
    im = Image.open(p).convert("RGBA"); a = np.array(im)
    H, W = a.shape[:2]
    c = a[H // 2, W // 2, :3].astype(int)
    same = (np.abs(a[..., :3].astype(int) - c).sum(2) < 70) & (a[..., 3] > 200)
    seen = np.zeros((H, W), bool); q = deque([(H // 2, W // 2)]); seen[H // 2, W // 2] = True
    while q:
        y, x = q.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < H and 0 <= nx < W and same[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = True; q.append((ny, nx))
    # the flood leaves the 1 px antialias rim opaque; cover it too when it is dark like the fill
    grow = seen.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        grow |= np.roll(np.roll(seen, dy, 0), dx, 1) & (a[..., :3].astype(int).sum(2) < 3 * 90)
    a[grow, 3] = (a[grow, 3] * FILL_ALPHA).round().astype(np.uint8)
    Image.fromarray(a).save(p)
    return grow.mean()

if __name__ == "__main__":
    for w in WORLDS:
        f = fill_pass(w) if "--no-fill" not in sys.argv else 0
        print(w, "fill region", round(float(f), 3))
