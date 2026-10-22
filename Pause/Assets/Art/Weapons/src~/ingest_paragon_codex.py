#!/usr/bin/env python3
"""Puts Codex's Paragon ultimate art into the game's atlases.

    python3 paragon_weapons.py          # script placeholders for everything (fallback / meter row)
    python3 ingest_paragon_codex.py     # then overwrite with Codex's drawings where delivered

Reads the sheets Codex drew (briefs in ShipArt/Paragon/src~/codex, J3):
  charge_16.png            4x4  -> Weapons/Paragon.png row 0 (charge 0..15)
  ready_shot_impact.png    4x4  -> row 1 (ready 0-3 | shot 0-5 | impact 0-5)
  muzzle_release_trail.png 4x3  -> row 2 (muzzle 0-3 | release 0-3 | trail 0-1)
  shot_6.png               3x2  -> row 1 shot 0-5 (REDO: replaces the shot cells of ready_shot_impact)
  meter_16.png             4x4  -> Weapons/SecretMeter.png row 16 (64 px cells: fill 0-9, ready 10-13, flash, icon)
  fx_16.png                4x4  -> Weapons/AttackFx.png row 16 (loop, burst, power loop, power burst)
  gun_tile.png             1x1  -> ShipArt/Guns/ship_gun_roster.png tile 16 (the ultimate gun pod)
Each cell is keyed off its flat #00FF00, Lanczos-fitted to 128 px (the gun
to 313 px) with premultiplied alpha, so edges carry no green fringe.
Missing files are skipped (the scripted placeholder stays).
"""
import os

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.normpath(os.path.join(HERE, "..", ".."))
SRC = os.path.join(ART, "Resources", "ShipArt", "Paragon", "src~", "codex")
WEAPONS = os.path.join(ART, "Resources", "Weapons")
GUNS = os.path.join(ART, "Resources", "ShipArt", "Guns", "ship_gun_roster.png")


def key_green(img):
    a = np.array(img.convert("RGB")).astype(int)
    dist = np.sqrt(a[..., 0] ** 2 + (a[..., 1] - 255) ** 2 + a[..., 2] ** 2)
    alpha = np.clip((dist - 70) * 255 / 90, 0, 255)
    rgb = a.copy()
    rgb[..., 1] = np.minimum(rgb[..., 1], np.maximum(rgb[..., 0], rgb[..., 2]) + 50)
    out = np.dstack([np.clip(rgb, 0, 255), alpha]).astype(np.uint8)
    out[alpha < 8] = 0
    return Image.fromarray(out)


def fit(img, size):
    arr = np.array(img).astype(np.float32)
    al = arr[..., 3:4] / 255.0
    pre = np.concatenate([arr[..., :3] * al, arr[..., 3:4]], axis=2)
    chans = [Image.fromarray(pre[..., i]).resize((size, size), Image.LANCZOS) for i in range(4)]
    pre = np.stack([np.array(c).astype(np.float32) for c in chans], axis=2)
    alpha = np.clip(pre[..., 3], 0, 255)
    alpha[alpha < 12] = 0
    rgb = np.clip(pre[..., :3] / (np.maximum(alpha, 1)[..., None] / 255.0), 0, 255)
    out = np.dstack([rgb, alpha]).astype(np.uint8)
    out[out[..., 3] == 0] = 0
    return Image.fromarray(out)


def cells(path, cols, rows):
    p = os.path.join(SRC, path)
    if not os.path.exists(p):
        return None
    im = Image.open(p).convert("RGB")
    w, h = im.width // cols, im.height // rows
    return [key_green(im.crop((c * w, r * h, (c + 1) * w, (r + 1) * h))) for r in range(rows) for c in range(cols)]


def place(atlas, frames, row, col0, cell=128, count=None):
    for i, f in enumerate(frames[:count]):
        x, y = (col0 + i) * cell, row * cell
        atlas.paste(Image.new("RGBA", (cell, cell), (0, 0, 0, 0)), (x, y))
        atlas.alpha_composite(fit(f, cell), (x, y))


def main():
    path = os.path.join(WEAPONS, "Paragon.png")
    atlas = Image.open(path).convert("RGBA")
    charge = cells("charge_16.png", 4, 4)
    if charge:
        place(atlas, charge, 0, 0)
    rsi = cells("ready_shot_impact.png", 4, 4)
    if rsi:
        place(atlas, rsi[0:4], 1, 0)
        place(atlas, rsi[4:10], 1, 4)
        place(atlas, rsi[10:16], 1, 10)
    shots = cells("shot_6.png", 3, 2)
    if shots:
        place(atlas, shots, 1, 4)
    mrt = cells("muzzle_release_trail.png", 4, 3)
    if mrt:
        place(atlas, mrt[0:4], 2, 0)
        place(atlas, mrt[4:8], 2, 4)
        place(atlas, mrt[8:10], 2, 8)
    atlas.save(path, optimize=True)

    fx = cells("fx_16.png", 4, 4)
    if fx:
        p = os.path.join(WEAPONS, "AttackFx.png")
        im = Image.open(p).convert("RGBA")
        place(im, fx, 15, 0)
        im.save(p, optimize=True)

    mt = cells("meter_16.png", 4, 4)
    if mt:
        p = os.path.join(WEAPONS, "SecretMeter.png")
        im = Image.open(p).convert("RGBA")
        im.paste(Image.new("RGBA", (1024, 64), (0, 0, 0, 0)), (0, 15 * 64))
        for i, f in enumerate(mt):
            im.alpha_composite(fit(f, 64), (i * 64, 15 * 64))
        im.save(p, optimize=True)

    gp = os.path.join(SRC, "gun_tile.png")
    if os.path.exists(gp):
        roster = Image.open(GUNS).convert("RGBA")
        cell = 1254 / 4.0
        tile = fit(key_green(Image.open(gp)), int(round(cell)))
        x0, y0 = int(3 * cell), int(3 * cell)        # slot 15: right column, bottom row
        roster.paste(Image.new("RGBA", (int(round(cell)),) * 2, (0, 0, 0, 0)), (x0, y0))
        roster.alpha_composite(tile, (x0, y0))
        roster.save(GUNS, optimize=True)
    print("ingested", [n for n, v in (("charge", charge), ("ready/shot/impact", rsi), ("muzzle/release/trail", mrt),
                                      ("shot redo", shots), ("meter", mt), ("fx", fx), ("gun", os.path.exists(gp))) if v])


if __name__ == "__main__":
    main()
