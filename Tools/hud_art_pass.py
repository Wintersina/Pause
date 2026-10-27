#!/usr/bin/env python3
"""HUD art pass (run from the repo root):
  1. hud_board.png (every world): the dark backing fill (the interior flood-filled from the
     centre) gets FILL_ALPHA of its alpha; the frame art is untouched.
  2. hud_home.png / hud_replay.png (every world): thin hollow outline glyphs derived from the
     quick-action glyph art (silhouette outline in the glyph's fill colour, fitted to one size).
Everything is deterministic; rerun after changing the constants."""
import os, sys, uuid, shutil
import numpy as np
from PIL import Image
from collections import deque

ROOT = "Pause/Assets/Art/Resources/"
WORLDS = ["Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert"]
FILL_ALPHA = 0.70      # backing opacity (was 1.0)
GLYPH_BOX = 190        # longest side of a glyph inside its 256 px tile
STROKE = 10             # outline thickness in 256 px tile pixels (thin: ~2.5 px at 1080 wide)

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

# Worlds whose backdrop is bright (clouds, foliage, shallows): their glyphs use the art's own dark
# outline colour instead of the light fill colour, so the thin line stays visible.
DARK = {"Frost": None, "Verdant": None}

def erode(alpha, r):
    out = alpha.copy(); H, W = alpha.shape
    pad = np.pad(alpha, r, mode="constant")
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r:
                out = np.minimum(out, pad[r + dy:r + dy + H, r + dx:r + dx + W])
    return out

def glyph_pass(world, kind):
    src = f"{ROOT}QuickActions/QuickAction_{kind}_glyph.png" if world == "Space" else f"{ROOT}HudSkins/{world}/qa_{kind}_glyph.png"
    im = Image.open(src).convert("RGBA")
    a = np.array(im).astype(float)
    mask = a[..., 3] > 128
    ys, xs = np.where(mask)
    # fill colour = median colour of the silhouette's deep interior
    deep = erode((a[..., 3] > 128).astype(float), 12) > .5
    if deep.sum() < 50: deep = erode((a[..., 3] > 128).astype(float), 6) > .5
    col = np.median(a[deep][:, :3], axis=0)
    if world in DARK:
        ring = mask & ~(erode(mask.astype(float), 5) > .5)
        col = np.array(DARK[world]) if DARK[world] else np.median(a[ring][:, :3], axis=0)
        col = col * .75     # a notch darker than the art's own outline
    crop = im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    s = GLYPH_BOX / max(crop.size)
    crop = crop.resize((max(1, round(crop.width * s)), max(1, round(crop.height * s))), Image.LANCZOS)
    tile = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    tile.alpha_composite(crop, ((256 - crop.width) // 2, (256 - crop.height) // 2))
    al = np.array(tile).astype(float)[..., 3] / 255.0
    al = np.clip((al - .4) / .2, 0, 1)      # crisp silhouette: drop faint interior shading
    ero = erode(al, STROKE // 2)           # stroke = the silhouette's outer ring, STROKE px thick
    stroke = np.clip(al - ero * 1.0, 0, 1)
    out = np.zeros((256, 256, 4), np.uint8)
    out[..., :3] = col.astype(np.uint8)
    out[..., 3] = (stroke * 255).round().astype(np.uint8)
    dst = f"{ROOT}HudSkins/{world}/hud_{kind}.png"
    Image.fromarray(out).save(dst)
    meta = dst + ".meta"
    if not os.path.exists(meta):
        tmpl = f"{ROOT}HudSkins/Frost/qa_{kind}_glyph.png.meta"
        t = open(tmpl).read()
        import re
        t = re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, t)
        t = re.sub(r"spriteID: [0-9a-f]{32}", "spriteID: " + uuid.uuid4().hex, t)
        open(meta, "w").write(t)
    return tuple(int(v) for v in col)

if __name__ == "__main__":
    for w in WORLDS:
        f = fill_pass(w) if "--no-fill" not in sys.argv else 0
        h = glyph_pass(w, "home"); r = glyph_pass(w, "replay")
        print(w, "fill region", round(float(f), 3), "home", h, "replay", r)
