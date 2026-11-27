#!/usr/bin/env python3
"""Paragon-standard enemy redraw: one approved PixelLab drawing -> the 7-cell flipbook, installed.

    python3 build.py <key> <approved.png> [--no-install]

The approved drawing (80 px PixelLab canvas, style refs Paragon + Bastion, see prompts.json) is:
  * trimmed and scaled with NEAREST so its longest edge matches the live strip's frame 0 exactly
    (EnemyRosterTest holds the silhouette within 15%, and the collider stays as it is), centred in the cell;
  * animated without repainting:
      0-3 idle   a 1 px bob and a flicker on the glow/flame pixels (the world's signal hue)
      4-5 tell   the glow ramps hot and throws spark crosses off the signal pixels
      6   hit    a white flash over the hull plus a few chips
Hard alpha everywhere (0 or 255). The old strip is kept in old/<key>.png the first time.
Prints the muzzle check: the shot point (EnemyBehaviours muzzle x TierScale below centre) must sit on or just
ahead of the hull's front (the bottom of the drawing, the nose faces the player).
Then run the world's build_<world>_enemy_death.py <key> to rebuild the death strip from the new idle cell 3.
"""
import json, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]                                   # .../Assets/Art
LIVE = ART / "Resources/Enemies"
OLD = HERE / "old"; OUT = HERE / "strips"; KEYS = HERE / "keys"
FRAMES = 7
TIER = 1.2
FRAME_WORLD = {"fighter": .9 * TIER, "chaser": .95 * TIER, "alien": .67 * TIER, "big": 1.3 * TIER, "rock": .66 * TIER}
FRAME_SCALE = {"ember_alien": 1.36}
# The world's cue hue (WorldCue.Hue: the enemy shots' colour); the art's glow is drawn in it, so the band finds the glow.
CUE = {"space": 318, "frost": 195, "verdant": 135, "ember": 32, "tide": 188, "storm": 258, "desert": 45}
SIGNAL_HUE = {w: (h - 30, h + 30) for w, h in CUE.items()}
SPARK = {"space": (255, 120, 220, 255), "frost": (190, 235, 255, 255), "verdant": (190, 255, 170, 255), "ember": (255, 200, 90, 255),
         "tide": (150, 255, 240, 255), "storm": (205, 180, 255, 255), "desert": (255, 240, 190, 255)}
BEHAVIOURS = HERE.parents[3] / "Scripts/Gameplay/Enemies/EnemyBehaviours.cs"


def muzzle_of(key):
    """EnemyBehaviours: B("key", ...) ... .Muzzle(x) before the next B( -- default .3 u."""
    import re
    s = BEHAVIOURS.read_text()
    i = s.find(f'B("{key}"')
    if i < 0: return .3
    j = s.find('B("', i + 3); seg = s[i:j if j > 0 else None]
    m = re.search(r"\.Muzzle\(([-\d.]+)f?\)", seg)
    return float(m.group(1)) if m else .3


def close_cut(im):
    """A drawing that runs off its canvas has a flat sliced edge: pad it and close that edge with the outline colour."""
    a = np.array(im.convert("RGBA"))
    op = a[:, :, 3] > 128
    if not op.any(): return im
    ink = a[op][np.argmin(a[op][:, :3].astype(int).sum(1))]
    a = np.pad(a, ((2, 2), (2, 2), (0, 0)))
    op = a[:, :, 3] > 128; H, W = op.shape
    for side in ("top", "bottom", "left", "right"):
        if side == "top" and op[2].any(): a[1][op[2]] = ink
        if side == "bottom" and op[H - 3].any(): a[H - 2][op[H - 3]] = ink
        if side == "left" and op[:, 2].any(): a[:, 1][op[:, 2]] = ink
        if side == "right" and op[:, W - 3].any(): a[:, W - 2][op[:, W - 3]] = ink
    return Image.fromarray(a, "RGBA")


def role(key):
    return next((r for r in ("fighter", "chaser", "alien", "big", "rock") if f"_{r}" in key), "fighter")


def trim(im):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.nonzero(a > 128)
    return im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


def hard(im):
    a = np.array(im); a[:, :, 3] = np.where(a[:, :, 3] > 128, 255, 0); return Image.fromarray(a, "RGBA")


def key_cell(src, old0, cell):
    d = trim(hard(close_cut(src.convert("RGBA"))))
    target = max(trim(old0).size)
    f = target / max(d.size)
    d = d.resize((max(1, round(d.width * f)), max(1, round(d.height * f))), Image.NEAREST)
    c = Image.new("RGBA", (cell, cell))
    c.alpha_composite(d, ((cell - d.width) // 2, (cell - d.height) // 2))
    return c


def signal_mask(im, world):
    a = np.asarray(im).astype(np.float32)
    hsv = np.asarray(im.convert("RGB").convert("HSV")).astype(np.float32)
    h, s, v = hsv[..., 0] * 360 / 255, hsv[..., 1] / 255, hsv[..., 2] / 255
    lo, hi = SIGNAL_HUE[world]
    return (a[..., 3] > 0) & (h >= lo) & (h <= hi) & (s > .45) & (v > .6)


def brighten(im, mask, k):
    a = np.asarray(im).astype(np.float32)
    rgb = a[..., :3]
    rgb[mask] = np.clip(rgb[mask] * (1 + k) + 255 * max(0, k) * .25, 0, 255)
    a[..., :3] = rgb
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def shift(im, dy):
    c = Image.new("RGBA", im.size); c.alpha_composite(im, (0, dy)); return c


def sparks(im, mask, color, n, seed):
    rng = np.random.default_rng(seed)
    out = im.copy(); px = out.load(); W, H = im.size
    ys, xs = np.nonzero(mask)
    if not len(xs): return out
    for i in rng.choice(len(xs), size=min(n, len(xs)), replace=False):
        x, y = int(xs[i]), int(ys[i])
        L = int(rng.integers(7, 13))
        for t in range(-L, L + 1):
            for (xx, yy) in ((x + t, y), (x, y + t)):
                if 0 <= xx < W and 0 <= yy < H and abs(t) <= L:
                    px[xx, yy] = (255, 255, 255, 255) if abs(t) <= 1 else color
    return out


def flash(im, k, seed):
    a = np.asarray(im).astype(np.float32)
    op = a[..., 3] > 0
    a[op, :3] = a[op, :3] * (1 - k) + 255 * k
    out = Image.fromarray(a.astype(np.uint8), "RGBA").copy()
    rng = np.random.default_rng(seed); px = out.load(); W, H = im.size
    ys, xs = np.nonzero(op)
    cy, cx = ys.mean(), xs.mean()
    for _ in range(9):
        ang = rng.uniform(0, 2 * np.pi); r = rng.uniform(.45, .62) * max(np.ptp(xs), np.ptp(ys))
        x, y = int(cx + r * np.cos(ang)), int(cy + r * np.sin(ang))
        for dx in range(3):
            for dy in range(3):
                if 0 <= x + dx < W and 0 <= y + dy < H: px[x + dx, y + dy] = (200, 196, 190, 255)
    return out


def build(key, src_path, install=True):
    world = key.split("_")[0]
    live = LIVE / f"{key}.png"
    OLD.mkdir(exist_ok=True); OUT.mkdir(exist_ok=True); KEYS.mkdir(exist_ok=True)
    if not (OLD / f"{key}.png").exists(): shutil.copy(live, OLD / f"{key}.png")
    old = Image.open(OLD / f"{key}.png").convert("RGBA")
    cell = old.height
    k = key_cell(Image.open(src_path), old.crop((0, 0, cell, cell)), cell)
    k.save(KEYS / f"{key}.png")
    m = signal_mask(k, world)
    frames = []
    for i, (dy, glow) in enumerate(((0, 0), (-1, .18), (-2, .05), (-1, .28))):
        frames.append(shift(brighten(k, m, glow), dy))
    frames.append(sparks(brighten(k, m, .6), m, SPARK[world], 7, 4))
    frames.append(sparks(brighten(k, m, 1.0), m, SPARK[world], 12, 5))
    frames.append(flash(k, .55, 6))
    strip = Image.new("RGBA", (cell * FRAMES, cell))
    for i, f in enumerate(frames): strip.alpha_composite(hard(f), (i * cell, 0))
    strip.save(OUT / f"{key}.png", optimize=True)
    if install: shutil.copy(OUT / f"{key}.png", live)
    # checks
    r = role(key); fw = FRAME_WORLD[r] * FRAME_SCALE.get(key, 1)
    edge_old, edge_new = max(trim(old.crop((0, 0, cell, cell))).size), max(trim(k).size)
    a = np.asarray(k)[:, :, 3] > 128; ys, xs = np.nonzero(a)
    front = (ys.max() + 1 - cell / 2) / cell * fw                      # world u below centre
    muzzle = muzzle_of(key) * TIER
    reds = [(0xD8, 0x23, 0x2C), (0x86, 0x12, 0x1F), (0xFF, 0x5B, 0x45)]
    px = np.asarray(strip)[np.asarray(strip)[:, :, 3] > 128][:, :3].astype(int)
    red = sum(((np.abs(px - np.array(c)).sum(1) <= 24)).sum() for c in reds) / max(1, len(px))
    rep = dict(key=key, edge_old=edge_old, edge_new=edge_new, front_u=round(front, 3), muzzle_u=round(muzzle, 3),
               muzzle_ok=bool(front - .25 <= muzzle <= front + .08), signal_px=int(m.sum()), player_red=round(float(red), 4))
    print(json.dumps(rep))
    return rep


if __name__ == "__main__":
    build(sys.argv[1], sys.argv[2], "--no-install" not in sys.argv)
